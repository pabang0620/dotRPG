// 우편 도착 알림(phase6_api.md 12절). 우편이 만들어진 트랜잭션이 커밋된 뒤에만 발행한다(롤백이면 없다).
// 푸시는 유실될 수 있다: 정본은 mails 표이고 클라이언트는 GET /mail/summary 로 따라잡는다.
import type { PoolClient } from 'pg';
import { afterCommit } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { parseItemKey } from '../../utils/itemKey';
import { logger } from '../../utils/logger';
import { getNotifier } from '../chat/realtimeNotifier';

export type MailKind = 'sold' | 'expired' | 'outbid' | 'bought' | 'cancelled' | 'system';

export interface NotificationEvent {
  type: 'mail.arrived' | 'auction.sold' | 'auction.outbid';
  characterUuid: string;
  mailUuid: string;
  kind: MailKind;
  refItemKey: string | null;
  gold: number;
  at: string;
}

export interface NotificationPublisher {
  /** 동기, 실패해도 던지지 않는다(best-effort) */
  publish(event: NotificationEvent): void;
}

export class NoopPublisher implements NotificationPublisher {
  publish(): void {
    // 알림 없음: 폴링(GET /mail/summary)만으로 동작한다
  }
}

const nameOf = (key: string | null): string => {
  if (!key) return '아이템';
  const p = parseItemKey(key);
  const base = getGameData().economy.items.get(p?.base ?? key)?.name ?? key;
  return p && p.level > 0 ? `${base} +${p.level}` : base;
};

/** 5단계 실시간 채널(chat.sys 한 줄)로 보낸다. 접속 중이 아니면 아무 일도 없다 */
export class ChatSysPublisher implements NotificationPublisher {
  publish(e: NotificationEvent): void {
    try {
      const n = getNotifier();
      if (!n.active) return;
      const item = nameOf(e.refItemKey);
      if (e.type === 'auction.sold') {
        n.systemLine(e.characterUuid, `[경매] ${item} 판매 대금 ${e.gold}G가 우편으로 도착했습니다.`);
      } else if (e.type === 'auction.outbid') {
        n.systemLine(e.characterUuid, `[경매] ${item} 입찰이 밀려 ${e.gold}G가 우편으로 반환되었습니다.`);
      } else if (e.kind !== 'sold' && e.kind !== 'outbid') {
        // 구체 이벤트가 따로 가는 우편은 일반 안내를 한 번 더 보내지 않는다
        n.systemLine(e.characterUuid, '[우편] 새 우편이 도착했습니다.');
      }
    } catch (err) {
      logger.error({ err }, 'mail notification failed');
    }
  }
}

let publisher: NotificationPublisher = new ChatSysPublisher();
export function setNotificationPublisher(p: NotificationPublisher): void {
  publisher = p;
}

/** 우편 한 통이 만들어졌다: 커밋 뒤에 mail.arrived(+ 종류별 이벤트)를 발행한다 */
export function announceMail(
  client: PoolClient,
  m: { characterUuid: string; mailUuid: string; kind: MailKind; refItemKey: string | null; gold: number; at: Date },
): void {
  afterCommit(client, () => {
    const base = {
      characterUuid: m.characterUuid,
      mailUuid: m.mailUuid,
      kind: m.kind,
      refItemKey: m.refItemKey,
      gold: m.gold,
      at: m.at.toISOString(),
    };
    publisher.publish({ ...base, type: 'mail.arrived' });
    if (m.kind === 'sold') publisher.publish({ ...base, type: 'auction.sold' });
    if (m.kind === 'outbid') publisher.publish({ ...base, type: 'auction.outbid' });
  });
}
