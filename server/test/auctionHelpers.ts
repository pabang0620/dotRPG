import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { setClockOverride } from '../src/utils/clock';
import { get, newHero, post, seedGold, seedItem, type Hero } from './economyHelpers';
import { auth, createChar, randomName } from './helpers';

/** 서버 시계를 실제 시각에서 offsetMs만큼 앞으로 민다(0이면 실제 시각) */
let offsetMs = 0;
export function advance(ms: number): void {
  offsetMs += ms;
  setClockOverride(() => new Date(Date.now() + offsetMs));
}
export function setNowAt(t: Date): void {
  offsetMs = t.getTime() - Date.now();
  setClockOverride(() => new Date(Date.now() + offsetMs));
}
export function resetClock(): void {
  offsetMs = 0;
  setClockOverride(null);
}
export const HOUR = 3_600_000;
export const MIN = 60_000;

/** 영웅을 만들고 골드를 정확히 gold로 맞춘다(시작 골드는 100) */
export async function mk(app: Express, gold = 100_000, cls: 'warrior' | 'mage' = 'warrior'): Promise<Hero> {
  const h = await newHero(app, cls);
  const diff = gold - (await goldNow(h));
  if (diff > 0) await seedGold(h, diff);
  else if (diff < 0) {
    await getPool().query('UPDATE characters SET gold = $2 WHERE id = $1', [h.dbId, gold]);
    await getPool().query(
      "INSERT INTO gold_ledger (character_id, delta, balance_after, reason, ref) VALUES ($1, $2, $3, 'shop_buy', $4)",
      [h.dbId, diff, gold, randomUUID()],
    );
  }
  return h;
}

/** 같은 계정의 둘째 캐릭터 */
export async function secondChar(app: Express, h: Hero): Promise<Hero> {
  const res = await createChar(app, h.s, randomName(), 'warrior');
  if (res.status !== 201) throw new Error(`createChar failed ${res.status}`);
  const id = res.body.data.character.id as string;
  const r = await getPool().query<{ id: string }>('SELECT id FROM characters WHERE uuid = $1', [id]);
  return { s: h.s, id, dbId: Number((r.rows[0] as { id: string }).id), cls: 'warrior' };
}

export const listReq = (app: Express, h: Hero, body: Record<string, unknown>, rid?: string) =>
  post(app, h, '/auction/listings', body, rid);
export const buyoutReq = (app: Express, h: Hero, id: string, rid?: string) =>
  post(app, h, `/auction/listings/${id}/buyout`, {}, rid);
export const bidReq = (app: Express, h: Hero, id: string, amount: number, rid?: string) =>
  post(app, h, `/auction/listings/${id}/bids`, { amount }, rid);
export const cancelReq = (app: Express, h: Hero, id: string, requestId?: string) =>
  request(app)
    .delete(`/characters/${h.id}/auction/listings/${id}${requestId ? `?request_id=${requestId}` : ''}`)
    .set(auth(h.s));
export const claimReq = (app: Express, h: Hero, mailId: string, rid?: string) =>
  post(app, h, `/mail/${mailId}/claim`, {}, rid);
export const claimAllReq = (app: Express, h: Hero) => post(app, h, '/mail/claim-all', {});
export const mailList = async (app: Express, h: Hero) =>
  (await get(app, h, '/mail?limit=50')).body.data.mails as {
    id: string;
    kind: string;
    gold: number;
    item: { item_key: string; count: number; bind: string } | null;
    ref_item_key: string | null;
  }[];

/** 장비 한 자루를 시드해 등록한다. 등록 id(uuid)를 돌려준다 */
export async function listIron(
  app: Express,
  seller: Hero,
  o: { buyout?: number; start_bid?: number; hours?: number; key?: string } = {},
): Promise<string> {
  const key = o.key ?? 'eq_sword_10_u';
  await seedItem(seller, key, 1);
  const body: Record<string, unknown> = { item_key: key, count: 1, buyout: o.buyout ?? 1000, hours: o.hours ?? 12 };
  if (o.start_bid !== undefined) body.start_bid = o.start_bid;
  const res = await listReq(app, seller, body);
  if (res.status !== 201) throw new Error(`list failed ${res.status} ${JSON.stringify(res.body)}`);
  return res.body.data.listing.id as string;
}

