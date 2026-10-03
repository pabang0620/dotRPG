// 자동 매칭(M1~M3)과 1초 틱. 대기열은 QueueStore(메모리), 맞춰진 결과만 parties 행이 된다.
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation, withTransaction } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { powerOf } from '../characters/powerEstimate';
import * as charRepo from '../characters/characterRepository';
import * as dungeonRepo from '../dungeons/dungeonRepository';
import { checkEntry } from '../dungeons/entryRules';
import * as econRepo from '../economy/economyRepository';
import type { StoredResult } from '../economy/economyService';
import * as partyRepo from '../party/partyRepository';
import { runParty } from '../party/partyTx';
import { buildPartyView } from '../party/partyView';
import { pickGroups } from './matchRules';
import { getQueueStore, type QueueTicket } from './queueStore';
import type { QueueBody, RequestOnlyBody } from './matchValidation';
import { logger } from '../../utils/logger';

const running = new Set<string>();

class NoMembers extends Error {}

function queueView(t: QueueTicket) {
  const pol = getConfig().policy;
  const humans = getQueueStore().listByKey(t.dungeonId, t.difficulty).length;
  return {
    dungeon_id: t.dungeonId,
    difficulty: t.difficulty,
    queued_at: t.queuedAt.toISOString(),
    depart_at: new Date(t.queuedAt.getTime() + pol.matchQueueSeconds * 1000).toISOString(),
    humans_waiting: humans,
    fill_ai_available: humans < 4,
  };
}

// ---------- M1 POST /match/queue ----------

export async function enqueue(accountId: number, characterUuid: string, body: QueueBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  const r = await runParty({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/match/queue',
    requestId,
    payload,
    handler: async (ctx) => {
      const store = getQueueStore();
      const partyId = await partyRepo.findPartyIdOf(ctx.client, ctx.char.id);
      if (partyId !== null) {
        const p = await partyRepo.getParty(ctx.client, partyId);
        throw new AppError(409, '이미 파티에 있습니다.', 'ALREADY_IN_PARTY', { party_id: p?.uuid });
      }
      if (await dungeonRepo.inPartyRun(ctx.client, ctx.char.id)) throw new AppError(409, '파티 판에 참여 중입니다.', 'IN_PARTY_RUN');
      const existing = store.get(ctx.char.id);
      if (existing) {
        existing.lastPollAt = ctx.now;
        return { status: 200, data: { queue: queueView(existing) } };
      }
      await checkEntry(ctx.client, ctx.char, body.dungeon_id, body.difficulty, ctx.now);
      const t: QueueTicket = {
        characterId: ctx.char.id,
        accountId: ctx.char.accountId,
        dungeonId: body.dungeon_id,
        difficulty: body.difficulty,
        level: ctx.char.level,
        power: await powerOf(ctx.client, ctx.char.id, ctx.char.class, ctx.char.level),
        queuedAt: ctx.now,
        lastPollAt: ctx.now,
        forceDepart: false,
      };
      store.put(t);
      return { status: 200, data: { queue: queueView(t) } };
    },
  });
  if (!r.replay) await matchKey(body.dungeon_id, body.difficulty);
  return r;
}

// ---------- M2 DELETE /match/queue ----------

export async function cancelQueue(accountId: number, characterUuid: string) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  getQueueStore().remove(c.id);
  return { cancelled: true };
}

// ---------- M3 POST /match/fill-ai ----------

