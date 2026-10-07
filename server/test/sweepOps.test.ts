// 10단계 작업·지표·정리: 클리어권 만료, 캠페인 작업 등록, 주간 카운터 정리, 스냅샷, CLI 명령
import { getPool } from '../src/db/pool';
import { COMMANDS } from '../src/admin/cli/cliCommands';
import { getJob } from '../src/ops/jobRunner';
import { MANUAL_JOBS, registerAllJobs } from '../src/ops/jobs';
import { purgeDaily } from '../src/ops/jobs/purge';
import { sweepTicketExpireJob } from '../src/ops/jobs/sweepJobs';
import { collectSnapshot } from '../src/ops/snapshot';
import { setClockOverride } from '../src/utils/clock';
import { newHero, post } from './economyHelpers';
import { buildApp, resetDb, shutdown } from './helpers';
import { MONDAY, accountIdOf, expectTicketLedgerConsistent, seedTickets, ticketTotal } from './sweepHelpers';

let fixed = new Date(MONDAY);
const ctx = { opts: {}, shouldStop: () => false };
beforeEach(async () => {
  await resetDb();
  fixed = new Date(MONDAY);
  setClockOverride(() => fixed);
  buildApp({ SWEEP_ENABLED: 'true' });
});
afterAll(async () => {
  setClockOverride(null);
  await shutdown();
});

describe('sweep-ticket-expire', () => {
  it('기한이 지난 이벤트 로트만 0으로 만들고 원장 expire를 남기며(로트별 SUM = remaining), 다시 돌려도 변화가 없다', async () => {
    const app = buildApp({ SWEEP_ENABLED: 'true' });
    const h = await newHero(app);
    const day = 86_400_000;
    await seedTickets(h, {
      normal: 2,
      events: [
        { n: 3, expiresAt: new Date(fixed.getTime() - 1000) },
        { n: 4, expiresAt: new Date(fixed.getTime() + 5 * day) },
      ],
    });
    const r = await sweepTicketExpireJob(ctx);
    expect(r.detail).toEqual({ lots: 1, tickets: 3 });
    const lots = await getPool().query('SELECT kind, remaining, expires_at FROM sweep_ticket_lots ORDER BY id');
    expect(lots.rows.map((x: { kind: string; remaining: number }) => [x.kind, x.remaining])).toEqual([['normal', 2], ['event', 0], ['event', 4]]);
    const ex = await getPool().query("SELECT delta, balance_after, character_id, request_id FROM sweep_ticket_ledger WHERE reason = 'expire'");
    expect(ex.rows).toEqual([{ delta: -3, balance_after: 6, character_id: null, request_id: null }]);
    await expectTicketLedgerConsistent(h);
    expect(await ticketTotal(h, fixed)).toBe(6);
    expect((await sweepTicketExpireJob(ctx)).detail).toEqual({ lots: 0, tickets: 0 });
    // 원장은 추가 전용이다
    await expect(getPool().query("UPDATE sweep_ticket_ledger SET delta = -1 WHERE reason = 'expire'")).rejects.toThrow();
    await expect(getPool().query('DELETE FROM sweep_ticket_ledger')).rejects.toThrow();
  });
});

describe('작업 등록과 정리', () => {
  it('세 작업이 등록되어 있고 관리자 수동 실행 허용 목록에 있다', () => {
    registerAllJobs();
    for (const n of ['sweep-ticket-expire', 'campaign-sweep', 'campaign-revoke']) {
      expect(getJob(n)).toBeDefined();
      expect(MANUAL_JOBS).toContain(n);
    }
    expect(getJob('campaign-revoke')?.schedule).toEqual({ kind: 'every', minutes: 0.5 });
  });

  it('purge-daily가 60일 지난 주간 카운터만 지운다', async () => {
    const app = buildApp({ SWEEP_ENABLED: 'true' });
    const h = await newHero(app);
    const acct = await accountIdOf(h);
    await getPool().query(
      `INSERT INTO account_week_counters (account_id, week_start, kind, used) VALUES
         ($1, now() - interval '70 days', 'sweep_buy', 3), ($1, now() - interval '70 days', 'direct_clear', 4), ($1, now() - interval '3 days', 'direct_clear', 1)`,
      [acct],
    );
    setClockOverride(null);
    await purgeDaily(ctx);
    const left = await getPool().query('SELECT kind, used FROM account_week_counters');
    expect(left.rows).toEqual([{ kind: 'direct_clear', used: 1 }]);
  });
});

describe('스냅샷 지표', () => {
  it('sweep과 campaign 블록이 있다', async () => {
    const app = buildApp({ SWEEP_ENABLED: 'true' });
    const h = await newHero(app);
    await seedTickets(h, { normal: 4 });
    setClockOverride(null);
    const snap = await collectSnapshot();
    expect(snap.sweep).toEqual({ runs_1h: 0, tickets_outstanding: 4, buy_limit_hits: expect.any(Number) });
    expect(snap.campaign).toEqual({ active: 0, delivered_1h: 0, cap_reached: 0, revoke_pending: 0 });
    expect(await post(app, h, '/sweep/weekly/claim', {})).toBeDefined();
  });
});

describe('관리자 CLI', () => {
  it('campaign create|list|show|approve|cancel|deliveries 명령이 MC1~MC6에 1:1로 있다', () => {
    const paths = COMMANDS.filter((c) => c.path[0] === 'campaign').map((c) => c.path[1]);
    expect(paths).toEqual(['create', 'list', 'show', 'approve', 'cancel', 'deliveries']);
  });
});
