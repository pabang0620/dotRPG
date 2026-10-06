// 여러 첨부가 있는 우편(캠페인 우편)의 첨부: 읽기, 목록 응답용 합성, 수령, 기한 폐기. 설계 6절.
// 첨부 아이템은 배달 때 item_ledger(admin_grant, 위치 mail +n)로 들어 있고, 수령하면 mail -n / bag +n, 폐기하면 mail -n(mail_expire)이다.
// 클리어권은 아직 지갑에 들어가지 않았으므로 수령 때 이벤트 로트를 만들고 폐기 때는 아무 기록도 없다.
import type { PoolClient } from 'pg';
import type { Queryable } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { insertSink } from '../auction/auctionRepository';
import type { EconCtx } from '../economy/economyContext';
import { insertItemLedger, type Bind } from '../economy/economyRepository';
import * as wallet from '../sweep/ticketWallet';
import type { MailRow } from './mailRepository';

export type AttachKind = 'gold' | 'item' | 'sweep_ticket';

export interface Attachment {
  slot: number;
  kind: AttachKind;
  itemKey: string | null;
  amount: number;
  bind: Bind | null;
}

interface RawAtt {
  mail_id: string;
  slot: number;
  kind: AttachKind;
  item_key: string | null;
  amount: string;
  bind: Bind | null;
}

/** 한 페이지 우편의 첨부를 한 번에 읽는다(우편마다 쿼리를 날리지 않는다) */
export async function attachmentsOf(db: Queryable, mailIds: number[]): Promise<Map<number, Attachment[]>> {
  const out = new Map<number, Attachment[]>();
  if (mailIds.length === 0) return out;
  const r = await db.query<RawAtt>(
    'SELECT mail_id, slot, kind, item_key, amount, bind FROM mail_attachments WHERE mail_id = ANY($1::bigint[]) ORDER BY mail_id, slot',
    [mailIds],
  );
  for (const x of r.rows) {
    const list = out.get(Number(x.mail_id)) ?? [];
    list.push({ slot: x.slot, kind: x.kind, itemKey: x.item_key, amount: Number(x.amount), bind: x.bind });
    out.set(Number(x.mail_id), list);
  }
  return out;
}

/** 옛 우편(첨부 표 없음)의 gold -> item 순서 합성. 응답이 옛·새 우편 모두 attachments를 갖게 한다 */
export function legacyAttachments(m: MailRow): Attachment[] {
  const out: Attachment[] = [];
  if (m.gold > 0) out.push({ slot: out.length + 1, kind: 'gold', itemKey: null, amount: m.gold, bind: null });
  if (m.itemKey !== null && m.count !== null) out.push({ slot: out.length + 1, kind: 'item', itemKey: m.itemKey, amount: m.count, bind: m.bind });
  return out;
}

/** 응답의 첨부 한 줄. 클리어권에는 수령 뒤 유효 일수를 함께 알린다 */
export function attachmentView(a: Attachment) {
  const sweep = getGameData().sweep;
  return {
    slot: a.slot,
    kind: a.kind,
    item_key: a.itemKey,
    count: a.amount,
    bind: a.bind,
    ...(a.kind === 'sweep_ticket' ? { valid_days_after_claim: sweep?.eventTicketDays ?? null } : {}),
  };
}

export async function insertAttachments(client: PoolClient, mailId: number, atts: Attachment[]): Promise<void> {
  for (const a of atts) {
    await client.query(
      'INSERT INTO mail_attachments (mail_id, slot, kind, item_key, amount, bind) VALUES ($1, $2, $3, $4, $5, $6)',
      [mailId, a.slot, a.kind, a.itemKey, a.amount, a.bind],
    );
  }
}

export const goldOf = (atts: Attachment[]): number => atts.filter((a) => a.kind === 'gold').reduce((s, a) => s + a.amount, 0);

/** 수령(캐릭터 행·계정 행·우편 행이 잠겨 있다). 부분 수령은 없다: 호출 쪽이 골드 상한을 먼저 확인한다 */
export async function claimAttachments(ctx: EconCtx, m: MailRow, atts: Attachment[], now: Date): Promise<{ tickets: boolean }> {
  const gold = goldOf(atts);
  if (gold > 0) await ctx.changeGold(gold, 'mail_claim', m.uuid);
  let tickets = false;
  for (const a of atts) {
    if (a.kind === 'item' && a.itemKey !== null && a.bind !== null) {
      await insertItemLedger(ctx.client, ctx.char.id, a.itemKey, -a.amount, 0, 'mail', 'mail_claim', m.uuid, ctx.requestId);
      await ctx.addItem('bag', a.itemKey, a.amount, 'mail_claim', m.uuid, a.bind);
    } else if (a.kind === 'sweep_ticket') {
      const sweep = getGameData().sweep;
      if (!sweep) throw new AppError(503, '아직 준비 중인 기능입니다.', 'FEATURE_DISABLED');
      const expiresAt = new Date(now.getTime() + sweep.eventTicketDays * 86_400_000);
      await wallet.addEvent(ctx.client, { accountId: ctx.char.accountId, characterId: ctx.char.id, requestId: ctx.requestId, now }, a.amount, expiresAt, m.uuid);
      tickets = true;
    }
  }
  return { tickets };
}

/** 기한 폐기(auctionTicker.expireMailById가 우편 행을 잠근 뒤 부른다): 아이템 mail -n, 골드는 소각 기록. 클리어권은 기록 없음 */
export async function expireAttachments(client: PoolClient, m: MailRow, now: Date): Promise<void> {
  const atts = (await attachmentsOf(client, [m.id])).get(m.id) ?? [];
  for (const a of atts) {
    if (a.kind === 'item' && a.itemKey !== null) {
      await insertItemLedger(client, m.characterId, a.itemKey, -a.amount, 0, 'mail', 'mail_expire', m.uuid, null);
    }
  }
  await insertSink(client, 'mail_expire', goldOf(atts), m.listingId, m.id, m.characterId, now);
}
