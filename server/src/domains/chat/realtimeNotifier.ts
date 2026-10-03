// 접속 목록(SessionRegistry)과 알림 인터페이스(RealtimeNotifier). 시작은 프로세스 메모리 구현이고,
// 서버가 여러 대가 되면 이 인터페이스 뒤의 구현만 바꾼다(phase5_api.md 12절).
import { getConfig } from '../../config/env';
import type { ChatSession } from './chatSession';
import type { Frame } from './wsProtocol';

export class SessionRegistry {
  private byAccount = new Map<number, ChatSession>();
  private byCharacter = new Map<number, ChatSession>();
  private shards = new Map<number, Set<ChatSession>>();

  size(): number {
    return this.byAccount.size;
  }
  ofAccount(accountId: number): ChatSession | undefined {
    return this.byAccount.get(accountId);
  }
  ofCharacter(characterId: number): ChatSession | undefined {
    return this.byCharacter.get(characterId);
  }
  inShard(shard: number): ChatSession[] {
    return [...(this.shards.get(shard) ?? [])];
  }
  all(): ChatSession[] {
    return [...this.byAccount.values()];
  }

  /** 정원(CHAT_SHARD_SIZE)이 남은 가장 작은 방 번호 */
  pickShard(): number {
    const cap = getConfig().social.chatShardSize;
    for (let n = 1; ; n++) if ((this.shards.get(n)?.size ?? 0) < cap) return n;
  }

  add(s: ChatSession): void {
    this.byAccount.set(s.accountId, s);
    this.byCharacter.set(s.characterId, s);
    const set = this.shards.get(s.shard) ?? new Set<ChatSession>();
    set.add(s);
    this.shards.set(s.shard, set);
  }

  /** 항상 자기 자신을 모든 목록에서 뺀다. 계정 자리를 쥐고 있던 것이 자기였을 때만 true */
  remove(s: ChatSession): boolean {
    this.shards.get(s.shard)?.delete(s);
    if (this.byCharacter.get(s.characterId) === s) this.byCharacter.delete(s.characterId);
    if (this.byAccount.get(s.accountId) !== s) return false;
    this.byAccount.delete(s.accountId);
    return true;
  }
}

export const registry = new SessionRegistry();

export type FriendsChangeReason = 'request' | 'accepted' | 'removed';
export type InviteClosedState = 'declined' | 'expired' | 'cancelled' | 'accepted';

export interface RealtimeNotifier {
  /** 접속 중인 세션이 하나라도 있는가(없으면 호출 쪽이 알림용 조회를 건너뛴다) */
  readonly active: boolean;
  partyChanged(characterIds: number[], scope: 'party' | 'run', runId?: string): void;
  friendsChanged(accountId: number, reason: FriendsChangeReason): void;
  partyInvite(characterId: number, frame: Frame): void;
  partyInviteClosed(characterId: number, id: string, state: InviteClosedState): void;
  blockChanged(accountId: number, blockedAccountId: number, blocked: boolean): void;
  /** 6단계: 접속 중인 그 캐릭터에게 chat.sys 한 줄(경매 판매·입찰 반환·우편 도착). 저장하지 않고 seq가 없다 */
  systemLine(characterUuid: string, text: string): void;
}

const COALESCE_MS = 200;

export class MemoryNotifier implements RealtimeNotifier {
  private timers = new Map<string, NodeJS.Timeout>();

  get active(): boolean {
    return registry.size() > 0;
  }

  partyChanged(characterIds: number[], scope: 'party' | 'run', runId?: string): void {
    for (const cid of characterIds) {
      const s = registry.ofCharacter(cid);
      if (!s) continue;
      // 같은 계정에 200ms 안의 여러 번은 하나로 묶는다
      const key = `${s.accountId}|${scope}|${runId ?? ''}`;
      if (this.timers.has(key)) continue;
      const timer = setTimeout(() => {
        this.timers.delete(key);
        const frame: Frame = { t: 'party.changed', scope };
        if (runId) frame.run_id = runId;
        s.send(frame);
      }, COALESCE_MS);
      timer.unref();
      this.timers.set(key, timer);
    }
  }

  friendsChanged(accountId: number, reason: FriendsChangeReason): void {
    registry.ofAccount(accountId)?.send({ t: 'friends.changed', reason });
  }

  partyInvite(characterId: number, frame: Frame): void {
    registry.ofCharacter(characterId)?.send(frame);
  }

  partyInviteClosed(characterId: number, id: string, state: InviteClosedState): void {
    registry.ofCharacter(characterId)?.send({ t: 'party.invite.closed', id, state });
  }

  blockChanged(accountId: number, blockedAccountId: number, blocked: boolean): void {
    const s = registry.ofAccount(accountId);
    if (!s) return;
    if (blocked) s.blocks.add(blockedAccountId);
    else s.blocks.delete(blockedAccountId);
  }

  systemLine(characterUuid: string, text: string): void {
    const s = registry.all().find((x) => x.characterUuid === characterUuid);
    s?.send({ t: 'chat.sys', text, at: new Date().toISOString() });
  }

  stop(): void {
    for (const t of this.timers.values()) clearTimeout(t);
    this.timers.clear();
  }
}

let current: RealtimeNotifier = new MemoryNotifier();
export function getNotifier(): RealtimeNotifier {
  return current;
}
