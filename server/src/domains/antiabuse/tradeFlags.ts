// 성공한 의심 거래 기록(7.4). 체결을 거절하지 않고 auction_trade_flags에 표시만 남긴다(추가 전용).
// 표시는 관리자 목록(H7), 경제 정지의 경매 가중(auction_in_w), 정지 전파의 근거가 된다.
import type { PoolClient } from 'pg';
import { getConfig } from '../../config/env';
import { limitsOf, referenceOf } from '../auction/auctionPricing';

export type TradeFlag = 'CEILING_PRICE' | 'NEW_BUYER' | 'SAME_DEVICE' | 'SAME_STEAM' | 'SAME_IP' | 'PAIR_REPEAT';

/** 경제 정지 가중(weight_pct): 거래 가중치는 플래그 중 최댓값(합산하지 않는다) */
export const FLAG_WEIGHT: Record<TradeFlag, number> = {
  CEILING_PRICE: 150,
  NEW_BUYER: 200,
  SAME_DEVICE: 300,
  SAME_STEAM: 300,
  SAME_IP: 150,
  PAIR_REPEAT: 150,
};

const DAY_MS = 86_400_000;

export interface TradeFacts {
  tradeId: number;
  itemKey: string;
  count: number;
  price: number;
  sellerAccountId: number;
  buyerAccountId: number;
  now: Date;
}

/** 두 계정이 days일 안에 같은 기기를 썼는가 */
export async function shareDevice(db: Pick<PoolClient, 'query'>, a: number, b: number, since: Date): Promise<boolean> {
  const r = await db.query(
    `SELECT 1 FROM account_devices x JOIN account_devices y ON y.device_hash = x.device_hash
      WHERE x.account_id = $1 AND y.account_id = $2 AND x.last_seen_at > $3 AND y.last_seen_at > $3 LIMIT 1`,
    [a, b, since],
  );
  return r.rows.length > 0;
}

/** 두 계정의 Steam 소유자 키(COALESCE(steam_owner_id, subject))가 같은가 */
export async function shareSteam(db: Pick<PoolClient, 'query'>, a: number, b: number): Promise<boolean> {
  const r = await db.query(
    `SELECT 1 FROM auth_identities x JOIN auth_identities y
        ON COALESCE(y.steam_owner_id, y.subject) = COALESCE(x.steam_owner_id, x.subject)
     WHERE x.account_id = $1 AND y.account_id = $2 AND x.provider = 'steam' AND y.provider = 'steam' LIMIT 1`,
    [a, b],
  );
  return r.rows.length > 0;
}

async function shareIp(db: Pick<PoolClient, 'query'>, a: number, b: number, since: Date): Promise<boolean> {
  const r = await db.query(
    `SELECT 1 FROM account_ips x JOIN account_ips y ON y.ip = x.ip
      WHERE x.account_id = $1 AND y.account_id = $2 AND x.last_seen_at > $3 AND y.last_seen_at > $3 LIMIT 1`,
    [a, b, since],
  );
  return r.rows.length > 0;
}

/**
 * 체결 직후 같은 트랜잭션에서 부른다(틱 정산에서도 같다). 걸린 플래그를 기록하고 거래 가중치(%, 플래그 최댓값, 없으면 100)를 돌려준다.
 * 같은 플래그가 중복으로 들어가지 않는다(UNIQUE(trade_id, flag)).
 */
export async function evaluateTrade(client: PoolClient, t: TradeFacts): Promise<{ flags: TradeFlag[]; weightPct: number }> {
  const cfg = getConfig().aa.auction;
  const found: { flag: TradeFlag; detail: Record<string, unknown> }[] = [];

  // CEILING_PRICE: 체결 시점에 다시 계산한 가격 상한의 비율 이상
  const ref = await referenceOf(client, t.itemKey, t.now);
  const limits = limitsOf(ref, t.count, t.itemKey);
  if (limits && t.price >= cfg.flagCeilRatio * limits.max) found.push({ flag: 'CEILING_PRICE', detail: { price: t.price, max: limits.max } });

  // NEW_BUYER: 구매자 계정이 새 계정이고 가격이 하한 이상(싼 거래의 소음 방지)
  if (t.price >= cfg.flagMinPrice) {
    const created = await client.query<{ created_at: Date }>('SELECT created_at FROM accounts WHERE id = $1', [t.buyerAccountId]);
    const at = created.rows[0]?.created_at;
    if (at && t.now.getTime() - at.getTime() < cfg.flagNewBuyerDays * DAY_MS) found.push({ flag: 'NEW_BUYER', detail: { price: t.price } });
  }
  if (await shareDevice(client, t.sellerAccountId, t.buyerAccountId, new Date(t.now.getTime() - cfg.flagDeviceDays * DAY_MS))) found.push({ flag: 'SAME_DEVICE', detail: {} });
  if (await shareSteam(client, t.sellerAccountId, t.buyerAccountId)) found.push({ flag: 'SAME_STEAM', detail: {} });
  if (await shareIp(client, t.sellerAccountId, t.buyerAccountId, new Date(t.now.getTime() - cfg.flagIpDays * DAY_MS))) found.push({ flag: 'SAME_IP', detail: {} });
  const pair = await client.query<{ n: string }>(
    `SELECT count(*) AS n FROM auction_trades
      WHERE ((seller_account_id = $1 AND buyer_account_id = $2) OR (seller_account_id = $2 AND buyer_account_id = $1)) AND traded_at > $3`,
    [t.sellerAccountId, t.buyerAccountId, new Date(t.now.getTime() - 7 * DAY_MS)],
  );
  const repeats = Number((pair.rows[0] as { n: string }).n);
  if (repeats >= cfg.flagPairRepeat) found.push({ flag: 'PAIR_REPEAT', detail: { trades_7d: repeats } });

  for (const f of found) {
    await client.query(
      `INSERT INTO auction_trade_flags (trade_id, flag, weight_pct, detail) VALUES ($1, $2, $3, $4::jsonb) ON CONFLICT (trade_id, flag) DO NOTHING`,
      [t.tradeId, f.flag, FLAG_WEIGHT[f.flag], JSON.stringify(f.detail)],
    );
  }
  return { flags: found.map((f) => f.flag), weightPct: found.reduce((m, f) => Math.max(m, FLAG_WEIGHT[f.flag]), 100) };
}