export async function fillAi(accountId: number, characterUuid: string, _body: RequestOnlyBody) {
  const c = await charRepo.findOwnedAlive(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const ticket = getQueueStore().get(c.id);
  if (!ticket) {
    // 재전송: 이미 맞춰진 파티가 있으면 같은 결과를 돌려준다
    const pid = await partyRepo.findPartyIdOf(getPool(), c.id);
    const p = pid === null ? null : await partyRepo.getParty(getPool(), pid);
    if (p && p.source === 'match' && p.state === 'forming') return { party: await buildPartyView(getPool(), p, c.id, getNow()) };
    throw new AppError(404, '대기 중이 아닙니다.', 'NOT_QUEUED');
  }
  ticket.forceDepart = true;
  await matchKey(ticket.dungeonId, ticket.difficulty);
  const pid = await partyRepo.findPartyIdOf(getPool(), c.id);
  const p = pid === null ? null : await partyRepo.getParty(getPool(), pid);
  if (!p) throw new AppError(409, '파티를 만들지 못했습니다.', 'MATCH_FAILED');
  return { party: await buildPartyView(getPool(), p, c.id, getNow()) };
}

// ---------- 맞추기 ----------

/** 같은 (던전, 난이도)의 대기열을 한 번 돌려 파티를 만든다 */
export async function matchKey(dungeonId: string, difficulty: number): Promise<void> {
  const k = `${dungeonId}:${difficulty}`;
  if (running.has(k)) return;
  running.add(k);
  try {
    const pol = getConfig().policy;
    const now = getNow();
    const store = getQueueStore();
    for (const t of store.listByKey(dungeonId, difficulty)) {
      // 폴링이 끊긴(접속이 끊긴) 티켓은 버린다
      if (now.getTime() - t.lastPollAt.getTime() > pol.matchTicketStaleSeconds * 1000) store.remove(t.characterId);
    }
    const groups = pickGroups(store.listByKey(dungeonId, difficulty), now.getTime(), pol.matchPowerRatio, pol.matchQueueSeconds * 1000);
    for (const g of groups) await formParty(g, now);
  } finally {
    running.delete(k);
  }
}

async function formParty(group: QueueTicket[], now: Date): Promise<void> {
  const store = getQueueStore();
  const pol = getConfig().policy;
  const alive = group.filter((t) => store.get(t.characterId) === t);
  if (alive.length === 0) return;
  try {
    await withTransaction(async (client) => {
      const locked = await econRepo.lockCharacters(client, alive.map((t) => t.characterId).sort((a, b) => a - b));
      const valid: { t: QueueTicket; c: econRepo.LockedChar }[] = [];
      for (const t of alive) {
        const c = locked.find((x) => x.id === t.characterId);
        if (!c) continue;
        if ((await partyRepo.findPartyIdOf(client, c.id)) !== null || (await dungeonRepo.inPartyRun(client, c.id))) continue;
        // 그 사이 자격을 잃은 사람(입장 횟수 소진 등)은 빼고 나머지로 만든다
        try {
          await checkEntry(client, c, t.dungeonId, t.difficulty, now);
        } catch (err) {
          if (err instanceof AppError) continue;
          throw err;
        }
        valid.push({ t, c });
      }
      if (valid.length === 0) return;
      const seed = valid[0] as (typeof valid)[number];
      const party = await partyRepo.insertParty(client, {
        leaderId: seed.c.id,
        dungeonId: seed.t.dungeonId,
        difficulty: seed.t.difficulty,
        maxMembers: 4,
        minPower: 0,
        message: '',
        source: 'match',
        listed: false,
        listedUntil: null,
        startBy: new Date(now.getTime() + pol.matchStartSeconds * 1000),
        now,
      });
      // 멤버마다 SAVEPOINT: 그 사이 다른 파티에 들어간 사람(UNIQUE 위반)만 건너뛴다
      const joined: typeof valid = [];
      for (const v of valid) {
        await client.query('SAVEPOINT match_member');
        try {
          await partyRepo.insertMember(client, party.id, v.c.id, v.c.accountId, true, now);
          await client.query('RELEASE SAVEPOINT match_member');
          joined.push(v);
        } catch (err) {
          if (!isUniqueViolation(err, 'party_members_one_active')) throw err;
          await client.query('ROLLBACK TO SAVEPOINT match_member');
        }
      }
      if (joined.length === 0) throw new NoMembers();
      // 방장(시드)이 빠졌으면 먼저 들어간 사람이 방장
      const first = joined[0] as (typeof joined)[number];
      if (!joined.includes(seed)) await partyRepo.setLeader(client, party.id, first.c.id);
    });
  } catch (err) {
    // 아무도 못 넣었으면 파티째 롤백된 것이다(티켓은 아래에서 정리)
    if (!(err instanceof NoMembers)) throw err;
  } finally {
    // 만들어졌든 자격을 잃었든 이 티켓들은 대기열을 떠난다
    for (const t of alive) store.remove(t.characterId);
  }
}

/** 서버가 1초마다 부른다 */
export async function runMatchTick(): Promise<void> {
  for (const k of getQueueStore().keys()) {
    try {
      await matchKey(k.dungeonId, k.difficulty);
    } catch (err) {
      logger.error({ err }, 'match tick failed');
    }
  }
}
