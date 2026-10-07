// 봉인된 상자(Docs/PLAN_CASH_BOX_PASS.md 2절·6절): 캐시샵 뽑기, 확률 상자·봉인된 상자 아이템 열기.
// 결과·확률·수량은 서버가 정한다. 부스터 게이지는 계정별로 저장하고 모든 열기(뽑기·아이템)가 같은 게이지를 쓴다.
// 게이지 규칙: 일반 열기마다 +1, 10이 되면 다음 1회는 부스터이고 열고 나면 0(= 11번째마다 부스터).
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { getSealedBox, sealedTable, type RatedRow, type SealedData } from '../../gamedata/sealedBox';
import { AppError } from '../../utils/AppError';
import { getRng } from '../../utils/rng';
import { assertNoHold } from '../antiabuse/holds';
import type { EconCtx } from '../economy/economyContext';
import { runEconomy, type StoredResult } from '../economy/economyService';
import { assertSpendAllowed } from '../starshop/starSpend';
import * as wallets from '../starshop/starWallet';
import * as tickets from '../sweep/ticketWallet';
import { grantItems, type ItemCount } from './sealedBoxGrant';
import * as repo from './sealedBoxRepository';
import type { OpenBody, PullBody } from './sealedBoxValidation';

/** 난수 해상도: 0.000001% (가장 낮은 확률 0.008%의 8000분의 1) */
const ROLL_SCALE = 100_000_000;

export interface OpenResult {
  item_key: string;
  count: number;
  boosted: boolean;
  tier: 'common' | 'rare';
  /** 한 번에 여러 아이템이 나오는 칸(상급 물약 묶음)도 전부 */
  rewards: ItemCount[];
  row_id: string;
  grade: string;
}

export function boosterView(gauge: number, d: SealedData = getSealedBox()): { gauge: number; next_boosted: boolean } {
  return { gauge, next_boosted: gauge >= d.boosterEvery };
}

/** 표 한 줄을 고른다(누적 확률). 마지막 줄은 부동소수 오차를 받는다 */
export function pickRow(table: RatedRow[]): RatedRow {
  const roll = (getRng().int(0, ROLL_SCALE) / ROLL_SCALE) * 100;
  let acc = 0;
  for (const t of table) {
    acc += t.rate;
    if (roll < acc) return t;
  }
  return table[table.length - 1] as RatedRow;
}

function tableView(table: RatedRow[]) {
  return table.map(({ row, rate, mult }) => ({
    item_key: (row.rewards[0] as ItemCount).item_key,
    count: (row.rewards[0] as ItemCount).count * mult,
    rate,
    tier: row.tier,
    rewards: row.rewards.map((r) => ({ item_key: r.item_key, count: r.count * mult })),
    row_id: row.id,
    grade: row.grade,
  }));
}

/** GET /starshop/sealed: 가격, 부스터 게이지, 일반 표와 부스터 표(확률 보기 창이 그대로 보여 준다) */
export async function summary(accountId: number) {
  const d = getSealedBox();
  const s = await repo.readState(getPool(), accountId);
  return {
    price_one: d.priceOne,
    price_eleven: d.priceEleven,
    eleven_count: d.elevenCount,
    rates_version: d.ratesVersion,
    booster_every: d.boosterEvery,
    booster: boosterView(s.gauge, d),
    table: tableView(sealedTable(d, false)),
    boosted_table: tableView(sealedTable(d, true)),
  };
}

/** 한 번 연다: 게이지를 보고 일반/부스터 표를 고르고 굴린 뒤 게이지를 갱신한다(state를 직접 바꾼다) */
function openOnce(d: SealedData, state: repo.BoosterState): { result: OpenResult; rate: number; before: number; after: number } {
  const before = state.gauge;
  const boosted = before >= d.boosterEvery;
  const picked = pickRow(sealedTable(d, boosted));
  state.gauge = boosted ? 0 : before + 1;
  state.totalOpens += 1;
  const rewards = picked.row.rewards.map((r) => ({ item_key: r.item_key, count: r.count * picked.mult }));
  const first = rewards[0] as ItemCount;
  return {
    result: { item_key: first.item_key, count: first.count, boosted, tier: picked.row.tier, rewards, row_id: picked.row.id, grade: picked.row.grade },
    rate: picked.rate,
    before,
    after: state.gauge,
  };
}

function assertRatesAck(seen: string | undefined, d: SealedData): void {
  if (seen === undefined) {
    if (getConfig().pay.ratesAckRequired) throw new AppError(400, '확률표 버전(rates_version)을 함께 보내야 합니다.', 'VALIDATION', { fields: [{ path: 'rates_version', message: '필수 값' }] });
    return;
  }
  if (seen !== d.ratesVersion) throw new AppError(409, '확률표가 바뀌었습니다. 다시 확인해 주세요.', 'RATES_CHANGED', { rates_version: d.ratesVersion });
}

