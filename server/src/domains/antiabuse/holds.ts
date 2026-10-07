// 경제 정지(12.4~12.7): 생성, 차단 헬퍼, 연결 계정 전파, 캐릭터·경매 평가.
// 정지 중에는 "얻는 것"은 막지 않고 "쓰는 것·옮기는 것"(경매 등록·구매·입찰, 상점 판매, 강화, 별조각 사용, 우편 수령)만 막는다(12.5, 12.6).
import { getConfig } from '../../config/env';
import { getPool, isUniqueViolation, type Queryable } from '../../db/pool';
import { getIncomeCaps } from '../../gamedata/antiAbuseData';
import { AppError } from '../../utils/AppError';
import { getNow } from '../../utils/clock';
import { logger } from '../../utils/logger';
import * as repo from './holdsRepository';
import type { HoldRow } from './holdsRepository';
import { HOUR_MS } from './incomeMeter';
import { auctionViolation, bandOf, capsFor, evaluateBuckets, firstBucketAt, loadBuckets, WINDOW_HOURS, WINDOWS, type WindowEval, type WindowKind } from './velocity';

export const HOLD_MESSAGE = '경제 활동이 일시적으로 제한되었습니다. 문의해 주세요.';
const DAY_MS = 24 * HOUR_MS;

/**
 * 정지 대상 7개 경로의 맨 앞에서 부른다(캐릭터 행을 잠근 뒤).
 * 정지 행을 FOR SHARE로 잠그지 않는다: 회수(clawback)는 정지 행을 먼저 잠그고 캐릭터 행을 잠그므로, 여기서 캐릭터 -> 정지 순으로 잠그면 교착이 난다.
 * 커밋된 정지는 READ COMMITTED의 이 SELECT에 보이므로 캐릭터 락 이후에 만들어진 정지는 같은 요청에 적용되고, 아직 커밋 전인 정지는 다음 요청부터 막는다. 근거 수치는 절대 싣지 않는다(우회 학습 방지) */
export async function assertNoHold(db: Queryable, accountId: number, characterId: number): Promise<void> {
  const h = await repo.findBlocking(db, accountId, characterId);
  if (h) throw new AppError(403, HOLD_MESSAGE, 'ECONOMY_HOLD', { scope: h.character_id === null ? 'account' : 'character', since: h.created_at.toISOString() });
}

