// 필드 파티 세션 맥락의 처치 보고(phase8_api.md 6.5): 세션 멤버십, 파티 속도, 세션 화력 상한, 기여 부채 게이트, 레벨 격차 감쇠.
import { getConfig } from '../../config/env';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import type { EconCtx } from '../economy/economyContext';
import { fieldRefExists } from '../kills/killRepository';
import { rejected, resolveFieldTarget, type KillTarget } from '../kills/killTarget';
import * as repo from './fieldRepository';
import { carryReferenceLevel, isHardGap, xpFactor } from './xpFactor';

const invalid = () => new AppError(409, '필드 세션이 유효하지 않습니다.', 'FIELD_SESSION_INVALID');

export async function resolveSessionTarget(
  ctx: EconCtx,
  sessionUuid: string,
  mapId: string,
  monsterId: string,
  monsterRef: number | undefined,
): Promise<KillTarget> {
  const cfg = getConfig();
  const fc = cfg.field;
  const client = ctx.client;
  // 1. 세션 멤버십(캐릭터 행을 이미 잠갔다: 락 순서 캐릭터 -> 세션)
  const session = await repo.lockByUuid(client, sessionUuid);
  const me = session ? await repo.memberRow(client, session.id, ctx.char.id) : null;
  if (!session || session.state !== 'active' || !me || me.state === 'left' || session.map_id !== mapId) throw invalid();
  const members = await repo.activeMembers(client, session.id);
  const n = members.length;
  // 일시 차단은 호스트에게만 쌓는다(정직한 멤버가 호스트의 몰이 때문에 막히지 않게)
  const nonHost = session.host_character_id !== ctx.char.id;
  // 2. 맵·몬스터·리스폰 공급 상한(캐릭터마다 센다, 3단계 그대로). 연출 스폰은 거절
  const base = await resolveFieldTarget(client, ctx.char.id, mapId, monsterId, ctx.now, { sessionOnly: true, nonHost });
  // 3. 같은 몬스터 이중 보고
  if (monsterRef !== undefined && (await fieldRefExists(client, session.id, ctx.char.id, monsterRef))) {
    throw new AppError(409, '이미 보고한 몬스터입니다.', 'KILL_DUPLICATE');
  }
  // 5. 처치 대조 게이트: 활성 멤버 2명 이상이고 호스트 관찰이 신선할 때만(관찰이 오래 없으면 공급·화력·속도 상한만 남는다)
  if (n >= 2) {
    const refAt = session.last_observe_at ?? session.created_at;
    if (ctx.now.getTime() - refAt.getTime() <= fc.observeGraceSeconds * 1000) {
      const debt = me.kills_accepted - me.kills_credited;
      if (session.last_observe_at && debt >= fc.uncreditedMax) {
        throw rejected('field_uncredited', nonHost ? 1 : 2, { session_id: sessionUuid, monster_id: monsterId, debt, max: fc.uncreditedMax });
      }
    } else {
      await noteMissingObserve(ctx, session, members);
    }
  }
  // 6. 화력 상한: 활성 멤버 attackCap 합 x 여유(파티 전체의 딜이 들어간다). 혼자면 3단계 방식
  // 합에는 연결·환영이 끝난(playing) 멤버만 넣는다(보고하는 본인은 항상 포함). 한 멤버는 자기 상한 x (1 + 알파)를 넘겨 기여받지 못한다
  const playingSum = members.filter((m) => m.state === 'playing' && m.character_id !== ctx.char.id).reduce((a, m) => a + m.attack_cap, 0);
  const own = me.attack_cap;
  const powerCap = n >= 2 ? Math.min((own + playingSum) * fc.powerSlack, own * (1 + fc.memberPowerAlpha)) : null;
  // 7. 레벨 격차 감쇠(활성 멤버 2명 이상일 때만). 경제 손잡이는 기본 1.0
  const gameData = getGameData();
  // Only the underground world's impossible-to-reach post-cap levels are normalized for carry rules.
  // Combat HP, actual level, drop tier and kill power checks continue to use base.level.
  const carryLevel = carryReferenceLevel(base.level, gameData.maps.get(base.mapId)?.worldLayer, gameData.economy.progression.maxLevel);
  let factor: number | null = null;
  let dropMul = 1;
  let hardXp = false;
  if (n >= 2) {
    factor = Math.round(xpFactor(carryLevel, ctx.level, fc) * fc.partyXpFactor * 1000) / 1000;
    // 호스트 관찰이 오래 끊긴 세션(고장 난 호스트 클라이언트가 게이트를 영구히 끄는 것을 막는다): 경험치 배율을 낮추고 한 번 기록한다
    const lapsed = ctx.now.getTime() - (session.last_observe_at ?? session.created_at).getTime() > fc.observeLapseMinutes * 60_000;
    if (lapsed) {
      factor = Math.round(Math.min(factor, fc.observeLapseXpFactor) * 1000) / 1000;
      await noteLapse(ctx, session, members);
    }
    dropMul = Math.round(factor * fc.partyDropFactor * 1000) / 1000;
    // 9단계: 하드 격차는 경험치 1과 재료·장비 드롭 배율 FIELD_CARRY_HARD_DROP_MUL 로 막는다(고레벨 사냥터에 저레벨을 끌고 가는 캐리의 끝)
    if (isHardGap(carryLevel, ctx.level, fc)) {
      hardXp = true;
      dropMul = fc.carryHardDropMul;
    }
  }
  // 4. 속도: 1초 창 한도를 세션 인원에 맞춘다
  const burst = cfg.policy.killBurstField + fc.killBurstPerExtra * Math.max(0, n - 1);
  return {
    ...base,
    burst,
    powerCap,
    nonHost,
    field: { sessionId: session.id, monsterRef: monsterRef ?? null, xpFactor: factor, dropMul, hardXp },
    commit: async (c) => {
      await repo.addAccepted(c, session.id, ctx.char.id);
      await repo.touchSession(c, session.id, ctx.now);
    },
  };
}

