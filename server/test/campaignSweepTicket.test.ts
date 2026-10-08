// 캠페인 클리어권 첨부는 계정 단위에서만 허용한다(캐릭터 단위는 계정 지갑에 캐릭터 수만큼 쌓인다)
import type { Express } from 'express';
import { getPool } from '../src/db/pool';
import * as cache from '../src/domains/mail/campaignCache';
import { campaignBody, createCampaign, twoOwners } from './campaignHelpers';
import { adminApp, adminPost, makeAdmin } from './opsHelpers';
import { resetClock } from './auctionHelpers';
import { buildApp, resetDb, shutdown } from './helpers';

const ON = { SWEEP_ENABLED: 'true', CAMPAIGN_DELIVERY_ENABLED: 'true', CAMPAIGN_MAX_GOLD_TOTAL: '100000000' };
let admin: Express;

beforeEach(async () => {
  await resetDb();
  resetClock();
  cache.invalidateCampaignCache();
  buildApp(ON);
  admin = adminApp();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

const ticketOnly = [{ kind: 'sweep_ticket', count: 1 }];

describe('캠페인 클리어권 배달 단위', () => {
  it('character 단위 + 클리어권 첨부는 422로 거절하고 캠페인을 만들지 않는다', async () => {
    const a = await makeAdmin('operator');
    const res = await adminPost(admin, a, '/admin/mail-campaigns', campaignBody({ delivery_unit: 'character', attachments: ticketOnly }));
    expect(res.status).toBe(422);
    expect(res.body.errors).toMatchObject({ code: 'CAMPAIGN_LIMIT', field: 'attachments.sweep_ticket' });
    const n = await getPool().query('SELECT count(*) AS n FROM mail_campaigns');
    expect(n.rows[0]).toEqual({ n: '0' });
    // 클리어권이 없는 character 단위는 그대로 만들 수 있다
    const ok = await adminPost(admin, a, '/admin/mail-campaigns', campaignBody({ delivery_unit: 'character', attachments: [{ kind: 'gold', amount: 100 }] }));
    expect(ok.status).toBe(201);
  });

  it('account 단위 클리어권 첨부는 허용하고, 이미 있던 character+클리어권 대기 캠페인은 승인 때 막는다', async () => {
    const [a, b] = await twoOwners();
    const id = await createCampaign(admin, a, { delivery_unit: 'account', attachments: ticketOnly });
    const ok = await adminPost(admin, b, `/admin/mail-campaigns/${id}/approve`);
    expect(ok.status).toBe(200);

    // 기존 데이터(검증이 생기기 전에 만든 것)를 흉내 낸다: DB에서 단위만 바꾼 대기 캠페인
    const legacy = await createCampaign(admin, a, { delivery_unit: 'account', attachments: ticketOnly });
    await getPool().query('ALTER TABLE mail_campaigns DISABLE TRIGGER USER'); // 작성 후 내용 불변 트리거를 잠시 끈다
    await getPool().query("UPDATE mail_campaigns SET delivery_unit = 'character' WHERE uuid = $1", [legacy]);
    await getPool().query('ALTER TABLE mail_campaigns ENABLE TRIGGER USER');
    const bad = await adminPost(admin, b, `/admin/mail-campaigns/${legacy}/approve`);
    expect(bad.status).toBe(422);
    expect(bad.body.errors.code).toBe('CAMPAIGN_LIMIT');
    const st = await getPool().query<{ status: string }>('SELECT status FROM mail_campaigns WHERE uuid = $1', [legacy]);
    expect(st.rows[0]?.status).toBe('pending');
  });
});
