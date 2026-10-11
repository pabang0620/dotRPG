import { randomUUID } from 'node:crypto';
import { getPool } from '../src/db/pool';
import { runAuctionTick } from '../src/domains/auction/auctionTicker';
import type { Hero } from './economyHelpers';
import { countOf, expectLedgerConsistent, get, post } from './economyHelpers';
import {
  advance,
  bidReq,
  buyoutReq,
  claimAllReq,
  claimReq,
  expectConserved,
  flagKinds,
  goldNow,
  HOUR,
  listIron,
  listReq,
  mailList,
  MIN,
  mk,
  resetClock,
} from './auctionHelpers';
import { resetDb, shutdown } from './helpers';
import { buildApp } from './auctionHelpers';

const app = buildApp();
beforeEach(async () => {
  resetClock();
  await resetDb();
});
afterAll(async () => {
  resetClock();
  await shutdown();
});

const systemMails = async (h: Hero, n: number, gold: number): Promise<void> => {
  // 운영 지급 우편(7단계가 만든다). 골드가 캐릭터 밖에서 생기므로 보존식은 system 합을 더해 닫는다
  for (let i = 0; i < n; i++) {
    await getPool().query(
      `INSERT INTO mails (character_id, kind, system_code, gold, created_at, expires_at)
       VALUES ($1, 'system', 'compensation', $2, now(), now() + interval '30 days')`,
      [h.dbId, gold],
    );
  }
};