/** 호스트 관찰이 30초 넘게 없으면 이상 기록(severity 1)을 세션당 한 번 남긴다 */
async function noteMissingObserve(ctx: EconCtx, session: repo.SessionRow, members: repo.FMemberRow[]): Promise<void> {
  const host = members.find((m) => m.character_id === session.host_character_id);
  if (!host) return;
  const seen = await ctx.client.query(
    "SELECT 1 FROM anomaly_log WHERE kind = 'field_host' AND character_id = $1 AND detail->>'session_id' = $2 AND detail->>'why' = 'observe_missing' LIMIT 1",
    [host.character_id, session.uuid],
  );
  if (seen.rows.length > 0) return;
  await ctx.client.query(
    "INSERT INTO anomaly_log (account_id, character_id, kind, severity, detail) VALUES ($1, $2, 'field_host', 1, $3::jsonb)",
    [host.account_id, host.character_id, JSON.stringify({ session_id: session.uuid, why: 'observe_missing' })],
  );
}

/** 관찰이 N분 넘게 없으면 이상 기록(severity 2)을 세션당 한 번 남긴다 */
async function noteLapse(ctx: EconCtx, session: repo.SessionRow, members: repo.FMemberRow[]): Promise<void> {
  const host = members.find((m) => m.character_id === session.host_character_id) ?? members[0];
  if (!host) return;
  const seen = await ctx.client.query(
    "SELECT 1 FROM anomaly_log WHERE kind = 'field_host' AND severity = 2 AND detail->>'session_id' = $1 AND detail->>'why' = 'observe_lapsed' LIMIT 1",
    [session.uuid],
  );
  if (seen.rows.length > 0) return;
  await ctx.client.query(
    "INSERT INTO anomaly_log (account_id, character_id, kind, severity, detail) VALUES ($1, $2, 'field_host', 2, $3::jsonb)",
    [host.account_id, host.character_id, JSON.stringify({ session_id: session.uuid, why: 'observe_lapsed' })],
  );
}
