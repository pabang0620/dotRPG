// 단계(tier)·속도 제한(Docs/server/phase11_payments.md 10절). 결제 금액 한도는 두지 않는다(단계는 이상 신호 기록·관리자 조회용).
// 일일 경계는 resetBoundaries 하나, 30일 합계·90일 창은 롤링 창이다(새 경계 함수를 만들지 않는다).
import { getConfig } from '../../config/env';
import type { Queryable } from '../../db/pool';
import { AppError } from '../../utils/AppError';
import { resetBoundaries } from '../../utils/resetBoundaries';
import * as holdsRepo from '../antiabuse/holdsRepository';
import * as repo from './paymentsRepository';
import type { FlagKind } from './paymentsRepository';

export type Tier = 'new' | 'standard' | 'restricted';

const DAY_MS = 86_400_000;
const HOUR_MS = 3_600_000;
export const RESTRICT_DAYS = 30;

export interface TierSignal {
  kind: FlagKind;
  severity: 1 | 2 | 3;
  detail: Record<string, unknown>;
  /** 이 신호가 제한 단계 하한을 30일 올린다 */
  raisesFloor: boolean;
}

export interface TierResult {
  tier: Tier;
  /** 내부 사유(관리자 상세용. 플레이어 응답에는 싣지 않는다) */
  reasons: string[];
  signals: TierSignal[];
}

/**
 * 10.2 단계 계산(restricted > new > standard 순으로 판정).
 * currentCountry 는 이번 주문 직전 Steam이 알려 준 국가(주문 행에는 아직 없다)
 */
export async function computeTier(db: Queryable, accountId: number, now: Date, currentCountry: string | null): Promise<TierResult> {
  const cfg = getConfig();
  const pay = cfg.pay;
  const reasons: string[] = [];
  const signals: TierSignal[] = [];
  const profile = await repo.profileOf(db, accountId);
  if (profile && profile.tier_floor === 'restricted' && profile.tier_floor_until && profile.tier_floor_until > now) reasons.push('tier_floor');

  const since90 = new Date(now.getTime() - 90 * DAY_MS);
  if ((await repo.countFlags(db, accountId, 'refund_after_spend', since90)) >= pay.refundRestrictCount) reasons.push('refund_repeat');

  const countries = new Set(await repo.distinctCountriesSince(db, accountId, since90));
  if (currentCountry) countries.add(currentCountry);
  if (countries.size - 1 > pay.countryChangeMax) {
    reasons.push('country_changed');
    signals.push({ kind: 'country_changed', severity: 1, detail: { countries: [...countries].sort() }, raisesFloor: true });
  }

  const hold = cfg.aa.hold;
  const devSince = new Date(now.getTime() - hold.linkDeviceDays * DAY_MS);
  const devices = await repo.purchasingAccountsOnDevices(db, accountId, devSince, new Date(now.getTime() - 30 * DAY_MS), hold.linkDeviceMaxAccounts);
  if (devices.some((d) => d.purchasers >= pay.sharedDeviceAccounts)) {
    reasons.push('shared_device');
    signals.push({ kind: 'shared_device', severity: 2, detail: { devices: devices.filter((d) => d.purchasers >= pay.sharedDeviceAccounts).length }, raisesFloor: false });
  }

  // 연결 계정 차지백: 같은 비공용 기기·같은 Steam 소유자 키(패밀리 공유 소유자 전파가 아니라 구매자 쪽 제한 단계만 올린다, 10.5)
  const linked = new Set<number>();
  for (const d of await holdsRepo.devicesWithCounts(db, accountId, devSince)) {
    if (d.accounts > hold.linkDeviceMaxAccounts) continue;
    for (const a of await holdsRepo.accountsOnDevice(db, d.device_hash, devSince)) linked.add(a);
  }
  const steamKey = await holdsRepo.steamKeyOf(db, accountId);
  if (steamKey) for (const a of await holdsRepo.accountsOfSteamKey(db, steamKey)) linked.add(a);
  linked.delete(accountId);
  const bad = await repo.linkedChargebackAccounts(db, [...linked]);
  if (bad.length > 0) {
    reasons.push('linked_chargeback');
    signals.push({ kind: 'linked_chargeback', severity: 3, detail: { linked: bad.length }, raisesFloor: true });
  }

  if (reasons.length > 0) return { tier: 'restricted', reasons, signals };
  const info = await repo.accountInfo(db, accountId);
  const ageDays = info ? (now.getTime() - info.created_at.getTime()) / DAY_MS : 0;
  if (ageDays < pay.newAccountDays) return { tier: 'new', reasons: ['new_account'], signals };
  return { tier: 'standard', reasons: [], signals };
}

export interface SpentView {
  daily: { used: number; resets_at: string };
  monthly: { used: number };
}

/** 관리자 조회용 구매 합계(한도 아님). 합산 대상은 failed·expired 를 뺀 모든 주문. 일일 = 06:00 KST부터, 월 = 최근 30일 롤링 */
export async function spentFor(db: Queryable, accountId: number, now: Date): Promise<SpentView> {
  const b = resetBoundaries(now);
  return {
    daily: { used: await repo.starsOrderedSince(db, accountId, new Date(b.dailyStartAt)), resets_at: b.nextDailyAt },
    monthly: { used: await repo.starsOrderedSince(db, accountId, new Date(now.getTime() - 30 * DAY_MS)) },
  };
}

/** 10.3 속도 제한: 최소 간격, 실패 쿨다운(시간당 건수 상한은 두지 않는다). 던지는 오류에는 retry_after_sec 가 있다 */
export async function assertSpeed(db: Queryable, accountId: number, now: Date): Promise<{ failBurst: boolean }> {
  const pay = getConfig().pay;
  const last = await repo.lastOrderAt(db, accountId);
  if (last && pay.minOrderGapSeconds > 0) {
    const wait = Math.ceil((last.getTime() + pay.minOrderGapSeconds * 1000 - now.getTime()) / 1000);
    if (wait > 0) throw new AppError(429, '주문이 너무 빠릅니다. 잠시 후 다시 시도해 주세요.', 'ORDER_TOO_FAST', { retry_after_sec: wait });
  }
  const hourAgo = new Date(now.getTime() - HOUR_MS);
  const failed = await repo.failedSince(db, accountId, hourAgo);
  if (failed.n >= pay.failCooldownThreshold) {
    const wait = failed.oldest ? Math.max(1, Math.ceil((failed.oldest.getTime() + HOUR_MS - now.getTime()) / 1000)) : 3600;
    throw new FailBurst(wait);
  }
  return { failBurst: false };
}

/** 실패 급증 쿨다운(호출 쪽이 fail_burst 플래그를 남기고 ORDER_TOO_FAST 로 바꾼다) */
export class FailBurst extends AppError {
  constructor(retryAfterSec: number) {
    super(429, '주문이 너무 빠릅니다. 잠시 후 다시 시도해 주세요.', 'ORDER_TOO_FAST', { retry_after_sec: retryAfterSec });
    this.name = 'FailBurst';
  }
}
