// 결과 대조(8절)의 순수 함수: DB·시계 없음. 방장 보고(H)와 멤버 보고(M)가 서로 일치하는지 판정한다.
export interface ReportFacts {
  outcome: 'cleared' | 'failed';
  elapsedMs: number;
  /** 방 번호 -> 그 방에서 본 처치 수 */
  rooms: Map<number, number>;
}

export interface Tolerance {
  elapsedMs: number;
  /** 방별 처치 수 허용 비율(방 총 마릿수 대비) */
  killRatio: number;
  /** 방별 총 마릿수 */
  roomSizes: number[];
}

/** 8.3 일치: 결과가 같고, 경과 시간 차가 허용 안이고, 방별 처치 수 차가 허용 안이다 */
export function consistent(a: ReportFacts, b: ReportFacts, tol: Tolerance): boolean {
  if (a.outcome !== b.outcome) return false;
  const allowedMs = Math.max(tol.elapsedMs, 0.05 * Math.max(a.elapsedMs, b.elapsedMs));
  if (Math.abs(a.elapsedMs - b.elapsedMs) > allowedMs) return false;
  const rooms = new Set([...a.rooms.keys(), ...b.rooms.keys()]);
  for (const r of rooms) {
    const allowed = Math.ceil(tol.killRatio * (tol.roomSizes[r] ?? 0));
    if (Math.abs((a.rooms.get(r) ?? 0) - (b.rooms.get(r) ?? 0)) > allowed) return false;
  }
  return true;
}

export interface Witness {
  /** 이 사람이 방장인가 */
  isHost: boolean;
  /** 보고했으면 그 내용(보고 전이어도 증인으로 센다) */
  report: ReportFacts | null;
}

export interface ReconcileInput {
  /** 정산 대상(i) */
  iAmHost: boolean;
  mine: ReportFacts;
  /** 방장 보고 H(없거나 무효면 null). i가 방장이면 i 자신의 관찰 */
  hostReport: ReportFacts | null;
  /** i를 뺀, 이탈·불참·탈락이 아닌 사람 멤버 */
  witnesses: Witness[];
  /** 대기 마감(first_report_at + 90초)이 지났는가 */
  pastDeadline: boolean;
  tol: Tolerance;
}

export type Decision =
  | { kind: 'settle'; hostOutlier?: boolean }
  | { kind: 'pending' }
  | { kind: 'held'; reason: 'MISMATCH' | 'NO_HOST_REPORT' | 'NO_WITNESS' };

export function reconcile(inp: ReconcileInput): Decision {
  const { mine, hostReport, witnesses, tol } = inp;
  // 증인이 없으면 솔로 수준(자기 검증만)
  if (witnesses.length === 0) return { kind: 'settle' };

  if (inp.iAmHost) {
    const ref = hostReport ?? mine;
    const reported = witnesses.filter((w) => w.report !== null);
    if (reported.some((w) => consistent(ref, w.report as ReportFacts, tol))) return { kind: 'settle' };
    const waiting = reported.length < witnesses.length && !inp.pastDeadline;
    if (waiting) return { kind: 'pending' };
    return { kind: 'held', reason: reported.length === 0 ? 'NO_WITNESS' : 'MISMATCH' };
  }

  const peers = witnesses.filter((w) => !w.isHost && w.report !== null);
  const peerAgrees = peers.some((w) => consistent(mine, w.report as ReportFacts, tol));
  if (hostReport) {
    if (consistent(mine, hostReport, tol)) return { kind: 'settle' };
    if (peerAgrees) return { kind: 'settle', hostOutlier: true };
    const peersWaiting = witnesses.some((w) => !w.isHost && w.report === null) && !inp.pastDeadline;
    return peersWaiting ? { kind: 'pending' } : { kind: 'held', reason: 'MISMATCH' };
  }
  // H가 없거나 무효
  if (!inp.pastDeadline) return { kind: 'pending' };
  return peerAgrees ? { kind: 'settle' } : { kind: 'held', reason: 'NO_HOST_REPORT' };
}

export interface MergeInput {
  mine: { hits: number; combo: number; revives: number };
  /** H의 내 항목(H가 유효할 때만) */
  host: { hits: number; combo: number; revives: number } | null;
}

/** 방장 관찰과 멤버 자기 값이 이만큼 넘게 어긋나면 방장 값을 쓰지 않는다(극단값으로 랭크를 깎는 것을 막는다) */
export const MERGE_TOLERANCE = { hits: 10, combo: 10, revives: 1 } as const;

/**
 * 8.4 정산 값 합성: 피격·부활은 나쁜 값(큰 값), 콤보는 낮은 값. 랭크를 올리려 줄여 말하는 것을 막는다.
 * 단 허용 범위를 넘게 어긋난 항목은 멤버 자기 값을 쓰고 outlier로 알린다(방장 이상 기록).
 */
export function mergeStats(m: MergeInput): { hits: number; combo: number; revives: number; outlier: boolean } {
  if (!m.host) return { ...m.mine, outlier: false };
  const t = MERGE_TOLERANCE;
  const far = {
    hits: Math.abs(m.mine.hits - m.host.hits) > t.hits,
    combo: Math.abs(m.mine.combo - m.host.combo) > t.combo,
    revives: Math.abs(m.mine.revives - m.host.revives) > t.revives,
  };
  return {
    hits: far.hits ? m.mine.hits : Math.max(m.mine.hits, m.host.hits),
    combo: far.combo ? m.mine.combo : Math.min(m.mine.combo, m.host.combo),
    revives: far.revives ? m.mine.revives : Math.max(m.mine.revives, m.host.revives),
    outlier: far.hits || far.combo || far.revives,
  };
}
