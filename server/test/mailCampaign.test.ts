// 10단계 14절 E: 운영 우편 캠페인(관리자 MC1~MC6, 접속 시 배달, 상한, 대상 조건, 취소·회수, 감사)
import { randomUUID } from 'node:crypto';
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import * as cache from '../src/domains/mail/campaignCache';
import { matchesTarget, type Facts } from '../src/domains/mail/campaignDelivery';
import { runAuctionTick } from '../src/domains/auction/auctionTicker';
import { integrityJob } from '../src/ops/jobs/integrity';
import { campaignRevokeJob, campaignSweepJob } from '../src/ops/jobs/sweepJobs';
import { expectLedgerConsistent, newHero, post, seedGold, seedLevel, type Hero } from './economyHelpers';
import { advance, resetClock, secondChar } from './auctionHelpers';
import { activeCampaign, awaitCampaignDeliveries, campaignBody, campaignMails, campaignRow, createCampaign, DAY, expireWindow, deliverTo, presenceBeat, summary, twoOwners } from './campaignHelpers';
import { adminApp, adminGet, adminPost, auditRows, makeAdmin, rid } from './opsHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { accountIdOf, expectTicketLedgerConsistent, ticketTotal } from './sweepHelpers';

const ON = { SWEEP_ENABLED: 'true', CAMPAIGN_DELIVERY_ENABLED: 'true', CAMPAIGN_MAX_GOLD_TOTAL: '100000000' };
let app: Express;
let admin: Express;