/** POST /characters/:uuid/starshop/sealed/pull: 별조각 차감 -> 부스터 반영하며 굴림 -> 가방 지급 -> 결과 기록 */
export function pull(accountId: number, characterUuid: string, body: PullBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/starshop/sealed/pull',
    requestId,
    payload,
    handler: async (ctx) => {
      const d = getSealedBox();
      assertRatesAck(body.rates_version, d);
      await assertNoHold(ctx.client, accountId, ctx.char.id);
      const db = ctx.client;
      const price = body.count === 11 ? d.priceEleven : d.priceOne;
      const times = body.count === 11 ? d.elevenCount : 1;
      // 락 순서: 캐릭터(runEconomy) -> 계정(클리어권 지급용) -> 별조각 지갑 -> 부스터 행
      await tickets.lockAccount(db, accountId);
      const wallet = await wallets.lockWallet(db, accountId);
      wallets.assertNoStarDebt(wallet);
      if (wallet.balance < price) throw new AppError(422, '별조각이 모자랍니다.', 'NOT_ENOUGH_STARS', { need: price, have: wallet.balance });
      await assertSpendAllowed(db, accountId, price, ctx.now);
      const paid = await wallets.debit(db, { accountId, reason: 'sealed_pull', price, ref: `sealed_${body.count}`, requestId });

      const state = await repo.lockState(db, accountId);
      const results: OpenResult[] = [];
      const log: repo.PullLogRow[] = [];
      for (let i = 0; i < times; i++) {
        const o = openOnce(d, state);
        results.push(o.result);
        log.push({ seq: i, source: 'shop', rowId: o.result.row_id, itemKey: o.result.item_key, count: o.result.count, tier: o.result.tier, boosted: o.result.boosted, rate: o.rate, gaugeBefore: o.before, gaugeAfter: o.after });
      }
      await grantItems(ctx, results.flatMap((r) => r.rewards), 'sealed_box', `sealed_${body.count}`);
      await repo.saveState(db, accountId, state);
      await repo.insertPulls(db, { accountId, characterId: ctx.char.id, requestId, version: d.ratesVersion, rows: log });
      return {
        status: 200,
        data: { results, booster: boosterView(state.gauge, d), delta: ctx.delta(), balance: paid.balance, rates_version: d.ratesVersion },
      };
    },
  });
}

/** POST /characters/:uuid/items/open: 확률 상자(+N 강화권 또는 마력 정수)와 봉인된 상자 아이템 */
export function openItem(accountId: number, characterUuid: string, body: OpenBody): Promise<StoredResult> {
  const { request_id: requestId, ...payload } = body;
  return runEconomy({
    accountId,
    characterUuid,
    endpoint: 'POST /characters/:uuid/items/open',
    requestId,
    payload,
    handler: async (ctx) => {
      const d = getSealedBox();
      const items = getGameData().economy.items;
      const info = items.get(body.item_key);
      if (!info || (info.use !== 'LuckBox' && info.use !== 'SealedBox')) throw new AppError(422, '열 수 없는 아이템입니다.', 'NOT_OPENABLE');
      await assertNoHold(ctx.client, accountId, ctx.char.id);
      const have = await ctx.stackCount('bag', body.item_key);
      if (have < 1) throw new AppError(422, '수량이 모자랍니다.', 'NOT_ENOUGH_ITEMS', { need: 1, have });
      await tickets.lockAccount(ctx.client, accountId);
      const consumed = await ctx.removeItem('bag', body.item_key, 1, 'box_open', body.item_key);
      if (consumed === null) throw new AppError(422, '수량이 모자랍니다.', 'NOT_ENOUGH_ITEMS', { need: 1, have });

      if (info.use === 'LuckBox') {
        const ticketKey = `ticket_enh${info.power}`;
        if (!items.has(ticketKey)) throw new Error(`확률 상자 ${body.item_key} 의 강화권 ${ticketKey} 이 items.json에 없습니다`);
        // chance는 정수 %(30, 10, 50). 0 이상 chance 미만이면 성공
        const hit = getRng().int(0, 100) < info.chance;
        const reward: ItemCount = hit ? { item_key: ticketKey, count: 1 } : d.luckBoxFallback;
        await grantItems(ctx, [reward], 'box_open', body.item_key);
        const result: OpenResult = { item_key: reward.item_key, count: reward.count, boosted: false, tier: hit ? 'rare' : 'common', rewards: [reward], row_id: body.item_key, grade: hit ? 'rare' : 'normal' };
        await repo.insertPulls(ctx.client, {
          accountId,
          characterId: ctx.char.id,
          requestId,
          version: d.ratesVersion,
          rows: [{ seq: 0, source: 'luck_box', rowId: body.item_key, itemKey: reward.item_key, count: reward.count, tier: result.tier, boosted: false, rate: info.chance, gaugeBefore: null, gaugeAfter: null }],
        });
        return { status: 200, data: { item_key: body.item_key, results: [result], delta: ctx.delta() } };
      }

      const state = await repo.lockState(ctx.client, accountId);
      const o = openOnce(d, state);
      await grantItems(ctx, o.result.rewards, 'box_open', body.item_key);
      await repo.saveState(ctx.client, accountId, state);
      await repo.insertPulls(ctx.client, {
        accountId,
        characterId: ctx.char.id,
        requestId,
        version: d.ratesVersion,
        rows: [{ seq: 0, source: 'sealed_item', rowId: o.result.row_id, itemKey: o.result.item_key, count: o.result.count, tier: o.result.tier, boosted: o.result.boosted, rate: o.rate, gaugeBefore: o.before, gaugeAfter: o.after }],
      });
      return { status: 200, data: { item_key: body.item_key, results: [o.result], booster: boosterView(state.gauge, d), delta: ctx.delta() } };
    },
  });
}
