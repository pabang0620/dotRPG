// PA13: 환불·차지백 주문의 결과물 회수(owner, 미리보기 후 적용, Docs/server/phase11_payments.md 9.5).
// 자동 회수는 없다(정직한 환불자의 계정 물건을 서버가 빼앗지 않는다): 부채와 정지가 막고, 확정적인 부정만 owner가 이 도구로 되돌린다.
// 대상: 그 주문의 별조각이 배분된 소비 원장 줄(gacha, exchange)의 결과. 뽑기는 그 요청의 결과 전체를 본다(한 요청이 여러 주문 로트에 걸쳐도 같다).
// 외형 최초 획득은 외형 행 삭제, 중복 획득은 여분 -1, 장비는 가방·창고에 남은 수량만 item_ledger(admin_clawback), 게이지는 뽑은 칸 수만큼.
// 합성 결과(star_synth_log)와 착용 해제는 이 도구의 범위 밖이다(TODO: 착용 중인 외형을 서버가 저장하는지 확인, 구현 기록 20절).
import type { PoolClient } from 'pg';
import { hashRequest } from '../../db/idempotency';
import { getPool, type Queryable } from '../../db/pool';
import * as econRepo from '../../domains/economy/economyRepository';
import * as payRepo from '../../domains/payments/paymentsRepository';
import { lockWallet, readWallet, setPity } from '../../domains/starshop/starWallet';
import { AppError } from '../../utils/AppError';
import type { AdminCtx } from '../common/adminTypes';
import { auditView, runAdminAction, type ActionResult } from '../common/audit';
import { ORDER_NOT_FOUND } from './paymentsAdminService';
import type { RevokeBody } from './paymentsAdminValidation';

interface Plan {
  order: string;
  cosmetics: { item_id: string; remove: boolean; copies: number }[];
  gear: { character_id: string; item_key: string; count: number }[];
  gauge: { aura: number; skin: number };
}

async function buildPlan(db: Queryable, order: payRepo.OrderRow, include: RevokeBody['include']): Promise<{ plan: Plan; characters: Map<string, number> }> {
  const lines = await db.query<{ ledger_id: string; reason: string; ref: string | null; request_id: string | null }>(
    `SELECT DISTINCT l.id AS ledger_id, l.reason, l.ref, l.request_id FROM star_spend_allocs a JOIN star_ledger l ON l.id = a.ledger_id WHERE a.order_id = $1 ORDER BY l.id`,
    [order.id],
  );
  const cos = new Map<string, { remove: boolean; copies: number }>();
  const gear = new Map<string, { character_id: string; item_key: string; count: number }>();
  const characters = new Map<string, number>();
  const gauge = { aura: 0, skin: 0 };
  const note = (id: string, remove: boolean, copies: number): void => {
    const cur = cos.get(id) ?? { remove: false, copies: 0 };
    cos.set(id, { remove: cur.remove || remove, copies: cur.copies + copies });
  };
  for (const l of lines.rows) {
    if (l.reason === 'exchange' && l.ref) {
      note(l.ref, true, 0);
      continue;
    }
    if (l.reason !== 'gacha' || !l.request_id) continue;
    const pulls = await db.query<{ item_id: string; duplicate: boolean; kind: string; banner: string }>(
      'SELECT item_id, duplicate, kind, banner FROM gacha_pulls WHERE account_id = $1 AND request_id = $2 ORDER BY seq',
      [order.account_id, l.request_id],
    );
    for (const p of pulls.rows) {
      if (p.kind === 'cosmetic') {
        if (p.duplicate) note(p.item_id, false, 1);
        else note(p.item_id, true, 0);
        if (p.banner === 'aura') gauge.aura++;
        else if (p.banner === 'skin') gauge.skin++;
      }
    }
    const g = await db.query<{ uuid: string; character_id: string; item_key: string; delta: number }>(
      `SELECT c.uuid, i.character_id, i.item_key, i.delta FROM item_ledger i JOIN characters c ON c.id = i.character_id
        WHERE i.request_id = $1 AND i.reason = 'gacha' AND i.delta > 0 ORDER BY i.id`,
      [l.request_id],
    );
    for (const x of g.rows) {
      const k = `${x.uuid}|${x.item_key}`;
      const cur = gear.get(k) ?? { character_id: x.uuid, item_key: x.item_key, count: 0 };
      gear.set(k, { ...cur, count: cur.count + x.delta });
      characters.set(x.uuid, Number(x.character_id));
    }
  }
  const plan: Plan = {
    order: order.uuid,
    cosmetics: include.cosmetics ? [...cos].map(([item_id, v]) => ({ item_id, ...v })).sort((a, b) => a.item_id.localeCompare(b.item_id)) : [],
    gear: include.gear ? [...gear.values()].sort((a, b) => (a.character_id + a.item_key).localeCompare(b.character_id + b.item_key)) : [],
    gauge: include.gauge ? gauge : { aura: 0, skin: 0 },
  };
  return { plan, characters };
}

const hashOf = (plan: Plan, include: RevokeBody['include']): string => hashRequest({ plan, include });

