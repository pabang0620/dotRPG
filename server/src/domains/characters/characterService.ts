import type { PoolClient } from 'pg';
import {
  REQUEST_LOG_UNIQUE,
  findRequest,
  hashRequest,
  saveRequest,
} from '../../db/idempotency';
import { getPool, isUniqueViolation, withTransaction, type Queryable } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';
import { CHARACTER_LIMIT } from '../auth/authService';
import { hasActiveAuction } from '../auction/auctionSearchRepository';
import { lockCharacter, readEconomyDetail } from '../economy/economyRepository';
import { hasOpenMail } from '../mail/mailRepository';
import { detachCharacter as detachFromGuild } from '../guilds/guildService';
import { getNow } from '../../utils/clock';
import { computePower } from './powerEstimate';
import { listWornKeys } from '../economy/economyRepository';
import * as repo from './characterRepository';
import { validateState } from './stateRules';
import { usesStyle3 } from './careerRules';
import { style3Owned } from '../skillstyles/skillStyleRepository';
import { getGrant } from './careerGrantRepository';
import { getConfig } from '../../config/env';
import { checkReservedName } from '../antiabuse/reservedNames';
import type { CreateCharacterBody, RenameCharacterBody, StateBody } from './characterValidation';

const NAME_UNIQUE = 'characters_name_alive';
const CREATE_ENDPOINT = 'POST /characters';
const RENAME_ENDPOINT = 'POST /characters/:uuid/rename';
const NOT_FOUND = () => new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');

export interface ApiBody {
  success: true;
  message: string;
  data: unknown;
}
export interface StoredResult {
  status: number;
  body: ApiBody;
  replay: boolean;
}

async function buildDetail(db: Parameters<typeof repo.getState>[0], c: repo.CharacterRow) {
  // db가 트랜잭션 클라이언트일 수 있어 하나씩 읽는다(한 클라이언트에 동시 query 금지)
  const s = await repo.getState(db, c.id);
  const items = await repo.getItems(db, c.id);
  const econ = await readEconomyDetail(db, c.id);
  const eco = getGameData().economy;
  return {
    id: c.uuid,
    name: c.name,
    class: c.class,
    created_at: c.created_at.toISOString(),
    rename_required: c.rename_required,
    rename_note: c.rename_note,
    level: c.level,
    xp: c.xp,
    gold: c.gold,
    state: {
      version: s.version,
      map_id: s.map_id,
      pos: s.pos_x === null || s.pos_y === null ? null : { x: s.pos_x, y: s.pos_y },
      facing: s.facing,
      quests: s.quests,
      story_flags: s.story_flags,
      tracked_quest: s.tracked_quest,
      passives: s.passives,
      skill_gems: s.skill_gems,
      career: s.career ?? null,
      updated_at: s.updated_at.toISOString(),
    },
    items: items.map((i) => ({
      id: i.uuid,
      item_key: i.item_key,
      count: i.count,
      location: i.location,
      slot: i.slot,
      bind: i.bind, // 교환 창이 귀속 아이템을 목록에서 뺀다
    })),
    // 3단계: 서버가 정본인 상태(퀘스트 청구, 상자, 강화 천장)
    bonus_max_health: econ.bonusMaxHealth,
    claimed_quests: econ.claimedQuests,
    opened_chests: econ.openedChests,
    enhance_pity: econ.pity,
    storage_capacity: eco.config.storageCapacity,
    // 4단계: 서버가 레벨·착용 장비로 계산한 전투력(패시브 제외라 캐릭터 카드 값과 다를 수 있다)
    power_estimate: computePower(c.class, c.level, await listWornKeys(db, c.id), econ.bonusMaxHealth),
  };
}

export async function listCharacters(accountId: number) {
  const rows = await repo.listAlive(accountId);
  return {
    characters: rows.map((r) => ({
      id: r.uuid,
      name: r.name,
      class: r.class,
      level: r.level,
      rename_required: r.rename_required,
      rename_note: r.rename_note,
      map_id: r.map_id,
      created_at: r.created_at.toISOString(),
      pos: r.pos_x === null || r.pos_y === null ? null : { x: r.pos_x, y: r.pos_y },
      updated_at: r.updated_at.toISOString(),
    })),
    limit: CHARACTER_LIMIT,
  };
}

export async function getCharacter(accountId: number, uuid: string) {
  const c = await repo.findOwnedAlive(getPool(), accountId, uuid);
  if (!c) throw NOT_FOUND();
  return { character: await buildDetail(getPool(), c) };
}

