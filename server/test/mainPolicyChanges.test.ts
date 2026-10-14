// 2026-10-14 정책 반영분: 자리표시 빈 줄 제거, 운영자 정보, account-deletion 페이지, WS 게임시간 거절,
// 파티 판 멤버 기기 정보 30일 뒤 NULL, PAY_MIN_ORDER_GAP_SECONDS 기본값 0
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { payShape } from '../src/config/payEnv';
import { fillPlaceholders } from '../src/domains/policies/policyRenderer';
import { setPoliciesDirForTest } from '../src/domains/policies/policyPages';
import { DOCS } from '../src/domains/policies/policyVersions';
import { purgeDaily } from '../src/ops/jobs/purge';
import { buildApp, openDungeonToday, resetDb, shutdown } from './helpers';
import { formParty, newHero, startAndBegin, type Hero } from './partyHelpers';
import { registry } from '../src/domains/chat/realtimeNotifier';
import { sleep, startServer, until, WsClient, helloFrame, type TestServer } from './wsHelpers';
import fs from 'node:fs';
import path from 'node:path';

const app = buildApp();
let srv: TestServer;
const ctx = { opts: {}, shouldStop: () => false };

beforeAll(async () => {
  srv = await startServer(app);
});
beforeEach(async () => {
  await resetDb();
  setPoliciesDirForTest(null);
});
afterEach(async () => {
  await until(() => registry.size() === 0);
});
afterAll(async () => {
  await srv.stop();
  await shutdown();
});

describe('정책 문서', () => {
  it('값이 빈 항목만 있는 표 줄은 빼고, 값이 있는 줄과 문장 속 빈 값은 유지한다', () => {
    const md = '| 항목 | 내용 |\n|---|---|\n| 대표자 | {{ceo}} |\n| 이메일 | {{email}} |\n| 주소 | {{ address }} |\n본문 {{ceo}} 끝';
    const ko = fillPlaceholders(md, { ceo: '', email: 'a@b.c', address: '  ' }, 'ko');
    expect(ko).not.toContain('대표자');
    expect(ko).not.toContain('주소');
    expect(ko).toContain('| 이메일 | a@b.c |');
    expect(ko).toContain('본문 (미정) 끝');
    expect(fillPlaceholders('| a | {{x}} |', {}, 'en')).toBe('');
  });

  it('operator.json에 운영자 이름과 이메일이 채워져 있고, 이용약관에 빈 자리표시가 남지 않는다', async () => {
    const op = JSON.parse(fs.readFileSync(path.resolve(__dirname, '..', 'policies', 'operator.json'), 'utf8')) as Record<string, string>;
    expect(op.name?.trim()).toBeTruthy();
    expect(op.email).toMatch(/@/);
    const res = await request(app).get('/policies/ko/terms');
    expect(res.status).toBe(200);
    expect(res.text).toContain(op.email as string);
    expect(res.text).not.toContain('{{');
  });

  it('account-deletion 문서가 목록에 있고 ko/en 페이지가 200', async () => {
    expect(DOCS).toContain('account-deletion');
    for (const lang of ['ko', 'en']) {
      const res = await request(app).get(`/policies/${lang}/account-deletion`);
      expect(res.status).toBe(200);
      expect(res.headers['content-type']).toMatch(/text\/html/);
    }
    expect((await request(app).get('/policies')).body.data.docs).toContain('account-deletion');
  });

  it('확률 페이지에 장비 강화 절이 있다', async () => {
    const res = await request(app).get('/policies/ko/probability');
    expect(res.text).toContain('장비 강화');
    expect((await request(app).get('/policies/en/probability')).text).toContain('Gear Enhancement');
  });
});

describe('WebSocket hello: 게임시간 선택제', () => {
  it('허용 시간대 밖이면 KICKED(4011)로 재접속 없이 끊고, 구간을 풀면 접속된다', async () => {
    const h = await newHero(app);
    const hm = (m: number) => ((m % 1440) + 1440) % 1440;
    const now = Math.floor(((Date.now() + 9 * 3_600_000) % 86_400_000) / 60_000);
    // 지금에서 3~4시간 뒤의 한 시간 구간만 허용: 지금은 닫혀 있다
    await getPool().query('UPDATE accounts SET play_window_start = $2, play_window_end = $3 WHERE uuid = $1', [h.s.accountId, hm(now + 180), hm(now + 240)]);
    const c = await WsClient.open(srv.port);
    c.send(helloFrame(h));
    const bye = await c.waitT('bye');
    expect(bye).toMatchObject({ code: 4011, reason: 'PLAY_WINDOW_CLOSED', reconnect: false });
    c.close();
    await getPool().query('UPDATE accounts SET play_window_start = NULL, play_window_end = NULL WHERE uuid = $1', [h.s.accountId]);
    const ok = await WsClient.open(srv.port);
    ok.send(helloFrame(h));
    await ok.waitT('ready');
    ok.close();
    await sleep(10);
  });
});

describe('purge-daily: party_run_members_device', () => {
  it('끝난 지 30일이 지난 판 멤버의 기기 정보만 NULL로 만들고 행은 남긴다', async () => {
    const host: Hero = await newHero(app);
    await formParty(app, host, [], { dungeon_id: openDungeonToday() });
    await startAndBegin(app, host, [], 0);
    const hostRun = (await getPool().query('SELECT m.party_run_id FROM party_run_members m WHERE m.character_id = $1', [host.dbId])).rows[0] as { party_run_id: string };
    const hash = 'a'.repeat(64);
    await getPool().query('UPDATE party_run_members SET device_hash = $2, steam_key = $3, install_id = gen_random_uuid() WHERE party_run_id = $1', [hostRun.party_run_id, hash, '76561198000000001']);
    // 아직 끝나지 않은 판은 건드리지 않는다
    await purgeDaily(ctx);
    expect((await getPool().query('SELECT device_hash FROM party_run_members WHERE party_run_id = $1', [hostRun.party_run_id])).rows[0]).toEqual({ device_hash: hash });
    // 29일 전에 끝난 판도 그대로
    await getPool().query("UPDATE party_runs SET state = 'ended', ended_at = now() - interval '29 days' WHERE id = $1", [hostRun.party_run_id]);
    await purgeDaily(ctx);
    expect((await getPool().query('SELECT device_hash FROM party_run_members WHERE party_run_id = $1', [hostRun.party_run_id])).rows[0]).toEqual({ device_hash: hash });
    // 31일 전이면 NULL
    await getPool().query("UPDATE party_runs SET ended_at = now() - interval '31 days' WHERE id = $1", [hostRun.party_run_id]);
    await purgeDaily(ctx);
    const after = await getPool().query('SELECT device_hash, steam_key, install_id, account_id FROM party_run_members WHERE party_run_id = $1', [hostRun.party_run_id]);
    expect(after.rows).toHaveLength(1);
    expect(after.rows[0]).toMatchObject({ device_hash: null, steam_key: null, install_id: null });
  });
});

describe('결제 환경값', () => {
  it('PAY_MIN_ORDER_GAP_SECONDS 기본값은 0이다', () => {
    expect(payShape.PAY_MIN_ORDER_GAP_SECONDS.parse(undefined)).toBe(0);
  });
});