async function loadRevocable(db: Queryable, uuid: string): Promise<payRepo.OrderRow> {
  const o = await payRepo.orderByUuid(db, uuid);
  if (!o) throw ORDER_NOT_FOUND();
  if (o.state !== 'refunded' && o.state !== 'chargeback') throw new AppError(409, '환불·차지백된 주문만 결과물을 회수할 수 있습니다.', 'PAYMENT_STATE');
  return o;
}

export async function revokeOutcomes(admin: AdminCtx, ip: string, uuid: string, body: RevokeBody): Promise<ActionResult> {
  if (body.mode === 'preview') {
    const db = getPool();
    const o = await loadRevocable(db, uuid);
    const { plan } = await buildPlan(db, o, body.include);
    await auditView(admin, ip, 'payment.revoke_outcomes.preview', 'payment_order', uuid);
    return { body: { status: 200, message: '', data: { mode: 'preview', targets: plan, preview_hash: hashOf(plan, body.include) } }, replay: false };
  }
  if (!body.preview_hash) throw new AppError(400, '입력값이 올바르지 않습니다.', 'VALIDATION', { fields: [{ path: 'preview_hash', message: 'apply 에는 미리보기 해시가 필요합니다' }] });
  const wanted = body.preview_hash;
  return runAdminAction({
    admin,
    ip,
    action: 'payment.revoke_outcomes',
    targetType: 'payment_order',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { include: body.include, preview_hash: wanted, note_length: body.note.length },
    handler: async (client) => {
      const o = await loadRevocable(client, uuid);
      const first = await buildPlan(client, o, body.include);
      // 본 것과 적용하는 것이 같음을 보장한다
      if (hashOf(first.plan, body.include) !== wanted) throw new AppError(409, '미리보기 이후 대상이 바뀌었습니다. 다시 미리보기를 하세요.', 'PREVIEW_CHANGED');
      // 락 순서: 캐릭터 행(id 순, ①) -> 지갑(④)
      const ids = [...first.characters.values()].sort((a, b) => a - b);
      if (ids.length > 0) await econRepo.lockCharacters(client, ids);
      const summary = await apply(client, o, first.plan, first.characters, body.request_id);
      await payRepo.insertEvent(client, o.id, { kind: 'outcomes_revoked', actor: 'admin', detail: { cosmetics: summary.cosmetics_removed, copies: summary.copies_removed, gear: summary.gear_removed, shortfall: summary.gear_shortfall, gauge: summary.gauge } });
      return { status: 200, data: { mode: 'apply', ...summary } };
    },
  });
}

async function apply(client: PoolClient, o: payRepo.OrderRow, plan: Plan, characters: Map<string, number>, requestId: string) {
  let cosmeticsRemoved = 0;
  let copiesRemoved = 0;
  for (const c of plan.cosmetics) {
    if (c.remove) {
      const r = await client.query('DELETE FROM account_cosmetics WHERE account_id = $1 AND item_id = $2', [o.account_id, c.item_id]);
      cosmeticsRemoved += r.rowCount ?? 0;
    } else if (c.copies > 0) {
      const r = await client.query<{ copies: number }>('UPDATE account_cosmetics SET copies = greatest(copies - $3, 0) WHERE account_id = $1 AND item_id = $2 RETURNING copies', [o.account_id, c.item_id, c.copies]);
      copiesRemoved += r.rowCount ? c.copies : 0;
    }
  }
  let gearRemoved = 0;
  let shortfall = 0;
  for (const g of plan.gear) {
    const characterId = characters.get(g.character_id) as number;
    let left = g.count;
    // 가방 먼저, 모자라면 창고. 착용 중인 것은 회수하지 않는다(강화로 키가 바뀐 것도 추적하지 않고 shortfall 로 보고한다)
    for (const loc of ['bag', 'storage'] as const) {
      if (left <= 0) break;
      const have = await econRepo.stackCount(client, characterId, loc, g.item_key);
      const take = Math.min(have, left);
      if (take <= 0) continue;
      if ((await econRepo.decStack(client, characterId, loc, g.item_key, take)) === null) continue;
      await econRepo.insertItemLedger(client, characterId, g.item_key, -take, have - take, loc, 'admin_clawback', o.uuid, requestId);
      left -= take;
      gearRemoved += take;
    }
    shortfall += left;
  }
  const gauge = { aura: 0, skin: 0 };
  if (plan.gauge.aura > 0 || plan.gauge.skin > 0) {
    await lockWallet(client, o.account_id);
    const w = await readWallet(client, o.account_id);
    gauge.aura = Math.min(w.pity, plan.gauge.aura);
    gauge.skin = Math.min(w.skinPity, plan.gauge.skin);
    if (gauge.aura > 0) await setPity(client, o.account_id, w.pity - gauge.aura, false);
    if (gauge.skin > 0) await setPity(client, o.account_id, w.skinPity - gauge.skin, true);
  }
  return { cosmetics_removed: cosmeticsRemoved, copies_removed: copiesRemoved, gear_removed: gearRemoved, gear_shortfall: shortfall, gauge };
}