export async function deleteCharacter(accountId: number, uuid: string) {
  // 6단계: 진행 중 등록·최고 입찰·미수령 우편이 있으면 삭제할 수 없다(자산 증발 방지).
  // 캐릭터 행을 잠가 같은 캐릭터의 등록·입찰 요청과 직렬화한다.
  const r = await withTransaction(async (client) => {
    const c = await lockCharacter(client, accountId, uuid);
    if (c && ((await hasActiveAuction(client, c.id)) || (await hasOpenMail(client, c.id)))) {
      throw new AppError(409, '진행 중인 경매나 받지 않은 우편이 있어 삭제할 수 없습니다.', 'CHARACTER_HAS_AUCTION');
    }
    // 0048: 길드장은 위임·해체 뒤에 삭제할 수 있고, 길드원은 같은 트랜잭션에서 탈퇴 처리한다(인원 수·신청 정리)
    if (c) await detachFromGuild(client, c.id, getNow(), 'refuse_leader');
    return repo.softDelete(accountId, uuid, client);
  });
  if (r === 'missing') throw NOT_FOUND();
  return { deleted: true };
}

// ---------- 생성 + 시작 지급 ----------

async function grantStarter(client: PoolClient, c: repo.CharacterRow): Promise<void> {
  const starter = getGameData().starter;
  if (starter.gold > 0) {
    const before = await repo.lockCharacterGold(client, c.id);
    const after = before + starter.gold;
    await repo.updateGold(client, c.id, after);
    await repo.insertGoldLedger(client, c.id, starter.gold, after, 'starter', c.uuid);
  }
  const bindOf = (key: string) => getGameData().economy.items.get(key)?.bind ?? 'none';
  for (const it of starter.items) {
    await repo.insertItem(client, c.id, it.itemKey, it.count, 'bag', null, bindOf(it.itemKey));
    await repo.insertItemLedger(client, c.id, it.itemKey, it.count, 'starter', c.uuid, 'bag', it.count);
  }
  const gear = starter.gear[c.class];
  if (gear) {
    await repo.insertItem(client, c.id, gear.itemKey, 1, 'worn', gear.slot, bindOf(gear.itemKey));
    await repo.insertItemLedger(client, c.id, gear.itemKey, 1, 'starter', c.uuid, 'worn', 1);
  }
}

async function replayIfStored(
  client: Queryable,
  accountId: number,
  requestId: string,
  hash: string,
): Promise<StoredResult | null> {
  const stored = await findRequest(client, accountId, requestId);
  if (!stored) return null;
  if (stored.requestHash !== hash || stored.endpoint !== CREATE_ENDPOINT) {
    throw new AppError(
      422,
      '같은 request_id로 다른 요청을 보낼 수 없습니다.',
      'IDEMPOTENCY_MISMATCH',
    );
  }
  return { status: stored.statusCode, body: stored.response as ApiBody, replay: true };
}

export async function createCharacter(
  accountId: number,
  input: CreateCharacterBody,
): Promise<StoredResult> {
  const hash = hashRequest({ name: input.name, class: input.class });
  // 9단계 10: 운영 예약어·금칙어 이름은 거절한다(길이·문자 검증을 통과한 뒤, 중복 검사 전)
  checkReservedName(input.name);
  try {
    return await withTransaction(async (client) => {
      // 계정 행 잠금: 같은 계정의 동시 생성(슬롯 초과, 같은 request_id)을 한 줄로 세운다
      await repo.lockAccount(client, accountId);

      const replay = await replayIfStored(client, accountId, input.request_id, hash);
      if (replay) return replay;

      if ((await repo.countAlive(client, accountId)) >= CHARACTER_LIMIT) {
        throw new AppError(422, '캐릭터 슬롯이 가득 찼습니다.', 'CHARACTER_LIMIT_REACHED');
      }

      if ((await repo.countCreatedLastDay(client, accountId)) >= getConfig().rate.charCreateDaily) {
        throw new AppError(429, '하루에 만들 수 있는 캐릭터 수를 넘었습니다. 내일 다시 시도해 주세요.', 'CHARACTER_CREATE_DAILY_LIMIT');
      }

      let character: repo.CharacterRow;
      try {
        character = await repo.insertCharacter(client, accountId, input.name, input.class);
      } catch (err) {
        if (isUniqueViolation(err, NAME_UNIQUE)) {
          throw new AppError(409, '이미 사용 중인 이름입니다.', 'NAME_TAKEN');
        }
        throw err;
      }
      await repo.insertInitialState(client, character.id, getGameData().starter.startMap);
      await grantStarter(client, character);

      // 지급 후 상태(골드 포함)로 응답을 만든다
      const fresh = await repo.findOwnedAlive(client, accountId, character.uuid);
      const body: ApiBody = {
        success: true,
        message: '',
        data: { character: await buildDetail(client, fresh as repo.CharacterRow) },
      };
      await saveRequest(client, accountId, input.request_id, CREATE_ENDPOINT, hash, 201, body);
      return { status: 201, body, replay: false };
    });
  } catch (err) {
    // 락을 우회한 UNIQUE 충돌(이론상 드묾): 먼저 끝난 요청의 응답을 돌려준다
    if (isUniqueViolation(err, REQUEST_LOG_UNIQUE)) {
      const replay = await replayIfStored(getPool(), accountId, input.request_id, hash);
      if (replay) return replay;
    }
    throw err;
  }
}

