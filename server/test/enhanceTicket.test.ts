// 14단계: 강화권(POST /characters/:uuid/enhance/ticket)
import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import * as enhance from '../src/domains/enhance/enhanceService';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { buildApp, resetDb, shutdown } from './helpers';
import { countOf, expectLedgerConsistent, newHero, post, seedItem, seedWorn, wornOf, type Hero } from './economyHelpers';

const app = buildApp();
beforeAll(resetDb);
beforeEach(() => getRateLimitStore().clear());
afterAll(shutdown);

const ticket = (h: Hero, ticketKey: string, gearKey: string, rid?: string) => post(app, h, '/enhance/ticket', { ticket_key: ticketKey, gear_key: gearKey }, rid);

describe('강화권', () => {
  it('가방 장비 +3 -> +12: 키가 바뀌고 강화권 1장 소모, 비용 없음, 기록 outcome=ticket', async () => {
    const h = await newHero(app);
    await seedItem(h, 'eq_sword_1_c+3', 1);
    await seedItem(h, 'ticket_enh12', 2);
    const r = await ticket(h, 'ticket_enh12', 'eq_sword_1_c+3');
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ gear_key_before: 'eq_sword_1_c+3', gear_key_after: 'eq_sword_1_c+12' });
    expect(await countOf(h, 'eq_sword_1_c+3')).toBe(0);
    expect(await countOf(h, 'eq_sword_1_c+12')).toBe(1);
    expect(await countOf(h, 'ticket_enh12')).toBe(1);
    const log = await getPool().query("SELECT from_key, to_key, outcome, target_location, gold FROM enhance_log WHERE character_id = $1", [h.dbId]);
    expect(log.rows).toEqual([{ from_key: 'eq_sword_1_c+3', to_key: 'eq_sword_1_c+12', outcome: 'ticket', target_location: 'bag', gold: 0 }]);
    await expectLedgerConsistent(h);
  });

  it('착용 중인 장비에도 쓴다(슬롯 유지)', async () => {
    const h = await newHero(app);
    await seedWorn(h, 0, 'eq_sword_1_c+3');
    await seedItem(h, 'ticket_enh10', 1);
    const r = await ticket(h, 'ticket_enh10', 'eq_sword_1_c+3');
    expect(r.status).toBe(200);
    expect(await wornOf(h, 0)).toBe('eq_sword_1_c+10');
    expect(await countOf(h, 'ticket_enh10')).toBe(0);
    const log = await getPool().query("SELECT target_location, target_slot FROM enhance_log WHERE character_id = $1", [h.dbId]);
    expect(log.rows).toEqual([{ target_location: 'worn', target_slot: 0 }]);
    await expectLedgerConsistent(h);
  });

  it('같거나 높은 단계는 409 ALREADY_HIGHER(+12 -> +12, +15 -> +12), 강화권은 그대로', async () => {
    const h = await newHero(app);
    await seedItem(h, 'eq_sword_1_c+12', 1);
    await seedItem(h, 'eq_staff_oak+15', 1);
    await seedItem(h, 'ticket_enh12', 2);
    for (const gear of ['eq_sword_1_c+12', 'eq_staff_oak+15']) {
      const r = await ticket(h, 'ticket_enh12', gear);
      expect(r.status).toBe(409);
      expect(r.body.errors.code).toBe('ALREADY_HIGHER');
    }
    expect(await countOf(h, 'ticket_enh12')).toBe(2);
    expect(await countOf(h, 'eq_staff_oak+15')).toBe(1);
  });

  it('장비가 아니거나 없거나 강화권이 아니면 거절, 강화권이 없으면 NOT_ENOUGH_ITEMS', async () => {
    const h = await newHero(app);
    await seedItem(h, 'potion_hp', 1);
    await seedItem(h, 'ticket_enh10', 1);
    await seedItem(h, 'eq_sword_1_c', 1);
    expect((await ticket(h, 'ticket_enh10', 'potion_hp')).body.errors.code).toBe('ENHANCE_INVALID_TARGET');
    expect((await ticket(h, 'ticket_enh10', 'eq_sword_10_c')).body.errors.code).toBe('ENHANCE_INVALID_TARGET'); // 가지고 있지 않다
    expect((await ticket(h, 'potion_hp', 'eq_sword_1_c')).body.errors.code).toBe('NOT_A_TICKET');
    expect((await ticket(h, 'ticket_enh12', 'eq_sword_1_c')).body.errors.code).toBe('NOT_ENOUGH_ITEMS');
    expect(await countOf(h, 'ticket_enh10')).toBe(1);
  });

  it('입력 오류: 단계·결과 필드는 받지 않는다(strict)', async () => {
    const h = await newHero(app);
    const r = await post(app, h, '/enhance/ticket', { ticket_key: 'ticket_enh10', gear_key: 'eq_sword_1_c', level: 15 });
    expect(r.status).toBe(400);
  });

  it('같은 request_id 재전송은 한 번만 소모, 동시 사용은 강화권 1장이면 한 번만 성공', async () => {
    const h = await newHero(app);
    await seedItem(h, 'eq_sword_1_c', 1);
    await seedItem(h, 'ticket_enh10', 1);
    const rid = randomUUID();
    const a = await ticket(h, 'ticket_enh10', 'eq_sword_1_c', rid);
    const b = await ticket(h, 'ticket_enh10', 'eq_sword_1_c', rid);
    expect(b.body.data).toEqual(a.body.data);
    expect(await countOf(h, 'eq_sword_1_c+10')).toBe(1);

    const acct = Number((await getPool().query<{ account_id: string }>('SELECT account_id FROM characters WHERE id = $1', [h.dbId])).rows[0]!.account_id);
    await seedItem(h, 'eq_staff_oak+1', 1);
    await seedItem(h, 'ticket_enh10', 1);
    const rs = await Promise.allSettled([
      enhance.applyTicket(acct, h.id, { request_id: randomUUID(), ticket_key: 'ticket_enh10', gear_key: 'eq_staff_oak+1' }),
      enhance.applyTicket(acct, h.id, { request_id: randomUUID(), ticket_key: 'ticket_enh10', gear_key: 'eq_staff_oak+1' }),
    ]);
    expect(rs.filter((r) => r.status === 'fulfilled')).toHaveLength(1);
    expect(await countOf(h, 'eq_staff_oak+10')).toBe(1);
    expect(await countOf(h, 'ticket_enh10')).toBe(0);
    await expectLedgerConsistent(h);
  });
});
