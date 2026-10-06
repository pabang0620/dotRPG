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
import { computePower } from './powerEstimate';
import { listWornKeys } from '../economy/economyRepository';
import * as repo from './characterRepository';
import { validateState } from './stateRules';
import { getGrant } from './careerGrantRepository';
import { getConfig } from '../../config/env';
import { checkReservedName } from '../antiabuse/reservedNames';
import type { CreateCharacterBody, StateBody } from './characterValidation';

const NAME_UNIQUE = 'characters_name_alive';
const CREATE_ENDPOINT = 'POST /characters';
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
    })),
    // 3단계: 서버가 정본인 상태(퀘스트 청구, 상자, 강화 천장, 납품). required는 서버 데이터
    bonus_max_health: econ.bonusMaxHealth,
    claimed_quests: econ.claimedQuests,
    opened_chests: econ.openedChests,
    enhance_pity: econ.pity,
    deliveries: Object.entries(eco.config.deliverySites).map(([siteId, site]) => ({
      site_id: siteId,
      items: site.items.map((i) => ({
        item_key: i.itemKey,
        delivered: econ.deliveries.find((d) => d.site_id === siteId && d.item_key === i.itemKey)?.delivered ?? 0,
        required: i.required,
      })),
    })),
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