// ---------- 이름 변경 요청(0038) ----------

/**
 * 운영자가 이름 변경을 요청한 캐릭터(rename_required)만 새 이름을 고를 수 있다. 생성과 같은 이름 규칙·예약어·중복 검사.
 * 같은 request_id 재전송은 처음 응답을 돌려준다.
 */
export async function renameCharacter(accountId: number, uuid: string, input: RenameCharacterBody): Promise<StoredResult> {
  const hash = hashRequest({ uuid, name: input.name });
  checkReservedName(input.name);
  return withTransaction(async (client) => {
    await repo.lockAccount(client, accountId);
    const stored = await findRequest(client, accountId, input.request_id);
    if (stored) {
      if (stored.requestHash !== hash || stored.endpoint !== RENAME_ENDPOINT) {
        throw new AppError(422, '같은 request_id로 다른 요청을 보낼 수 없습니다.', 'IDEMPOTENCY_MISMATCH');
      }
      return { status: stored.statusCode, body: stored.response as ApiBody, replay: true };
    }
    const c = await repo.lockOwnedAlive(client, accountId, uuid);
    if (!c) throw NOT_FOUND();
    if (!c.rename_required) throw new AppError(409, '이름을 바꿀 수 있는 상태가 아닙니다.', 'RENAME_NOT_REQUIRED');
    await client.query('SAVEPOINT rename_try');
    try {
      await repo.applyRename(client, c.id, input.name);
      await client.query('RELEASE SAVEPOINT rename_try');
    } catch (err) {
      await client.query('ROLLBACK TO SAVEPOINT rename_try');
      if (isUniqueViolation(err, NAME_UNIQUE)) throw new AppError(409, '이미 사용 중인 이름입니다.', 'NAME_TAKEN');
      throw err;
    }
    const body: ApiBody = { success: true, message: '', data: { character: { id: c.uuid, name: input.name, rename_required: false, rename_note: null } } };
    await saveRequest(client, accountId, input.request_id, RENAME_ENDPOINT, hash, 200, body);
    return { status: 200, body, replay: false };
  });
}

// ---------- 상태 저장 ----------

export async function saveState(accountId: number, uuid: string, input: StateBody) {
  const data = getGameData();
  return withTransaction(async (client) => {
    const c = await repo.findOwnedAlive(client, accountId, uuid);
    if (!c) throw NOT_FOUND();
    // 상태 행을 잠가 같은 캐릭터의 동시 저장을 한 줄로 세운다
    const stored = await repo.getStateForUpdate(client, c.id);
    // 레벨·직업은 서버 값. 요청 값이 아니다
    let passives: string[];
    try {
      // 9단계 E9: 서버가 부여한 전직·각성 기록과 일치하는 값만 받는다(CAREER_SERVER_TRUTH off|log|enforce)
      const truth = getConfig().aa.career.serverTruth;
      const grant = truth === 'off' ? null : await getGrant(client, c.id);
      ({ passives } = validateState(data, input, { level: c.level, class: c.class }, stored, truth === 'off' ? undefined : { granted: grant ? { career: grant.career, stage: grant.stage } : null, mode: truth }));
      // 스킬 스타일 3은 계정이 별조각으로 해금했어야 쓴다(Docs/PLAN_SKILL_STYLES.md 4절)
      if (usesStyle3(input.career) && !(await style3Owned(client, accountId))) {
        throw new AppError(422, '스킬 스타일 3이 잠겨 있습니다.', 'SKILL_STYLE_LOCKED');
      }
    } catch (err) {
      // 저장 거절은 진행이 사라지는 원인이 되므로 사유를 남긴다
      if (err instanceof AppError) logger.warn({ character: uuid, code: err.code, extra: err.extra }, 'state save rejected');
      throw err;
    }
    const saved = await repo.updateStateIfVersion(client, c.id, input.version, {
      mapId: input.map_id,
      posX: input.pos ? input.pos.x : null,
      posY: input.pos ? input.pos.y : null,
      facing: input.facing,
      quests: input.quests,
      storyFlags: input.story_flags,
      trackedQuest: input.tracked_quest,
      passives,
      skillGems: input.skill_gems,
      career: input.career ?? stored.career,
    });
    if (!saved) {
      throw new AppError(409, '다른 곳에서 저장된 상태가 있습니다. 다시 불러와 주세요.', 'VERSION_CONFLICT', {
        current_version: stored.version,
      });
    }
    return { version: saved.version, updated_at: saved.updated_at.toISOString() };
  });
}

