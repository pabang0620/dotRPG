// 채팅 제한 상태는 연결이 아니라 계정에 붙인다(재접속으로 우회 못 하게). 인터페이스 뒤 메모리 구현, 마지막 사용 후 10분 보관.
export interface LimiterState {
  lastSentAt: number;
  lastText: string;
  repeats: number;
  repeatMutedUntil: number;
  sent: number[];
  whisperTargets: Map<number, number>;
  filterStrikes: number[];
  repeatMuteStrikes: number[];
  lastUsed: number;
}

export interface ChatLimiterStore {
  get(accountId: number): LimiterState;
  clear(): void;
}

const KEEP_MS = 10 * 60_000;

export class MemoryLimiterStore implements ChatLimiterStore {
  private map = new Map<number, LimiterState>();

  get(accountId: number): LimiterState {
    const now = Date.now();
    if (this.map.size > 2000) this.sweep(now);
    let st = this.map.get(accountId);
    if (!st || now - st.lastUsed > KEEP_MS) {
      st = {
        lastSentAt: 0,
        lastText: '',
        repeats: 0,
        repeatMutedUntil: 0,
        sent: [],
        whisperTargets: new Map(),
        filterStrikes: [],
        repeatMuteStrikes: [],
        lastUsed: now,
      };
      this.map.set(accountId, st);
    }
    st.lastUsed = now;
    return st;
  }

  clear(): void {
    this.map.clear();
  }

  private sweep(now: number): void {
    for (const [k, v] of this.map) if (now - v.lastUsed > KEEP_MS) this.map.delete(k);
  }
}

let store: ChatLimiterStore = new MemoryLimiterStore();
export const getLimiterStore = (): ChatLimiterStore => store;
export const setLimiterStore = (s: ChatLimiterStore): void => {
  store = s;
};