beforeEach(async () => {
  await resetDb();
  resetClock();
  cache.invalidateCampaignCache();
  app = buildApp(ON);
  admin = adminApp();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

const claim = (h: Hero, mailId: string, rid2?: string) => post(app, h, `/mail/${mailId}/claim`, {}, rid2);
const count = async (sql: string, params: unknown[] = []): Promise<number> => Number(((await getPool().query<{ n: string }>(sql, params)).rows[0] as { n: string }).n);
const jobCtx = { opts: {}, shouldStop: () => false };

describe('E1 MC1 작성 검증', () => {
  it('정상 작성은 pending이고 첨부가 서버 기준으로 정리된다(귀속은 키의 하한)', async () => {
    const a = await makeAdmin('operator');
    const res = await adminPost(admin, a, '/admin/mail-campaigns', campaignBody());
    expect(res.status).toBe(201);
    const c = res.body.data.campaign;
    expect(c).toMatchObject({ status: 'pending', title: '정기 점검 보상', category: 'maintenance', delivery_unit: 'account', cap_count: 100, issued_count: 0, needs_approval_by: 'owner other than author' });
    expect(c.attachments.map((x: { kind: string }) => x.kind)).toEqual(['gold', 'item', 'sweep_ticket']);
    expect(c.attachments[2]).toMatchObject({ item_key: 'ticket_sweep_event', count: 1, valid_days_after_claim: 14 });
    expect(c.created_by).toBe(a.loginId);
    expect((await campaignRow(c.id)).memo).toBe('테스트 지급');
  });

  it('거절: 첨부 6개, 중복 종류, 클리어권 키를 item으로, 없는 아이템, 과거·긴 기간, 골드 총액, 없는 계정, 빈 target', async () => {
    const a = await makeAdmin('operator');
    const bad = async (over: Record<string, unknown>, status: number, code?: string, field?: string) => {
      const res = await adminPost(admin, a, '/admin/mail-campaigns', campaignBody(over));
      expect(res.status).toBe(status);
      if (code) expect(res.body.errors.code).toBe(code);
      if (field) expect(res.body.errors.field).toBe(field);
    };
    const g = { kind: 'gold', amount: 100 };
    await bad({ attachments: [g, g, g, g, g, g] }, 400, 'VALIDATION');
    await bad({ attachments: [g, { kind: 'gold', amount: 200 }] }, 422, 'CAMPAIGN_LIMIT', 'attachments');
    await bad({ attachments: [{ kind: 'sweep_ticket', count: 1 }, { kind: 'sweep_ticket', count: 2 }] }, 422, 'CAMPAIGN_LIMIT');
    await bad({ attachments: [{ kind: 'item', item_key: 'ticket_sweep_event', count: 1 }] }, 422, 'ITEM_NOT_FOUND');
    await bad({ attachments: [{ kind: 'item', item_key: 'ticket_sweep', count: 1 }] }, 422, 'ITEM_NOT_FOUND');
    await bad({ attachments: [{ kind: 'item', item_key: 'no_such_item', count: 1 }] }, 422, 'ITEM_NOT_FOUND');
    await bad({ attachments: [{ kind: 'item', item_key: 'potion_hp', count: 1 }, { kind: 'item', item_key: 'potion_hp', count: 2 }] }, 422, 'CAMPAIGN_LIMIT');
    await bad({ attachments: [{ kind: 'sweep_ticket', count: 11 }] }, 422, 'CAMPAIGN_LIMIT', 'attachments.sweep_ticket');
    await bad({ ends_at: new Date(Date.now() - 1000).toISOString(), starts_at: new Date(Date.now() - DAY).toISOString() }, 422, 'CAMPAIGN_LIMIT', 'ends_at');
    await bad({ ends_at: new Date(Date.now() + 61 * DAY).toISOString() }, 422, 'CAMPAIGN_LIMIT', 'ends_at');
    await bad({ ends_at: new Date(Date.now() - 2 * DAY).toISOString() }, 422, 'CAMPAIGN_LIMIT');
    await bad({ attachments: [{ kind: 'gold', amount: 2_000_000 }] }, 422, 'CAMPAIGN_LIMIT', 'attachments.gold'); // 1통 상한
    await bad({ attachments: [{ kind: 'gold', amount: 500_000 }], cap_count: 1000 }, 422, 'CAMPAIGN_LIMIT', 'attachments.gold'); // 총액 상한(1억)
    await bad({ cap_count: 300_000 }, 422, 'CAMPAIGN_LIMIT', 'cap_count');
    await bad({ target: { account_ids: [randomUUID()] } }, 422, 'CAMPAIGN_TARGET_INVALID');
    await bad({ target: {} }, 400, 'VALIDATION');
    await bad({ target: { all: true, min_account_level: 3 } }, 400, 'VALIDATION');
    await bad({ target: { classes: ['warrior'] } }, 422, 'CAMPAIGN_TARGET_INVALID'); // 계정 단위에서는 직업 조건 불가
    await bad({ body: 'a\u0007b' }, 400, 'VALIDATION');
    await bad({ memo: '' }, 400, 'VALIDATION');
    await bad({ cap_count: 0 }, 400, 'VALIDATION');
    expect(await count('SELECT count(*) AS n FROM mail_campaigns')).toBe(0);
  });

  it('총 지급 골드 상한(CAMPAIGN_MAX_GOLD_TOTAL)이 비어 있으면 골드 첨부 캠페인을 만들 수 없다', async () => {
    app = buildApp({ SWEEP_ENABLED: 'true', CAMPAIGN_DELIVERY_ENABLED: 'true' });
    admin = adminApp();
    const a = await makeAdmin('operator');
    const res = await adminPost(admin, a, '/admin/mail-campaigns', campaignBody());
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({ code: 'CAMPAIGN_LIMIT', field: 'attachments.gold' });
    const noGold = await adminPost(admin, a, '/admin/mail-campaigns', campaignBody({ attachments: [{ kind: 'sweep_ticket', count: 1 }] }));
    expect(noGold.status).toBe(201);
  });

  it('viewer는 만들 수 없다(403)', async () => {
    const v = await makeAdmin('viewer');
    expect((await adminPost(admin, v, '/admin/mail-campaigns', campaignBody())).status).toBe(403);
  });
});

describe('E2 승인(2인 확인)', () => {
  it('작성자 본인은 CAMPAIGN_SELF_APPROVAL, owner가 아닌 역할은 403, 다른 owner는 성공, 재승인은 CAMPAIGN_STATE', async () => {
    const [o1, o2] = await twoOwners();
    const op = await makeAdmin('operator');
    const id = await createCampaign(admin, o1);
    const self = await adminPost(admin, o1, `/admin/mail-campaigns/${id}/approve`);
    expect(self.status).toBe(403);
    expect(self.body.errors.code).toBe('CAMPAIGN_SELF_APPROVAL');
    expect((await adminPost(admin, op, `/admin/mail-campaigns/${id}/approve`)).body.errors.code).toBe('FORBIDDEN_ROLE');
    expect((await campaignRow(id)).status).toBe('pending');
    const ok = await adminPost(admin, o2, `/admin/mail-campaigns/${id}/approve`);
    expect(ok.status).toBe(200);
    expect(ok.body.data.campaign).toMatchObject({ status: 'active', approved_by: o2.loginId });
    expect((await adminPost(admin, o2, `/admin/mail-campaigns/${id}/approve`)).body.errors.code).toBe('CAMPAIGN_STATE');
  });

  it('DB도 같은 결과: 작성자가 승인자인 UPDATE는 CHECK가 거절한다', async () => {
    const [o1] = await twoOwners();
    const id = await createCampaign(admin, o1);
    await expect(getPool().query("UPDATE mail_campaigns SET status = 'active', approved_by = created_by, approved_at = now() WHERE uuid = $1", [id])).rejects.toThrow(/mail_campaigns_two_person_chk/);
  });

  it('기능 플래그가 꺼져 있으면 승인은 503, 기간이 지난 대기 캠페인은 CAMPAIGN_WINDOW_PASSED', async () => {
    const [o1, o2] = await twoOwners();
    const id = await createCampaign(admin, o1);
    app = buildApp({ SWEEP_ENABLED: 'true' });
    admin = adminApp();
    const off = await adminPost(admin, o2, `/admin/mail-campaigns/${id}/approve`);
    expect(off.status).toBe(503);
    expect(off.body.errors.code).toBe('FEATURE_DISABLED');
    app = buildApp(ON);
    admin = adminApp();
    await expireWindow(id);
    const late = await adminPost(admin, o2, `/admin/mail-campaigns/${id}/approve`);
    expect(late.status).toBe(422);
    expect(late.body.errors.code).toBe('CAMPAIGN_WINDOW_PASSED');
  });
});

describe('E3 내용 불변', () => {
  it('제목 UPDATE, 삭제, 첨부 UPDATE·DELETE는 DB가 막는다', async () => {
    const [o1] = await twoOwners();
    const id = await createCampaign(admin, o1);
    await expect(getPool().query("UPDATE mail_campaigns SET title = 'x' WHERE uuid = $1", [id])).rejects.toThrow(/immutable/);
    await expect(getPool().query('UPDATE mail_campaigns SET cap_count = 9999999 WHERE uuid = $1', [id])).rejects.toThrow(/immutable/);
    await expect(getPool().query('DELETE FROM mail_campaigns WHERE uuid = $1', [id])).rejects.toThrow(/never deleted/);
    await expect(getPool().query('UPDATE mail_campaign_attachments SET amount = 1')).rejects.toThrow();
    await expect(getPool().query('DELETE FROM mail_campaign_attachments')).rejects.toThrow();
    // 상태 열은 바뀐다
    await getPool().query("UPDATE mail_campaigns SET revoke_requested = false WHERE uuid = $1", [id]);
  });
});

describe('E4~E5 배달', () => {
  it('승인 전에는 우편이 없고, 승인 후 프레즌스 진입에서 한 통 생성(제목·본문·첨부 복사, admin_grant 원장, 배달 기록), 재접속·다른 캐릭터는 추가 없음', async () => {
    const [o1, o2] = await twoOwners();
    const a = await newHero(app);
    const b = await secondChar(app, a);
    const id = await createCampaign(admin, o1);
    expect((await presenceBeat(app, a)).status).toBe(200);
    await awaitCampaignDeliveries();
    expect(await campaignMails(id)).toHaveLength(0);

    expect((await adminPost(admin, o2, `/admin/mail-campaigns/${id}/approve`)).status).toBe(200);
    cache.invalidateCampaignCache();
    // 새 접속(A)이 먼저: 받는 캐릭터는 처음 접속한 캐릭터
    await getPool().query('DELETE FROM online_sessions');
    expect((await presenceBeat(app, a)).status).toBe(200);
    await awaitCampaignDeliveries();
    const mails = await campaignMails(id);
    expect(mails).toHaveLength(1);
    expect(mails[0]?.character_id).toBe(String(a.dbId));
    expect(mails[0]?.attach_n).toBe(3);
    const m = (await getPool().query("SELECT title, body, kind, system_code, gold, item_key, expires_at, created_at FROM mails WHERE uuid = $1", [mails[0]?.uuid])).rows[0];
    expect(m).toMatchObject({ title: '정기 점검 보상', body: '점검에 협조해 주셔서 감사합니다.\n작은 선물을 드립니다.', kind: 'system', system_code: 'maintenance', gold: '0', item_key: null });
    expect(m.expires_at.getTime() - m.created_at.getTime()).toBe(14 * DAY);
    const atts = await getPool().query("SELECT kind, item_key, amount, bind FROM mail_attachments ORDER BY slot");
    expect(atts.rows).toEqual([
      { kind: 'gold', item_key: null, amount: '5000', bind: null },
      { kind: 'item', item_key: 'potion_hp', amount: '10', bind: 'none' },
      { kind: 'sweep_ticket', item_key: 'ticket_sweep_event', amount: '1', bind: null },
    ]);
    const led = await getPool().query("SELECT delta, location, reason FROM item_ledger WHERE ref = $1", [mails[0]?.uuid]);
    expect(led.rows).toEqual([{ delta: 10, location: 'mail', reason: 'admin_grant' }]);
    const del = await getPool().query('SELECT delivery_key, character_id FROM mail_campaign_deliveries');
    expect(del.rows).toEqual([{ delivery_key: String(await accountIdOf(a)), character_id: String(a.dbId) }]);
    expect((await campaignRow(id)).issued_count).toBe(1);

    // 같은 계정이 다시 접속·폴링해도, 같은 계정의 다른 캐릭터도 추가 우편이 없다
    await getPool().query('DELETE FROM online_sessions');
    expect((await presenceBeat(app, b)).status).toBe(200);
    await awaitCampaignDeliveries();
    expect((await summary(app, a)).status).toBe(200);
    await awaitCampaignDeliveries();
    expect(await campaignMails(id)).toHaveLength(1);
    expect((await campaignRow(id)).issued_count).toBe(1);
  });

  it('M4 우편 요약이 두 번째 트리거다: 접속 중이던 사람이 승인 직후 폴링에서 받는다(알림은 제목 포함)', async () => {
    const [o1, o2] = await twoOwners();
    const a = await newHero(app);
    const id = await createCampaign(admin, o1);
    expect((await summary(app, a)).status).toBe(200);
    await awaitCampaignDeliveries();
    expect(await campaignMails(id)).toHaveLength(0);
    await adminPost(admin, o2, `/admin/mail-campaigns/${id}/approve`);
    cache.invalidateCampaignCache();
    const first = await summary(app, a);
    expect(first.body.data.unclaimed).toBe(0); // 이 응답은 배달 전 상태
    await awaitCampaignDeliveries();
    const second = await summary(app, a);
    expect(second.body.data.unclaimed).toBe(1);
    await awaitCampaignDeliveries();
    const withSince = await (await import('supertest')).default(app).get(`/characters/${a.id}/mail/summary?since=${encodeURIComponent(new Date(Date.now() - DAY).toISOString())}`).set((await import('./helpers')).auth(a.s));
    expect(withSince.body.data.new[0]).toMatchObject({ kind: 'system', system_code: 'maintenance', title: '정기 점검 보상' });
  });

  it('character 단위 캠페인은 캐릭터마다 한 통', async () => {
    const [o1, o2] = await twoOwners();
    const a = await newHero(app);
    const b = await secondChar(app, a);
    const id = await activeCampaign(admin, o1, o2, { delivery_unit: 'character', attachments: [{ kind: 'gold', amount: 5000 }, { kind: 'item', item_key: 'potion_hp', count: 10 }] });
    expect(await deliverTo(a)).toBe(1);
    expect(await deliverTo(b)).toBe(1);
    expect(await deliverTo(a)).toBe(0);
    expect(await campaignMails(id)).toHaveLength(2);
  });
});

describe('E6 총 지급 상한', () => {
  it('cap_count=3에 5개 계정이 동시에 접속하면 정확히 3통, 나머지는 롤백되어 찌꺼기가 없고, 작업 뒤 ended', async () => {
    const [o1, o2] = await twoOwners();
    const heroes: Hero[] = [];
    for (let i = 0; i < 5; i++) heroes.push(await newHero(app));
    const id = await activeCampaign(admin, o1, o2, { cap_count: 3 });
    const results = await Promise.all(heroes.map((h) => deliverTo(h)));
    expect(results.reduce((a, b) => a + b, 0)).toBe(3);
    expect(await campaignMails(id)).toHaveLength(3);
    expect((await campaignRow(id)).issued_count).toBe(3);
    expect(await count('SELECT count(*) AS n FROM mail_campaign_deliveries')).toBe(3);
    expect(await count('SELECT count(*) AS n FROM mails WHERE campaign_id IS NOT NULL')).toBe(3);
    expect(await count('SELECT count(*) AS n FROM mail_attachments')).toBe(9);
    expect(await count("SELECT count(*) AS n FROM item_ledger WHERE reason = 'admin_grant'")).toBe(3);
    await campaignSweepJob();
    expect((await campaignRow(id)).status).toBe('ended');
    // 닫힌 뒤에는 더 만들지 않는다
    expect((await Promise.all(heroes.map((h) => deliverTo(h)))).reduce((a, b) => a + b, 0)).toBe(0);
    await expect(getPool().query('UPDATE mail_campaigns SET issued_count = 4 WHERE uuid = $1', [id])).rejects.toThrow(/mail_campaigns_cap_chk/);
  });
});

describe('E7 대상 조건', () => {
  const facts = (over: Partial<Facts> = {}): Facts => ({
    accountUuid: randomUUID(),
    createdAt: new Date('2026-09-01T00:00:00Z'),
    lastLoginAt: new Date('2026-10-06T00:00:00Z'), // just logged in (delivery runs after a login)
    prevLoginAt: new Date('2026-08-01T00:00:00Z'), // the visit before: two months away
    accountMaxLevel: 20,
    charLevel: 12,
    charClass: 'warrior',
    charUuid: randomUUID(),
    ...over,
  });
  const m = (target: Record<string, unknown>, f = facts()) => matchesTarget({ target } as never, f);

  it('조건별 일치·불일치(AND)', () => {
    expect(m({ all: true })).toBe(true);
    expect(m({ min_account_level: 20 })).toBe(true);
    expect(m({ min_account_level: 21 })).toBe(false);
    expect(m({ max_account_level: 19 })).toBe(false);
    expect(m({ min_level: 12, max_level: 12 })).toBe(true);
    expect(m({ max_level: 11 })).toBe(false);
    expect(m({ classes: ['warrior'] })).toBe(true);
    expect(m({ classes: ['mage'] })).toBe(false);
    expect(m({ account_created_from: '2026-08-01T00:00:00Z', account_created_to: '2026-10-01T00:00:00Z' })).toBe(true);
    expect(m({ account_created_from: '2026-09-02T00:00:00Z' })).toBe(false);
    expect(m({ last_login_before: '2026-09-01T00:00:00Z' })).toBe(true);
    expect(m({ last_login_before: '2026-07-01T00:00:00Z' })).toBe(false);
    expect(m({ last_login_before: '2026-09-01T00:00:00Z' }, facts({ prevLoginAt: null }))).toBe(false); // first login ever
    // A returning player: the fresh login does not hide the long break
    expect(m({ last_login_before: '2026-09-01T00:00:00Z' }, facts({ lastLoginAt: new Date() }))).toBe(true);
    const f = facts();
    expect(m({ account_ids: [f.accountUuid] }, f)).toBe(true);
    expect(m({ account_ids: [randomUUID()] }, f)).toBe(false);
    expect(m({ min_account_level: 20, classes: ['mage'] })).toBe(false); // AND
  });

  it('배달에 연결: 특정 계정 목록과 레벨 조건이 맞는 계정만 받는다', async () => {
    const [o1, o2] = await twoOwners();
    const a = await newHero(app);
    const b = await newHero(app);
    const accUuid = ((await getPool().query('SELECT uuid FROM accounts WHERE id = $1', [await accountIdOf(a)])).rows[0] as { uuid: string }).uuid;
    const byId = await activeCampaign(admin, o1, o2, { target: { account_ids: [accUuid] } });
    expect(await deliverTo(a)).toBe(1);
    expect(await deliverTo(b)).toBe(0);
    expect(await campaignMails(byId)).toHaveLength(1);
    // 계정 최고 레벨 조건(삭제한 캐릭터 포함)
    const c = await newHero(app);
    await seedLevel(c, 30);
    const byLevel = await activeCampaign(admin, o1, o2, { target: { min_account_level: 30 } });
    expect(await deliverTo(c)).toBe(1);
    expect(await deliverTo(b)).toBe(0);
    expect(await campaignMails(byLevel)).toHaveLength(1);
  });

  it('character 단위의 직업 조건', async () => {
    const [o1, o2] = await twoOwners();
    const w = await newHero(app, 'warrior');
    const mg = await newHero(app, 'mage');
    const id = await activeCampaign(admin, o1, o2, { delivery_unit: 'character', attachments: [{ kind: 'gold', amount: 5000 }, { kind: 'item', item_key: 'potion_hp', count: 10 }], target: { classes: ['mage'] } });
    expect(await deliverTo(w)).toBe(0);
    expect(await deliverTo(mg)).toBe(1);
    expect(await campaignMails(id)).toHaveLength(1);
  });
});

describe('E8 기간과 기능 플래그', () => {
  it('시작 전과 종료 후 접속은 배달이 없고, 플래그가 꺼져 있으면 승인된 캠페인도 배달하지 않는다', async () => {
    const [o1, o2] = await twoOwners();
    const h = await newHero(app);
    const future = await activeCampaign(admin, o1, o2, { starts_at: new Date(Date.now() + 2 * DAY).toISOString(), ends_at: new Date(Date.now() + 5 * DAY).toISOString() });
    expect(await deliverTo(h)).toBe(0);
    advance(3 * DAY);
    expect(await deliverTo(h)).toBe(1); // 이제 기간 안
    expect(await campaignMails(future)).toHaveLength(1);
    advance(5 * DAY);
    const h2 = await newHero(app);
    expect(await deliverTo(h2)).toBe(0); // 종료 후
    // 플래그 꺼짐
    resetClock();
    const open = await activeCampaign(admin, o1, o2);
    app = buildApp({ SWEEP_ENABLED: 'true' });
    cache.invalidateCampaignCache();
    const h3 = await newHero(app);
    expect(await deliverTo(h3)).toBe(0);
    expect(await campaignMails(open)).toHaveLength(0);
  });
});

describe('E9 취소와 미수령 회수', () => {
  it('취소 후 새 배달 없음, 회수 요청이면 미수령 우편만 기한을 앞당겨 폐기 틱이 닫고(원장·소각 기록) 받은 우편은 그대로', async () => {
    const [o1, o2] = await twoOwners();
    const hs: Hero[] = [await newHero(app), await newHero(app), await newHero(app), await newHero(app)];
    const id = await activeCampaign(admin, o1, o2);
    for (const h of hs.slice(0, 3)) expect(await deliverTo(h)).toBe(1);
    const mails = await campaignMails(id);
    // 한 명은 이미 수령
    const claimerIdx = hs.findIndex((h) => String(h.dbId) === mails[0]?.character_id);
    expect((await claim(hs[claimerIdx] as Hero, mails[0]?.uuid as string)).status).toBe(200);

    advance(1000);
    const cancel = await adminPost(admin, o2, `/admin/mail-campaigns/${id}/cancel`, { reason: '오지급 방지', revoke_unclaimed: true });
    expect(cancel.status).toBe(200);
    expect(cancel.body.data.campaign.status).toBe('cancelled');
    expect(cancel.body.data.revoke).toEqual({ requested: true, remaining: 2 });
    expect(await deliverTo(hs[3] as Hero)).toBe(0); // 취소 후 새 배달 없음
    expect((await campaignRow(id)).cancel_reason).toBe('오지급 방지');

    advance(1000);
    const rv = await campaignRevokeJob(jobCtx);
    expect(rv.rows).toBe(2);
    const row = await campaignRow(id);
    expect(row.revoke_done_at).not.toBeNull();
    expect(row.revoked_count).toBe(2);
    const tick = await runAuctionTick();
    expect(tick.mailsExpired).toBe(2);
    const after = await campaignMails(id);
    expect(after.filter((x) => x.claimed_at).length).toBe(1);
    expect(after.filter((x) => x.expired_at).length).toBe(2);
    expect(await count("SELECT count(*) AS n FROM item_ledger WHERE reason = 'mail_expire' AND delta = -10")).toBe(2);
    expect(Number(((await getPool().query("SELECT coalesce(sum(amount), 0) AS n FROM auction_sinks WHERE kind = 'mail_expire'")).rows[0] as { n: string }).n)).toBe(10_000);
    // 정합성: 골드·아이템·클리어권 보존식
    const integ = await integrityJob({ opts: { full: true }, shouldStop: () => false });
    const checks = (integ.detail as { checks: Record<string, { count: number; samples: unknown[] }> }).checks;
    expect(checks.I3).toEqual({ count: 0, samples: [] });
    // MC3 현황
    const show = await adminGet(admin, o1, `/admin/mail-campaigns/${id}`);
    expect(show.body.data.stats).toMatchObject({ issued: 3, claimed: 1, unclaimed_open: 0, expired: 0, revoked: 2, gold_promised: 15_000, gold_claimed: 5000, tickets_promised: 3, tickets_claimed: 1 });
    expect(show.body.data.revoke).toEqual({ requested: true, remaining: 0 });
  });

  it('회수와 수령이 겹치면 수령 xor 만료(둘 다 성립하지 않는다)', async () => {
    const [o1, o2] = await twoOwners();
    const h = await newHero(app);
    const id = await activeCampaign(admin, o1, o2);
    await deliverTo(h);
    const mail = (await campaignMails(id))[0] as { uuid: string };
    advance(1000);
    await adminPost(admin, o2, `/admin/mail-campaigns/${id}/cancel`, { reason: '경합', revoke_unclaimed: true });
    advance(1000);
    const [c, r] = await Promise.all([claim(h, mail.uuid), campaignRevokeJob(jobCtx)]);
    await runAuctionTick();
    const row = (await getPool().query('SELECT claimed_at, expired_at FROM mails WHERE uuid = $1', [mail.uuid])).rows[0];
    expect(r.rows).toBeLessThanOrEqual(1);
    if (c.status === 200) {
      expect(row.claimed_at).not.toBeNull();
      expect(row.expired_at).toBeNull();
    } else {
      expect([410, 409]).toContain(c.status);
      expect(row.claimed_at).toBeNull();
      expect(row.expired_at).not.toBeNull();
    }
  });
});

describe('E10 취소 권한과 시스템 취소', () => {
  it('대기 중 취소는 작성자도 가능, 진행 중 취소는 owner만, 승인 전에 기간이 끝나면 시스템 취소', async () => {
    const [o1, o2] = await twoOwners();
    const op = await makeAdmin('operator');
    const other = await makeAdmin('operator');
    const pending = await createCampaign(admin, op);
    expect((await adminPost(admin, other, `/admin/mail-campaigns/${pending}/cancel`, { reason: '남', revoke_unclaimed: false })).status).toBe(403);
    expect((await adminPost(admin, op, `/admin/mail-campaigns/${pending}/cancel`, { reason: '작성 실수', revoke_unclaimed: false })).status).toBe(200);
    expect((await campaignRow(pending)).status).toBe('cancelled');
    expect((await adminPost(admin, op, `/admin/mail-campaigns/${pending}/cancel`, { reason: '또', revoke_unclaimed: false })).body.errors.code).toBe('CAMPAIGN_STATE');

    const active = await activeCampaign(admin, o1, o2);
    expect((await adminPost(admin, op, `/admin/mail-campaigns/${active}/cancel`, { reason: '권한없음', revoke_unclaimed: false })).status).toBe(403);
    expect((await adminPost(admin, o1, `/admin/mail-campaigns/${active}/cancel`, { reason: '중단', revoke_unclaimed: false })).status).toBe(200);

    const stale = await createCampaign(admin, o1);
    advance(8 * DAY);
    await campaignSweepJob();
    const row = await campaignRow(stale);
    expect(row).toMatchObject({ status: 'cancelled', cancel_reason: 'unapproved_expired', cancelled_by: null });
  });

  it('ended 캠페인은 회수 요청일 때만 취소 계열 요청을 받는다', async () => {
    const [o1, o2] = await twoOwners();
    const h = await newHero(app);
    const id = await activeCampaign(admin, o1, o2, { cap_count: 1 });
    expect(await deliverTo(h)).toBe(1);
    await campaignSweepJob();
    expect((await campaignRow(id)).status).toBe('ended');
    expect((await adminPost(admin, o2, `/admin/mail-campaigns/${id}/cancel`, { reason: '끝남', revoke_unclaimed: false })).body.errors.code).toBe('CAMPAIGN_STATE');
    const rv = await adminPost(admin, o2, `/admin/mail-campaigns/${id}/cancel`, { reason: '회수', revoke_unclaimed: true });
    expect(rv.status).toBe(200);
    expect(rv.body.data.campaign.status).toBe('ended');
    expect(rv.body.data.revoke.requested).toBe(true);
  });
});

describe('E11 감사와 멱등성', () => {
  it('작성·승인·취소·조회가 admin_audit_log에 남고 memo가 캠페인에 있다, 같은 request_id 재전송은 같은 응답', async () => {
    const [o1, o2] = await twoOwners();
    const body = campaignBody();
    const reqId = rid();
    const a = await adminPost(admin, o1, '/admin/mail-campaigns', body, reqId);
    const b = await adminPost(admin, o1, '/admin/mail-campaigns', body, reqId);
    expect(a.status).toBe(201);
    expect(b.status).toBe(201);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body.data).toEqual(a.body.data);
    expect(await count('SELECT count(*) AS n FROM mail_campaigns')).toBe(1);
    const mismatch = await adminPost(admin, o1, '/admin/mail-campaigns', campaignBody({ title: '다른 제목' }), reqId);
    expect(mismatch.status).toBe(422);
    expect(mismatch.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
    const id = a.body.data.campaign.id as string;
    await adminPost(admin, o2, `/admin/mail-campaigns/${id}/approve`);
    await adminGet(admin, o1, '/admin/mail-campaigns');
    await adminGet(admin, o1, `/admin/mail-campaigns/${id}`);
    await adminGet(admin, o1, `/admin/mail-campaigns/${id}/deliveries`);
    await adminPost(admin, o2, `/admin/mail-campaigns/${id}/cancel`, { reason: '감사 확인', revoke_unclaimed: false });
    const actions = (await auditRows("target_type = 'campaign' AND result = 'ok'")).map((r) => r.action);
    expect(actions).toEqual(expect.arrayContaining(['campaign.create', 'campaign.approve', 'campaign.cancel', 'campaign.list', 'campaign.view', 'campaign.deliveries']));
    expect((await campaignRow(id)).memo).toBe('테스트 지급');
  });
});

describe('E12 MC2 목록, MC3 현황, MC6 배달 목록', () => {
  it('목록 상태 필터와 커서, 현황 수치가 DB 집계와 같고, 배달 목록에 수령 상태가 나온다', async () => {
    const [o1, o2] = await twoOwners();
    const hs = [await newHero(app), await newHero(app), await newHero(app)];
    const id = await activeCampaign(admin, o1, o2);
    await createCampaign(admin, o1);
    for (const h of hs) await deliverTo(h);
    const mails = await campaignMails(id);
    const first = hs.find((h) => String(h.dbId) === mails[0]?.character_id) as Hero;
    expect((await claim(first, mails[0]?.uuid as string)).status).toBe(200);

    const all = await adminGet(admin, o1, '/admin/mail-campaigns');
    expect(all.body.data.items).toHaveLength(2);
    expect((await adminGet(admin, o1, '/admin/mail-campaigns?status=active')).body.data.items).toHaveLength(1);
    const page1 = await adminGet(admin, o1, '/admin/mail-campaigns?limit=1');
    expect(page1.body.data.items).toHaveLength(1);
    expect(page1.body.data.next_before).not.toBeNull();
    const page2 = await adminGet(admin, o1, `/admin/mail-campaigns?limit=1&before=${page1.body.data.next_before}`);
    expect(page2.body.data.items).toHaveLength(1);
    expect(page2.body.data.items[0].id).not.toBe(page1.body.data.items[0].id);

    const show = await adminGet(admin, o1, `/admin/mail-campaigns/${id}`);
    expect(show.body.data.stats).toEqual({ issued: 3, claimed: 1, unclaimed_open: 2, expired: 0, revoked: 0, gold_promised: 15_000, gold_claimed: 5000, tickets_promised: 3, tickets_claimed: 1 });
    expect(show.body.data.campaign.issued_count).toBe(3);

    const dl = await adminGet(admin, o1, `/admin/mail-campaigns/${id}/deliveries`);
    expect(dl.body.data.items).toHaveLength(3);
    expect(dl.body.data.items.filter((x: { state: string }) => x.state === 'claimed')).toHaveLength(1);
    expect((await adminGet(admin, o1, `/admin/mail-campaigns/${id}/deliveries?state=open`)).body.data.items).toHaveLength(2);
    expect((await adminGet(admin, o1, `/admin/mail-campaigns/${randomUUID()}`)).status).toBe(404);
  });
});

describe('E13 배달 실패 격리', () => {
  it('배달 함수가 오류를 내도 프레즌스와 우편 요약 응답은 정상이다(로그만)', async () => {
    const [o1, o2] = await twoOwners();
    const h = await newHero(app);
    await activeCampaign(admin, o1, o2);
    const spy = jest.spyOn(cache, 'activeCampaigns').mockRejectedValue(new Error('boom'));
    try {
      expect((await presenceBeat(app, h)).status).toBe(200);
      expect((await summary(app, h)).status).toBe(200);
      await awaitCampaignDeliveries();
      expect(spy).toHaveBeenCalled();
      expect(await count('SELECT count(*) AS n FROM mails WHERE campaign_id IS NOT NULL')).toBe(0);
    } finally {
      spy.mockRestore();
    }
    // 다음 폴링에서 다시 시도해 받는다
    cache.invalidateCampaignCache();
    expect((await summary(app, h)).status).toBe(200);
    await awaitCampaignDeliveries();
    expect(await count('SELECT count(*) AS n FROM mails WHERE campaign_id IS NOT NULL')).toBe(1);
  });
});

describe('락·교착', () => {
  it('계정 행을 잠그는 경로(소탕, 구매, 주간 수령, 우편 수령, 만료 작업)를 섞은 동시 부하에서 교착 없이 끝난다', async () => {
    const [o1, o2] = await twoOwners();
    const a = await newHero(app);
    const b = await secondChar(app, a);
    await seedLevel(a, 10);
    await seedLevel(b, 10);
    await seedGold(a, 1_000_000);
    await seedGold(b, 1_000_000);
    const id = await activeCampaign(admin, o1, o2);
    await deliverTo(a);
    const mail = (await campaignMails(id))[0] as { uuid: string; character_id: string };
    const owner = String(a.dbId) === mail.character_id ? a : b;
    const expireJob = async () => (await import('../src/ops/jobs/sweepJobs')).sweepTicketExpireJob(jobCtx);
    const results = await Promise.all([
      post(app, a, '/sweep/tickets/buy', { count: 2 }),
      post(app, b, '/sweep/tickets/buy', { count: 2 }),
      post(app, a, '/sweep/weekly/claim', {}),
      post(app, b, '/sweep/weekly/claim', {}),
      claim(owner, mail.uuid),
      post(app, owner, '/mail/claim-all', {}),
      expireJob(),
    ]);
    for (const r of results.slice(0, 6) as { status: number }[]) expect([200, 409, 422]).toContain(r.status);
    // 구매 4장 + 이벤트권 1장(우편은 한 번만 받힌다) = 지갑 5장, 로트별 원장 보존
    expect(await ticketTotal(a)).toBe(5);
    await expectTicketLedgerConsistent(a);
    await expectLedgerConsistent(a);
    await expectLedgerConsistent(b);
    expect(await count("SELECT count(*) AS n FROM sweep_ticket_ledger WHERE reason = 'campaign_claim'")).toBe(1);
  });
});
