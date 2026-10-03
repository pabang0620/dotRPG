// 자동 매칭 대기열. 대기는 60초짜리 일시 상태라 표를 만들지 않고 메모리에 둔다(PLAN_SERVER §7: 확장 1단계에서 Redis로 교체).
// 서버가 재시작하면 대기가 사라지고 클라이언트는 다음 폴링에서 queue=null을 보고 다시 건다.
export interface QueueTicket {
  characterId: number;
  accountId: number;
  dungeonId: string;
  difficulty: number;
  level: number;
  power: number;
  queuedAt: Date;
  lastPollAt: Date;
  forceDepart: boolean;
}

export interface QueueStore {
  get(characterId: number): QueueTicket | undefined;
  put(t: QueueTicket): void;
  remove(characterId: number): void;
  /** 같은 (던전, 난이도) 키의 티켓을 대기 시작 순으로 */
  listByKey(dungeonId: string, difficulty: number): QueueTicket[];
  keys(): { dungeonId: string; difficulty: number }[];
  clear(): void;
}

export class MemoryQueueStore implements QueueStore {
  private tickets = new Map<number, QueueTicket>();

  get(characterId: number): QueueTicket | undefined {
    return this.tickets.get(characterId);
  }
  put(t: QueueTicket): void {
    this.tickets.set(t.characterId, t);
  }
  remove(characterId: number): void {
    this.tickets.delete(characterId);
  }
  listByKey(dungeonId: string, difficulty: number): QueueTicket[] {
    return [...this.tickets.values()]
      .filter((t) => t.dungeonId === dungeonId && t.difficulty === difficulty)
      .sort((a, b) => a.queuedAt.getTime() - b.queuedAt.getTime() || a.characterId - b.characterId);
  }
  keys(): { dungeonId: string; difficulty: number }[] {
    const seen = new Map<string, { dungeonId: string; difficulty: number }>();
    for (const t of this.tickets.values()) seen.set(`${t.dungeonId}:${t.difficulty}`, { dungeonId: t.dungeonId, difficulty: t.difficulty });
    return [...seen.values()];
  }
  clear(): void {
    this.tickets.clear();
  }
}

let store: QueueStore = new MemoryQueueStore();
export function getQueueStore(): QueueStore {
  return store;
}
export function setQueueStore(s: QueueStore): void {
  store = s;
}