export async function listingRow(uuid: string): Promise<Record<string, unknown>> {
  const r = await getPool().query('SELECT * FROM auction_listings WHERE uuid = $1', [uuid]);
  return r.rows[0] as Record<string, unknown>;
}

export const goldNow = async (h: Hero): Promise<number> =>
  Number(((await getPool().query('SELECT gold FROM characters WHERE id = $1', [h.dbId])).rows[0] as { gold: string }).gold);

export async function flagKinds(h: Hero): Promise<string[]> {
  const r = await getPool().query<{ kind: string }>(
    'SELECT kind FROM auction_flags WHERE character_id = $1 ORDER BY id',
    [h.dbId],
  );
  return r.rows.map((x) => x.kind);
}

/** 9.3 골드 보존식과 아이템 보존식(DB 전체). 어긋나면 던진다 */
export async function expectConserved(): Promise<void> {
  const db = getPool();
  const n = async (sql: string): Promise<number> => Number(((await db.query<{ v: string }>(sql)).rows[0] as { v: string }).v);
  const out = await n(
    "SELECT coalesce(sum(-delta), 0) AS v FROM gold_ledger WHERE reason IN ('auction_deposit', 'auction_bid', 'auction_buyout')",
  );
  const back = await n("SELECT coalesce(sum(delta), 0) AS v FROM gold_ledger WHERE reason = 'mail_claim'");
  const system = await n("SELECT coalesce(sum(gold), 0) AS v FROM mails WHERE kind = 'system'");
  const deposits = await n("SELECT coalesce(sum(deposit), 0) AS v FROM auction_listings WHERE status = 'active'");
  const bids = await n("SELECT coalesce(sum(current_bid), 0) AS v FROM auction_listings WHERE status = 'active'");
  const inMail = await n('SELECT coalesce(sum(gold), 0) AS v FROM mails WHERE claimed_at IS NULL AND expired_at IS NULL');
  const sinks = await n('SELECT coalesce(sum(amount), 0) AS v FROM auction_sinks');
  const left = out - back + system;
  const right = deposits + bids + inMail + sinks;
  if (left !== right) {
    throw new Error(`골드 보존식 불일치: 좌 ${left} (나감 ${out} 돌아옴 ${back} 시스템 ${system}) 우 ${right} (보증금 ${deposits} 입찰 ${bids} 우편 ${inMail} 소각 ${sinks})`);
  }
  // 아이템: 위치별 원장 합 = 보관 행의 수량
  const items = await db.query<{ item_key: string; loc: string; led: string; held: string }>(
    `SELECT k.item_key, k.loc, coalesce(l.s, 0) AS led, coalesce(h.s, 0) AS held FROM (
       SELECT item_key, location AS loc FROM item_ledger WHERE location IN ('auction', 'mail')
       UNION SELECT item_key, 'auction' FROM auction_listings WHERE status = 'active'
       UNION SELECT item_key, 'mail' FROM mails WHERE item_key IS NOT NULL) k
     LEFT JOIN (SELECT item_key, location AS loc, sum(delta) AS s FROM item_ledger WHERE location IN ('auction', 'mail')
                GROUP BY 1, 2) l ON l.item_key = k.item_key AND l.loc = k.loc
     LEFT JOIN (
       SELECT item_key, 'auction' AS loc, sum(count) AS s FROM auction_listings WHERE status = 'active' GROUP BY 1
       UNION ALL
       SELECT item_key, 'mail', sum(count) FROM mails
        WHERE item_key IS NOT NULL AND claimed_at IS NULL AND expired_at IS NULL GROUP BY 1) h
       ON h.item_key = k.item_key AND h.loc = k.loc`,
  );
  for (const r of items.rows) {
    if (Number(r.led) !== Number(r.held)) {
      throw new Error(`아이템 보존식 불일치 ${r.loc}/${r.item_key}: 원장 ${r.led} 보관 ${r.held}`);
    }
  }
}

export const rid = (): string => randomUUID();
