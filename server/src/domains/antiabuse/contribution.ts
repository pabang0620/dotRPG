// 레이드·파티 던전의 "한 사람" 판정과 기여 판정(5절). DB 없는 순수 함수.
import type { AntiAbuseConfig } from '../../config/antiAbuseEnv';

export interface HumanKey {
  deviceHash: string | null;
  steamKey: string | null;
  /** 설치 id(device_hash를 생략·위조해도 같은 설치면 한 사람). 모르면 null 또는 생략 */
  installId?: string | null;
}

/**
 * 같은 사람 = device_hash(기기 키)가 같거나 steam_key가 같거나 install_id가 같은(각각 NULL이 아닐 때) 멤버끼리의 연결 요소(union-find). IP는 쓰지 않는다.
 * 돌려주는 값은 연결 요소별 멤버 목록이다. humans = 요소 수.
 */
export function humanGroups<T extends HumanKey>(rows: T[]): T[][] {
  const parent = rows.map((_, i) => i);
  const find = (i: number): number => {
    let x = i;
    while (parent[x] !== x) {
      parent[x] = parent[parent[x] as number] as number;
      x = parent[x] as number;
    }
    return x;
  };
  const union = (a: number, b: number): void => {
    const ra = find(a);
    const rb = find(b);
    if (ra !== rb) parent[ra] = rb;
  };
  for (let i = 0; i < rows.length; i++) {
    for (let j = i + 1; j < rows.length; j++) {
      const a = rows[i] as T;
      const b = rows[j] as T;
      if ((a.deviceHash !== null && a.deviceHash === b.deviceHash) || (a.steamKey !== null && a.steamKey === b.steamKey) || (a.installId != null && a.installId === b.installId)) union(i, j);
    }
  }
  const groups = new Map<number, T[]>();
  rows.forEach((r, i) => {
    const root = find(i);
    groups.set(root, [...(groups.get(root) ?? []), r]);
  });
  return [...groups.values()];
}

export interface Contribution {
  /** 사람 멤버 피해 합 중 이 멤버의 지분(0~1). 분모가 0이면 0 */
  share: number;
  hits: number | null;
  met: boolean;
}

/** 기여 충족 = 지분 >= minShare 또는 적중 수 >= minHits(치유·방어형 직업을 위해 타격 수 경로를 열어 둔다). hits가 없으면 지분만으로 판정 */
export function contributionMet(share: number, hits: number | null, minShare: number, minHits: number): boolean {
  // 부동소수점 경계(정확히 5%)를 안전하게 넘기려고 아주 작은 여유를 둔다
  return share >= minShare - 1e-9 || (hits !== null && hits >= minHits);
}

export interface MemberDamage {
  id: number;
  damage: number;
  hits: number | null;
}

/** 호스트가 관찰한 멤버별 피해·적중으로 멤버마다 기여를 계산한다(분모는 사람 멤버만, AI 제외) */
export function contributionsOf(members: MemberDamage[], minShare: number, minHits: number): Map<number, Contribution> {
  const total = members.reduce((a, m) => a + m.damage, 0);
  return new Map(
    members.map((m) => {
      const share = total > 0 ? m.damage / total : 0;
      return [m.id, { share, hits: m.hits, met: contributionMet(share, m.hits, minShare, minHits) }] as const;
    }),
  );
}

/**
 * 멤버 주장(damage_dealt, hits_landed)이 호스트 관찰의 ratio배 이상이고 주장 기준으로는 기여를 충족하는데 호스트 기준으로는 못 하면 분쟁이다.
 * 서버는 주장을 지급 근거로 쓰지 않는다. 분쟁이면 그 멤버 정산은 보류(held)한다.
 */
export function isDisputed(
  host: { damage: number; hits: number | null; totalDamage: number },
  claim: { damage?: number | undefined; hits?: number | undefined },
  p: { ratio: number; minShare: number; minHits: number },
): boolean {
  if (claim.damage === undefined && claim.hits === undefined) return false;
  const hostMet = contributionMet(host.totalDamage > 0 ? host.damage / host.totalDamage : 0, host.hits, p.minShare, p.minHits);
  if (hostMet) return false;
  const bigger =
    (claim.damage !== undefined && claim.damage >= p.ratio * Math.max(host.damage, 1)) ||
    (claim.hits !== undefined && claim.hits >= p.ratio * Math.max(host.hits ?? 0, 1));
  if (!bigger) return false;
  const claimDamage = claim.damage ?? host.damage;
  const claimTotal = host.totalDamage - host.damage + claimDamage;
  return contributionMet(claimTotal > 0 ? claimDamage / claimTotal : 0, claim.hits ?? host.hits, p.minShare, p.minHits);
}

/** 언더레벨 경험치 배율(5.4): 권장 레벨 - 멤버 레벨이 GAP 이상일 때만. clamp(FACTOR - STEP x (gap - GAP), MIN, FACTOR) */
export function underlevelFactor(gap: number, p: AntiAbuseConfig['contribution']): number {
  if (gap < p.underlevelGap) return 1;
  const f = p.underlevelFactor - p.underlevelStep * (gap - p.underlevelGap);
  return Math.min(p.underlevelFactor, Math.max(p.underlevelMin, f));
}

/** 호스트 보고의 적중 수 물리 상한: (서버 경과 + 여유) / 공격 쿨다운 x AOE 상한(콤보 검사와 같은 식) */
export function hitsCap(serverElapsedSeconds: number, slackSeconds: number, attackCooldown: number, aoeCap: number): number {
  return ((serverElapsedSeconds + slackSeconds) / attackCooldown) * aoeCap;
}