describe('S14 멱등성', () => {
  it('같은 request_id 재전송은 처음 응답 그대로, 다른 본문 재사용은 IDEMPOTENCY_MISMATCH', async () => {
    const seller = await mk(app);
    const buyer = await mk(app);
    const body = { item_key: 'eq_sword_10_u', count: 1, buyout: 1000, start_bid: 100, hours: 12 };
    const { seedItem } = await import('./economyHelpers');
    await seedItem(seller, 'eq_sword_10_u', 2);
    const r = randomUUID();
    const a = await listReq(app, seller, body, r);
    const b = await listReq(app, seller, body, r);
    expect(a.status).toBe(201);
    expect(b.status).toBe(201);
    expect(b.headers['idempotent-replay']).toBe('true');
    expect(b.body).toEqual(a.body);
    expect((await getPool().query('SELECT count(*) AS n FROM auction_listings')).rows[0]).toEqual({ n: '1' });
    expect(await goldNow(seller)).toBe(100_000 - 10);
    expect((await listReq(app, seller, { ...body, buyout: 1200 }, r)).body.errors.code).toBe('IDEMPOTENCY_MISMATCH');

    const id = a.body.data.listing.id as string;
    const r2 = randomUUID();
    const x = await bidReq(app, buyer, id, 100, r2);
    const y = await bidReq(app, buyer, id, 100, r2);
    expect(y.headers['idempotent-replay']).toBe('true');
    expect(y.body).toEqual(x.body);
    expect(await goldNow(buyer)).toBe(100_000 - 100);
    expect((await bidReq(app, buyer, id, 200, r2)).body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
    // 같은 금액을 새 request_id로 연타하면 이미 최고 입찰자
    expect((await bidReq(app, buyer, id, 100)).body.errors.code).toBe('ALREADY_TOP_BIDDER');
    await expectConserved();
  });
});

describe('S15 우편 수령', () => {
  it('순차 이중 수령은 409, 동시 수령(다른 request_id)은 한 번만 지급, 같은 request_id 동시는 재생', async () => {
    const seller = await mk(app);
    const buyer = await mk(app);
    await buyoutReq(app, buyer, await listIron(app, seller, { buyout: 1000 }));
    const mailId = ((await mailList(app, seller))[0] as { id: string }).id;
    const [x, y] = await Promise.all([claimReq(app, seller, mailId), claimReq(app, seller, mailId)]);
    expect([x.status, y.status].sort()).toEqual([200, 409]);
    expect([x, y].find((r) => r.status === 409)?.body.errors.code).toBe('MAIL_ALREADY_CLAIMED');
    expect(await goldNow(seller)).toBe(100_000 - 10 + 960);
    expect((await claimReq(app, seller, mailId)).body.errors.code).toBe('MAIL_ALREADY_CLAIMED');

    const buyMail = ((await mailList(app, buyer))[0] as { id: string }).id;
    const rid = randomUUID();
    const [p, q] = await Promise.all([claimReq(app, buyer, buyMail, rid), claimReq(app, buyer, buyMail, rid)]);
    expect(p.status).toBe(200);
    expect(q.status).toBe(200);
    expect(await countOf(buyer, 'eq_sword_10_u')).toBe(1);
    await expectConserved();
    await expectLedgerConsistent(seller);
    await expectLedgerConsistent(buyer);
  });

  it('남의 우편 id는 404와 의심 기록, 모르는 id도 404', async () => {
    const seller = await mk(app);
    const buyer = await mk(app);
    const stranger = await mk(app);
    await buyoutReq(app, buyer, await listIron(app, seller));
    const mailId = ((await mailList(app, seller))[0] as { id: string }).id;
    const r = await claimReq(app, stranger, mailId);
    expect(r.status).toBe(404);
    expect(r.body.errors.code).toBe('MAIL_NOT_FOUND');
    expect(await flagKinds(stranger)).toEqual(['foreign_id']);
    expect((await claimReq(app, stranger, randomUUID())).status).toBe(404);
    expect(await mailList(app, seller)).toHaveLength(1);
  });

  it('모두 받기는 한 번에 최대 50통, 남은 수를 알려 준다', async () => {
    const h = await mk(app);
    await systemMails(h, 55, 10);
    const first = await claimAllReq(app, h);
    expect(first.status).toBe(200);
    expect(first.body.data).toMatchObject({ claimed_count: 50, remaining: 5, skipped: 0 });
    expect(await goldNow(h)).toBe(100_000 + 500);
    const second = await claimAllReq(app, h);
    expect(second.body.data).toMatchObject({ claimed_count: 5, remaining: 0 });
    expect(await goldNow(h)).toBe(100_000 + 550);
    expect((await claimAllReq(app, h)).body.data.claimed_count).toBe(0);
    await expectConserved();
  });

  it('골드 상한을 넘으면 GOLD_CAP_EXCEEDED이고 우편은 남는다(모두 받기는 건너뛴다)', async () => {
    const h = await mk(app, 2_147_483_600);
    await systemMails(h, 1, 100);
    const m = ((await mailList(app, h))[0] as { id: string }).id;
    const r = await claimReq(app, h, m);
    expect(r.status).toBe(422);
    expect(r.body.errors.code).toBe('GOLD_CAP_EXCEEDED');
    expect(await mailList(app, h)).toHaveLength(1);
    const all = await claimAllReq(app, h);
    expect(all.body.data).toMatchObject({ claimed_count: 0, skipped: 1 });
    expect(await goldNow(h)).toBe(2_147_483_600);
  });

  it('입력 오류: request_id가 없거나 알 수 없는 필드면 400', async () => {
    const h = await mk(app);
    expect((await post(app, h, `/mail/${randomUUID()}/claim`, { request_id: 'x' })).status).toBe(400);
    expect((await post(app, h, '/mail/claim-all', { extra: 1 })).status).toBe(400);
    expect((await get(app, h, '/mail?tab=nope')).status).toBe(400);
    expect((await get(app, h, '/mail?limit=51')).status).toBe(400);
  });
});

describe('S17 우편 30일 경과 폐기', () => {
  it('첨부 아이템은 mail_expire 원장, 골드는 소각 기록, 수령은 410', async () => {
    const seller = await mk(app);
    const bidder = await mk(app);
    // 아이템 우편: 입찰 없이 마감된 반환 우편 / 골드 우편: 밀려난 입찰 반환
    const idA = await listIron(app, seller, { buyout: 1000 });
    const idB = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
    const rival = await mk(app);
    await bidReq(app, bidder, idB, 100);
    await bidReq(app, rival, idB, 105);
    void idA;
    advance(12 * HOUR + MIN);
    await runAuctionTick();
    const items = (await mailList(app, seller)).filter((m) => m.item);
    const golds = (await mailList(app, bidder)).filter((m) => m.gold > 0);
    expect(items).toHaveLength(1);
    expect(golds).toHaveLength(1);
    await expectConserved();

    advance(31 * 24 * HOUR);
    expect(await mailList(app, seller)).toEqual([]); // 목록에서도 사라진다
    const gone = await claimReq(app, seller, (items[0] as { id: string }).id);
    expect(gone.status).toBe(410);
    expect(gone.body.errors.code).toBe('MAIL_EXPIRED');
    const t = await runAuctionTick();
    expect(t.mailsExpired).toBeGreaterThanOrEqual(2);
    const led = await getPool().query("SELECT location, delta FROM item_ledger WHERE reason = 'mail_expire'");
    // 판매자에게 돌아온 장비 우편 + 낙찰자(rival)의 구매 우편
    expect(led.rows).toEqual([{ location: 'mail', delta: -1 }, { location: 'mail', delta: -1 }]);
    const sink = await getPool().query("SELECT sum(amount) AS s FROM auction_sinks WHERE kind = 'mail_expire'");
    expect(Number((sink.rows[0] as { s: string }).s)).toBe(100 + (105 - 6 + 10)); // 입찰 반환 100 + 판매 대금(105 - 수수료 6 + 보증금 10)
    expect(await countOf(seller, 'eq_sword_10_u')).toBe(0);
    await expectConserved();
    // 다시 돌려도 이중 폐기가 없다
    expect((await runAuctionTick()).mailsExpired).toBe(0);
    await expectConserved();
  });
});

describe('S17 보강: 폐기된 첨부 아이템은 원장에 남는다', () => {
  it('구매 우편이 30일 뒤 폐기돼도 원장에 +1(auction_buy)과 -1(mail_expire)이 모두 남는다', async () => {
    const seller = await mk(app);
    const buyer = await mk(app);
    await buyoutReq(app, buyer, await listIron(app, seller, { buyout: 1000 }));
    advance(31 * 24 * HOUR);
    await runAuctionTick();
    const led = await getPool().query(
      "SELECT reason, delta FROM item_ledger WHERE character_id = $1 AND location = 'mail' ORDER BY id",
      [buyer.dbId],
    );
    expect(led.rows).toEqual([{ reason: 'auction_buy', delta: 1 }, { reason: 'mail_expire', delta: -1 }]);
    expect(await countOf(buyer, 'eq_sword_10_u')).toBe(0);
    await expectConserved();
  });
});

describe('우편 도착 알림(NotificationPublisher)', () => {
  it('커밋 뒤에만 발행하고 우편마다 mail.arrived, sold와 outbid는 전용 이벤트도 함께', async () => {
    const { setNotificationPublisher, ChatSysPublisher } = await import('../src/domains/mail/mailNotify');
    type Ev = { type: string; kind: string; gold: number; characterUuid: string; mailUuid: string };
    const events: Ev[] = [];
    setNotificationPublisher({ publish: (e) => events.push(e as Ev) });
    try {
      const seller = await mk(app);
      const b1 = await mk(app);
      const b2 = await mk(app);
      const id = await listIron(app, seller, { buyout: 1000, start_bid: 100 });
      await bidReq(app, b1, id, 100);
      expect(events).toEqual([]); // 우편이 없다
      await bidReq(app, b2, id, 105);
      expect(events.map((e) => e.type)).toEqual(['mail.arrived', 'auction.outbid']);
      expect(events[0]).toMatchObject({ characterUuid: b1.id, kind: 'outbid', gold: 100 });
      events.length = 0;
      // 실패한 요청(롤백)은 알림이 없다
      await bidReq(app, b1, id, 106);
      expect(events).toEqual([]);
      advance(12 * HOUR + MIN);
      await runAuctionTick();
      const types = events.map((e) => `${e.type}:${e.characterUuid === seller.id ? 'seller' : 'buyer'}`).sort();
      expect(types).toEqual(['auction.sold:seller', 'mail.arrived:buyer', 'mail.arrived:seller']);
    } finally {
      setNotificationPublisher(new ChatSysPublisher());
    }
  });
});