/** P3: 내 경제 정지 여부(수치 없음) */
export async function holdStatusOf(accountId: number, characterUuid: string): Promise<{ on_hold: boolean; since: string | null; scope: 'character' | 'account' | null }> {
  const c = await repo.ownedCharacter(getPool(), accountId, characterUuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const h = await repo.findBlocking(getPool(), accountId, c.id);
  return h
    ? { on_hold: true, since: h.created_at.toISOString(), scope: h.character_id === null ? 'account' : 'character' }
    : { on_hold: false, since: null, scope: null };
}

export interface CreateSpec {
  accountId: number;
  characterId: number | null;
  kind: 'velocity' | 'auction' | 'linked' | 'manual';
  evidence: Record<string, unknown>;
  windowKind?: string | null;
  windowStart?: Date | null;
  windowEnd?: Date | null;
  originHoldId?: number | null;
  /** 수동·연결 정지는 모드를 따르지 않는다(state를 직접 준다) */
  forceState?: 'shadow' | 'active';
  note?: string | null;
  now?: Date;
}

export interface CreateResult {
  state: 'shadow' | 'active';
  hold: HoldRow;
  /** 이번 호출이 새로 만들었는가(이미 있어서 건너뛴 것이면 false) */
  created: boolean;
}

/** 모드에 맞는 정지 한 건을 만든다. off면 null. 같은 범위에 막는 정지가 이미 있으면 그것을, shadow는 같은 대상에 DEDUPE 시간당 한 줄만 쓴다 */
export async function createHold(spec: CreateSpec): Promise<CreateResult | null> {
  const cfg = getConfig().aa.hold;
  const db = getPool();
  const now = spec.now ?? getNow();
  const state = spec.forceState ?? (cfg.mode === 'enforce' ? 'active' : cfg.mode === 'log_only' ? 'shadow' : null);
  if (state === null) return null;
  if (state === 'active') {
    const existing = await repo.blockingInScope(db, spec.accountId, spec.characterId);
    if (existing) return { state: 'active', hold: existing, created: false };
  } else if (await repo.recentShadow(db, spec.accountId, spec.characterId, new Date(now.getTime() - cfg.shadowDedupeHours * HOUR_MS))) {
    return null;
  }
  try {
    const ins = await repo.insertHold(db, {
      accountId: spec.accountId,
      characterId: spec.characterId,
      kind: spec.kind,
      state,
      originHoldId: spec.originHoldId ?? null,
      windowKind: spec.windowKind ?? null,
      windowStart: spec.windowStart ?? null,
      windowEnd: spec.windowEnd ?? null,
      evidence: spec.evidence,
      createdAt: now,
      note: spec.note ?? null,
    });
    const hold = (await repo.holdById(db, ins.id)) as HoldRow;
    logger.warn({ hold: hold.uuid, kind: spec.kind, account: spec.accountId, character: spec.characterId, state }, state === 'shadow' ? 'anti_abuse.hold_shadow' : 'anti_abuse.hold_active');
    return { state, hold, created: true };
  } catch (err) {
    // 같은 계정에 평가가 겹쳤다: 유일 인덱스가 하나만 통과시켰다
    if (isUniqueViolation(err, 'economy_holds_one_active')) {
      const existing = await repo.blockingInScope(db, spec.accountId, spec.characterId);
      if (existing) return { state: 'active', hold: existing, created: false };
    }
    throw err;
  }
}

/** 12.7: 정지 계정과 같은 기기·같은 Steam 소유자·의심 거래 상대를 계정 단위로 연결 정지한다(상한 HOLD_LINK_MAX_ACCOUNTS) */
export async function propagate(origin: HoldRow, now: Date = getNow()): Promise<number> {
  const cfg = getConfig().aa.hold;
  const db = getPool();
  const via = new Map<number, Set<string>>();
  const add = (account: number, how: string): void => {
    if (account === origin.account_id) return;
    const s = via.get(account) ?? new Set<string>();
    s.add(how);
    via.set(account, s);
  };
  const deviceSince = new Date(now.getTime() - cfg.linkDeviceDays * DAY_MS);
  for (const d of await repo.devicesWithCounts(db, origin.account_id, deviceSince)) {
    // 계정이 너무 많은 기기는 공용 PC(PC방)로 보고 연결하지 않는다
    if (d.accounts > cfg.linkDeviceMaxAccounts) continue;
    for (const a of await repo.accountsOnDevice(db, d.device_hash, deviceSince)) add(a, 'device');
  }
  const steamKey = await repo.steamKeyOf(db, origin.account_id);
  if (steamKey) for (const a of await repo.accountsOfSteamKey(db, steamKey)) add(a, 'steam');
  for (const a of await repo.flaggedPartners(db, origin.account_id, new Date(now.getTime() - 7 * DAY_MS))) add(a, 'auction');
  let made = 0;
  for (const [account, how] of via) {
    if (made >= cfg.linkMaxAccounts) break;
    if (await repo.blockingInScope(db, account, null)) continue;
    const state = origin.state === 'active' || origin.state === 'clawed_back' ? 'active' : 'shadow';
    try {
      await repo.insertHold(db, { accountId: account, characterId: null, kind: 'linked', state, originHoldId: origin.id, evidence: { origin: origin.uuid, via: [...how] }, createdAt: now });
      made++;
    } catch (err) {
      if (!isUniqueViolation(err, 'economy_holds_one_active')) throw err;
    }
  }
  return made;
}

const round = (n: number): number => Math.round(n * 100) / 100;
const roundMap = (m: Record<string, number>): Record<string, number> => Object.fromEntries(Object.entries(m).map(([k, v]) => [k, round(v)]));

function evidenceOf(evals: WindowEval[], pick: WindowEval, metric: string, mult: number, bandRange: { min_level: number; max_level: number }): Record<string, unknown> {
  const key = metric as keyof WindowEval['values'];
  return {
    metric,
    window: pick.window,
    value: round(pick.values[key]),
    cap: round(pick.caps[key]),
    mult,
    active_seconds: pick.activeSeconds,
    band: bandRange,
    windows: Object.fromEntries(evals.map((e) => [e.window, { values: roundMap(e.values), caps: roundMap(e.caps), active_seconds: e.activeSeconds, over: e.over }])),
  };
}

export interface CheckOutcome {
  violated: boolean;
  created?: CreateResult | null;
  evals: WindowEval[];
}

/** 한 캐릭터의 수입 속도를 창 3개로 평가하고, 넘었으면 정지(모드에 따라 shadow 또는 active)를 만든다 */
export async function checkCharacter(characterId: number, now: Date = getNow()): Promise<CheckOutcome> {
  const cfg = getConfig().aa.hold;
  const none: CheckOutcome = { violated: false, evals: [] };
  if (cfg.mode === 'off') return none;
  const caps = getIncomeCaps();
  if (!caps) return none;
  const db = getPool();
  const ch = await repo.characterBrief(db, characterId);
  if (!ch) return none;
  const baseline = await repo.baselineOf(db, ch.account_id, characterId);
  const first = await firstBucketAt(db);
  if (first === null) return none;
  const evals: WindowEval[] = [];
  for (const w of WINDOWS) {
    // 서버의 첫 버킷부터 창 길이만큼 지나지 않았으면 그 창은 건너뛴다(배포 직후 7일 창)
    if (now.getTime() - first.getTime() < WINDOW_HOURS[w] * HOUR_MS) continue;
    evals.push(evaluateBuckets(caps, w, await loadBuckets(db, characterId, now, w, baseline), ch.level, cfg.mult));
  }
  const hit = evals.find((e) => e.over.length > 0);
  if (!hit) return { violated: false, evals };
  const metric = hit.over[0] as string;
  const band = bandOf(caps, ch.level);
  const created = await createHold({
    accountId: ch.account_id,
    characterId,
    kind: 'velocity',
    windowKind: hit.window,
    windowStart: new Date(now.getTime() - WINDOW_HOURS[hit.window] * HOUR_MS),
    windowEnd: now,
    evidence: evidenceOf(evals, hit, metric, cfg.mult, { min_level: band.minLevel, max_level: band.maxLevel }),
    now,
  });
  if (created?.created) await propagate(created.hold, now);
  return { violated: true, created, evals };
}

/** 경매 위반(12.4): 계정의 캐릭터별로 24시간 가중 순유입이 자기 골드환산 상한의 비율 + 하한을 넘는가. 넘으면 계정 단위 정지 */
export async function checkAuction(accountId: number, now: Date = getNow()): Promise<CreateResult | null> {
  const cfg = getConfig().aa;
  if (cfg.hold.mode === 'off') return null;
  const caps = getIncomeCaps();
  if (!caps) return null;
  const db = getPool();
  for (const ch of await repo.charactersOfAccount(db, accountId)) {
    const baseline = await repo.baselineOf(db, accountId, ch.id);
    const buckets = await loadBuckets(db, ch.id, now, '24h', baseline);
    const capGold = capsFor(caps, '24h', buckets, ch.level).goldEq;
    const v = auctionViolation(buckets, capGold, cfg.auction.netRatio, cfg.auction.netFloor);
    if (!v.over) continue;
    const created = await createHold({
      accountId,
      characterId: null,
      kind: 'auction',
      windowKind: 'auction_24h',
      windowStart: new Date(now.getTime() - 24 * HOUR_MS),
      windowEnd: now,
      evidence: { metric: 'auction_net', character_id: ch.id, value: Math.round(v.net), cap: Math.round(v.limit), ratio: cfg.auction.netRatio, floor: cfg.auction.netFloor },
      now,
    });
    if (created?.created) await propagate(created.hold, now);
    return created;
  }
  return null;
}

export type { WindowKind };
