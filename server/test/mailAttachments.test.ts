// 10단계 14절 D: 우편 확장(첨부 여러 개, 제목·본문, 탭 필터, 수령 분기, 폐기, 보존식)과 옛 우편 회귀
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import * as cache from '../src/domains/mail/campaignCache';
import { runAuctionTick } from '../src/domains/auction/auctionTicker';
import { integrityJob } from '../src/ops/jobs/integrity';
import { countOf, expectLedgerConsistent, get, goldOf, newHero, post, seedGold, type Hero } from './economyHelpers';
import { advance, resetClock } from './auctionHelpers';
import { activeCampaign, campaignMails, DAY, deliverTo, twoOwners } from './campaignHelpers';
import { adminApp, adminPost } from './opsHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { accountIdOf, expectTicketLedgerConsistent, ticketTotal } from './sweepHelpers';

let app: Express;
let admin: Express;
beforeEach(async () => {
  await resetDb();
  resetClock();
  cache.invalidateCampaignCache();
  app = buildApp({ SWEEP_ENABLED: 'true', CAMPAIGN_DELIVERY_ENABLED: 'true', CAMPAIGN_MAX_GOLD_TOTAL: '100000000' });
  admin = adminApp();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

const count = async (sql: string, params: unknown[] = []): Promise<number> => Number(((await getPool().query<{ n: string }>(sql, params)).rows[0] as { n: string }).n);
type MailView = { id: string; title: string | null; body: string | null; campaign: boolean; attachments: { slot: number; kind: string; item_key: string | null; count: number; bind: string | null; valid_days_after_claim?: number }[]; item: unknown; gold: number; days_left: number; system_code?: string };
const mails = async (h: Hero, tab = 'all'): Promise<MailView[]> => (await get(app, h, `/mail?tab=${tab}&limit=50`)).body.data.mails as MailView[];

/** 캠페인 우편 한 통을 받은 영웅 */
async function withCampaignMail(over: Record<string, unknown> = {}): Promise<{ h: Hero; mailId: string; campaignId: string }> {
  const [o1, o2] = await twoOwners();
  const h = await newHero(app);
  const campaignId = await activeCampaign(admin, o1, o2, over);
  expect(await deliverTo(h)).toBe(1);
  return { h, mailId: (await campaignMails(campaignId))[0]?.uuid as string, campaignId };
}

describe('D1 옛 우편 회귀', () => {
  it('운영 지급 우편(admin_grants)은 그대로 목록·수령되고 attachments가 합성된다(title/body는 null)', async () => {
    const owner = (await twoOwners())[0];
    const h = await newHero(app);
    const g = await adminPost(admin, owner, '/admin/grants', { character_id: h.id, system_code: 'compensation', gold: 700, item: { item_key: 'potion_hp', count: 3 }, memo: '회귀' });
    expect(g.status).toBe(201);
    const [m] = await mails(h);
    expect(m).toMatchObject({ title: null, body: null, campaign: false, gold: 700, system_code: 'compensation' });
    expect(m?.item).toEqual({ item_key: 'potion_hp', count: 3, bind: 'none' });
    expect(m?.attachments).toEqual([
      { slot: 1, kind: 'gold', item_key: null, count: 700, bind: null },
      { slot: 2, kind: 'item', item_key: 'potion_hp', count: 3, bind: 'none' },
    ]);
    const res = await post(app, h, `/mail/${m?.id}/claim`, {});
    expect(res.status).toBe(200);
    expect(res.body.data.claimed).toMatchObject({ gold: 700, item: { item_key: 'potion_hp', count: 3 } });
    expect(res.body.data.claimed.attachments).toHaveLength(2);
    expect(res.body.data.claimed.tickets).toBeUndefined();
    expect(await goldOf(h)).toBe(800);
    await expectLedgerConsistent(h);
    // 보존식: system 우편 골드 = admin_grants 골드
    expect(await count("SELECT coalesce(sum(gold), 0) AS n FROM mails WHERE kind = 'system'")).toBe(await count('SELECT coalesce(sum(gold), 0) AS n FROM admin_grants'));
  });
});

describe('D2 캠페인 우편 목록(M1)', () => {
  it('제목, 본문(줄바꿈), 첨부 3종, 기한, 탭 필터가 첨부 기준이다', async () => {
    const { h } = await withCampaignMail();
    const owner = (await twoOwners())[0];
    // 아이템만 있는 옛 우편 한 통을 더한다
    await adminPost(admin, owner, '/admin/grants', { character_id: h.id, system_code: 'event', item: { item_key: 'potion_hp', count: 1 }, memo: '탭' });
    const all = await mails(h);
    expect(all).toHaveLength(2);
    const c = all.find((x) => x.campaign) as MailView;
    expect(c).toMatchObject({ title: '정기 점검 보상', body: '점검에 협조해 주셔서 감사합니다.\n작은 선물을 드립니다.', system_code: 'maintenance', item: null, gold: 0 });
    expect(c.attachments).toEqual([
      { slot: 1, kind: 'gold', item_key: null, count: 5000, bind: null },
      { slot: 2, kind: 'item', item_key: 'potion_hp', count: 10, bind: 'none' },
      { slot: 3, kind: 'sweep_ticket', item_key: 'ticket_sweep_event', count: 1, bind: null, valid_days_after_claim: 14 },
    ]);
    expect(c.days_left).toBe(14);
    expect((await mails(h, 'gold')).map((x) => x.campaign)).toEqual([true]);
    expect((await mails(h, 'item')).map((x) => x.campaign).sort()).toEqual([false, true]);
    expect((await get(app, h, '/mail?tab=all&limit=1&page=1')).body.meta).toMatchObject({ total: 2, page: 1, limit: 1 });
  });
});

describe('D3 수령(M2)', () => {
  it('골드 + 아이템 + 클리어권이 한 번에 들어가고 원장이 맞고 이벤트 로트 기한은 수령 + 14일, 재수령은 거절', async () => {
    const { h, mailId } = await withCampaignMail();
    const res = await post(app, h, `/mail/${mailId}/claim`, {});
    expect(res.status).toBe(200);
    expect(res.body.data.claimed.attachments).toHaveLength(3);
    expect(res.body.data.claimed.tickets).toMatchObject({ total: 1, normal: 0 });
    expect(await goldOf(h)).toBe(5100);
    expect(await countOf(h, 'potion_hp')).toBeGreaterThanOrEqual(10);
    const gl = await getPool().query("SELECT delta, ref FROM gold_ledger WHERE character_id = $1 AND reason = 'mail_claim'", [h.dbId]);
    expect(gl.rows).toEqual([{ delta: '5000', ref: mailId }]);
    const il = await getPool().query("SELECT delta, location, reason FROM item_ledger WHERE ref = $1 ORDER BY id", [mailId]);
    expect(il.rows).toEqual([
      { delta: 10, location: 'mail', reason: 'admin_grant' },
      { delta: -10, location: 'mail', reason: 'mail_claim' },
      { delta: 10, location: 'bag', reason: 'mail_claim' },
    ]);
    const tl = await getPool().query("SELECT delta, ref, character_id FROM sweep_ticket_ledger WHERE reason = 'campaign_claim'");
    expect(tl.rows).toEqual([{ delta: 1, ref: mailId, character_id: String(h.dbId) }]);
    const lot = (await getPool().query("SELECT kind, expires_at FROM sweep_ticket_lots")).rows[0];
    expect(lot.kind).toBe('event');
    expect(lot.expires_at.getTime()).toBeGreaterThan(Date.now() + 13.9 * DAY);
    expect(lot.expires_at.getTime()).toBeLessThan(Date.now() + 14.1 * DAY);
    await expectLedgerConsistent(h);
    await expectTicketLedgerConsistent(h);
    expect((await post(app, h, `/mail/${mailId}/claim`, {})).body.errors.code).toBe('MAIL_ALREADY_CLAIMED');
    expect(await mails(h)).toHaveLength(0);
  });

  it('같은 request_id 재전송은 같은 응답이고 이중 수령이 없다, 동시 두 요청은 하나만 성공한다', async () => {
    const { h, mailId, campaignId } = await withCampaignMail();
    const rid = (await import('node:crypto')).randomUUID();
    const a = await post(app, h, `/mail/${mailId}/claim`, {}, rid);
    const b = await post(app, h, `/mail/${mailId}/claim`, {}, rid);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect(await ticketTotal(h)).toBe(1);

    const h2 = await newHero(app);
    expect(await deliverTo(h2)).toBe(1);
    const mailId2 = (await campaignMails(campaignId)).find((m) => m.character_id === String(h2.dbId))?.uuid as string;
    const [x, y] = await Promise.all([post(app, h2, `/mail/${mailId2}/claim`, {}), post(app, h2, `/mail/${mailId2}/claim`, {})]);
    expect([x.status, y.status].sort()).toEqual([200, 409]);
    expect(await ticketTotal(h2)).toBe(1);
    expect(await count("SELECT count(*) AS n FROM sweep_ticket_ledger WHERE reason = 'campaign_claim' AND ref = $1", [mailId2])).toBe(1);
  });

  it('모두 받기(M3): 캠페인 우편이 섞여도 한 번에 받고 tickets가 응답에 온다', async () => {
    const { h } = await withCampaignMail();
    const owner = (await twoOwners())[0];
    await adminPost(admin, owner, '/admin/grants', { character_id: h.id, system_code: 'refund', gold: 300, memo: '모두' });
    const res = await post(app, h, '/mail/claim-all', {});
    expect(res.status).toBe(200);
    expect(res.body.data).toMatchObject({ claimed_count: 2, remaining: 0, skipped: 0, tickets: { total: 1 } });
    expect(await goldOf(h)).toBe(5400);
    await expectLedgerConsistent(h);
  });
});

describe('D4 골드 상한', () => {
  it('상한을 넘으면 우편 전체 거절(부분 수령 없음), 모두 받기는 건너뛰고 skipped에 센다', async () => {
    const { h, mailId } = await withCampaignMail();
    const cap = 2_147_483_647;
    await seedGold(h, cap - 100 - 100); // 보유 골드 = cap - 100
    const potionsBefore = await countOf(h, 'potion_hp');
    const res = await post(app, h, `/mail/${mailId}/claim`, {});
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('GOLD_CAP_EXCEEDED');
    expect(await countOf(h, 'potion_hp')).toBe(potionsBefore);
    expect(await ticketTotal(h)).toBe(0);
    expect(await count('SELECT count(*) AS n FROM mails WHERE claimed_at IS NOT NULL')).toBe(0);
    const all = await post(app, h, '/mail/claim-all', {});
    expect(all.body.data).toMatchObject({ claimed_count: 0, skipped: 1, remaining: 0 });
    expect(await goldOf(h)).toBe(cap - 100);
    expect(await mails(h)).toHaveLength(1);
  });
});

describe('D5 경제 정지', () => {
  it('정지 중에는 M2·M3 모두 403이고 클리어권 첨부도 못 받는다', async () => {
    const { h, mailId } = await withCampaignMail();
    await getPool().query(
      `INSERT INTO economy_holds (account_id, character_id, kind, state, window_kind, window_start, window_end, evidence)
       VALUES ($1, $2, 'manual', 'active', '24h', now() - interval '1 day', now() + interval '1 day', '{"metric":"xp","value":1,"cap":1}'::jsonb)`,
      [await accountIdOf(h), h.dbId],
    );
    expect((await post(app, h, `/mail/${mailId}/claim`, {})).body.errors.code).toBe('ECONOMY_HOLD');
    expect((await post(app, h, '/mail/claim-all', {})).body.errors.code).toBe('ECONOMY_HOLD');
    expect(await ticketTotal(h)).toBe(0);
    expect(await count('SELECT count(*) AS n FROM mails WHERE claimed_at IS NOT NULL')).toBe(0);
  });
});

describe('D6~D7 기한 폐기와 보존식', () => {
  it('기한이 지난 우편은 MAIL_EXPIRED, 폐기 틱이 아이템 mail_expire -n과 골드 소각 기록을 남기고 보존식이 맞다', async () => {
    const { h, mailId } = await withCampaignMail({ mail_days: 3 });
    advance(4 * DAY);
    const res = await post(app, h, `/mail/${mailId}/claim`, {});
    expect(res.status).toBe(410);
    expect(res.body.errors.code).toBe('MAIL_EXPIRED');
    expect(await mails(h)).toHaveLength(0);
    const tick = await runAuctionTick();
    expect(tick.mailsExpired).toBe(1);
    const il = await getPool().query("SELECT delta, location FROM item_ledger WHERE ref = $1 AND reason = 'mail_expire'", [mailId]);
    expect(il.rows).toEqual([{ delta: -10, location: 'mail' }]);
    expect(await count("SELECT coalesce(sum(amount), 0) AS n FROM auction_sinks WHERE kind = 'mail_expire'")).toBe(5000);
    expect(await ticketTotal(h)).toBe(0);
    const integ = await integrityJob({ opts: { full: true }, shouldStop: () => false });
    expect((integ.detail as { checks: Record<string, { count: number }> }).checks.I3?.count).toBe(0);
    // 같은 틱을 다시 돌려도 이중 폐기가 없다
    expect((await runAuctionTick()).mailsExpired).toBe(0);
  });

  it('우편이 열려 있는 동안과 수령 뒤에도 아이템·골드 보존식이 맞다', async () => {
    const { h, mailId } = await withCampaignMail();
    const check = async () => {
      const integ = await integrityJob({ opts: { full: true }, shouldStop: () => false });
      expect((integ.detail as { checks: Record<string, { count: number; samples: unknown[] }> }).checks.I3).toEqual({ count: 0, samples: [] });
    };
    await check();
    await post(app, h, `/mail/${mailId}/claim`, {});
    await check();
  });
});

describe('DB 제약', () => {
  it('첨부 우편은 item_key·gold를 비워야 하고, 첨부 수 범위·종류별 형태를 지킨다', async () => {
    const h = await newHero(app);
    const base = `INSERT INTO mails (character_id, kind, system_code, title, expires_at, attach_n, item_key, count, bind, gold) VALUES ($1, 'system', 'notice', 't', now() + interval '1 day', 1, $2, $3, $4, $5)`;
    await expect(getPool().query(base, [h.dbId, 'potion_hp', 1, 'none', 0])).rejects.toThrow(/mails_attach_mode_chk/);
    await expect(getPool().query(base, [h.dbId, null, null, null, 5])).rejects.toThrow(/mails_attach_mode_chk/);
    await expect(getPool().query("INSERT INTO mails (character_id, kind, system_code, expires_at, attach_n) VALUES ($1, 'system', 'notice', now() + interval '1 day', 6)", [h.dbId])).rejects.toThrow();
    // 옛 우편(첨부 표 없음, attach_n=0)은 내용이 비면 거절
    await expect(getPool().query("INSERT INTO mails (character_id, kind, system_code, expires_at) VALUES ($1, 'system', 'notice', now() + interval '1 day')", [h.dbId])).rejects.toThrow(/mails_content_chk/);
    const ok = await getPool().query(`INSERT INTO mails (character_id, kind, system_code, title, expires_at, attach_n) VALUES ($1, 'system', 'notice', 't', now() + interval '1 day', 1) RETURNING id`, [h.dbId]);
    const id = (ok.rows[0] as { id: string }).id;
    await expect(getPool().query("INSERT INTO mail_attachments (mail_id, slot, kind, item_key, amount, bind) VALUES ($1, 1, 'gold', 'x', 5, NULL)", [id])).rejects.toThrow(/mail_att_shape_chk/);
    await expect(getPool().query("INSERT INTO mail_attachments (mail_id, slot, kind, item_key, amount, bind) VALUES ($1, 1, 'sweep_ticket', 'ticket_sweep_event', 1001, NULL)", [id])).rejects.toThrow(/mail_att_shape_chk/);
    await getPool().query("INSERT INTO mail_attachments (mail_id, slot, kind, item_key, amount, bind) VALUES ($1, 1, 'gold', NULL, 5, NULL)", [id]);
    await expect(getPool().query("UPDATE mail_attachments SET amount = 9")).rejects.toThrow();
    await expect(getPool().query("INSERT INTO mail_attachments (mail_id, slot, kind, item_key, amount, bind) VALUES ($1, 1, 'gold', NULL, 5, NULL)", [id])).rejects.toThrow();
  });
});
