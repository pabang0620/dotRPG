// 호스트 선출(순수 함수, phase8_api.md 6.2). 파티 방장 우선, 없으면 파티 가입이 가장 빠른 멤버(좌석은 선출과 무관).

export interface Candidate {
  characterId: number;
  seat: number;
  state: 'joined' | 'playing' | 'disconnected' | 'left';
  isLeader: boolean;
  /** 파티 가입 시각(ms) */
  partyJoinedAt: number;
  /** 하트비트가 신선한가(요청자 본인은 호출 쪽이 true로 준다) */
  fresh: boolean;
}

const byPriority = (a: Candidate, b: Candidate): number =>
  Number(b.isLeader) - Number(a.isLeader) || a.partyJoinedAt - b.partyJoinedAt || a.seat - b.seat;

/**
 * 후보: 연결·환영이 끝난(playing) 신선한 멤버. 없으면 방금 들어온(joined) 신선한 멤버. 그래도 없으면 null.
 * exclude: 후보에서 뺄 캐릭터(떠나는 옛 호스트)
 */
export function electHost(members: Candidate[], exclude: number | null = null): number | null {
  const pool = members.filter((m) => m.characterId !== exclude && m.fresh);
  const playing = pool.filter((m) => m.state === 'playing').sort(byPriority);
  if (playing[0]) return playing[0].characterId;
  const joined = pool.filter((m) => m.state === 'joined').sort(byPriority);
  return joined[0]?.characterId ?? null;
}

/** 선출 창 안에서 더 우선인 멤버가 있으면 그 사람, 아니면 null(바꾸지 않는다). 우선순위만 비교하고 신선도는 보지 않는다 */
export function electionWinner(members: Candidate[], currentHost: number | null): number | null {
  const pool = members.filter((m) => m.state === 'joined' || m.state === 'playing').sort(byPriority);
  const best = pool[0];
  if (!best || best.characterId === currentHost) return null;
  return best.characterId;
}
