// 0040: 확률표 공개 기록(이용약관 제14조의2 3항). 지문 upsert, 관리자 조회, 확률 페이지의 지문 표시, 작업 등록
import request from 'supertest';
import { getConfig } from '../src/config/env';
import { getPool } from '../src/db/pool';
import { buildRatesMarkdown, buildRatesPublication } from '../src/domains/policies/ratesDisclosure';
import { recordRatesPublication } from '../src/domains/policies/ratesPublicationService';
import { POLICY } from '../src/domains/withdrawal/withdrawalPolicy';
import { getJob } from '../src/ops/jobRunner';
import { MANUAL_JOBS, registerAllJobs as registerJobs } from '../src/ops/jobs';
import { ratesPublicationJob } from '../src/ops/jobs/ratesPublication';
import { COMMANDS } from '../src/admin/cli/cliCommands';
import { buildApp, resetDb, shutdown } from './helpers';
import { adminApp, adminGet, makeAdmin, type TestAdmin } from './opsHelpers';

const pub = buildApp();
const app = adminApp();
let viewer: TestAdmin;

beforeEach(async () => {
  await resetDb();
  await getPool().query('DELETE FROM rates_publications');
  viewer = await makeAdmin('viewer');
});
afterAll(async () => {
  await shutdown();
});

const rows = async () => (await getPool().query('SELECT * FROM rates_publications ORDER BY id')).rows as { id: string; fingerprint: string; content: string; versions: Record<string, string>; first_seen_at: Date; last_seen_at: Date }[];

describe('지문과 upsert', () => {
  it('생성 시각이 달라도 지문은 같고, 내용에는 생성 시각 줄이 없다', () => {
    const dir = getConfig().gameDataDir;
    const a = buildRatesPublication(dir);
    expect(a.fingerprint).toMatch(/^[0-9a-f]{64}$/);
    expect(a.content).not.toContain('생성 시각');
    expect(a.content).not.toContain('1970-01-01');
    // 같은 문서에서 시각만 다른 두 번의 빌드는 시각을 뺀 뒤 같은 문자열이다
    const x = buildRatesMarkdown(dir, 'ko', new Date('2026-01-01T00:00:00Z')).replace(/생성 시각: [^ ]+ /, '');
    const y = buildRatesMarkdown(dir, 'ko', new Date('2030-06-01T00:00:00Z')).replace(/생성 시각: [^ ]+ /, '');
    expect(x).toBe(y);
    expect(a.versions).toMatchObject({ cosmetic: expect.any(String), sealed: expect.any(String), raid: expect.any(String) });
  });

  it('같은 지문이면 한 줄만 두고 last_seen_at 만 갱신한다', async () => {
    const first = await recordRatesPublication();
    expect(first.created).toBe(true);
    const [r1] = await rows();
    await new Promise((r) => setTimeout(r, 20));
    const second = await recordRatesPublication();
    expect(second.created).toBe(false);
    const all = await rows();
    expect(all).toHaveLength(1);
    expect(all[0]?.fingerprint).toBe(first.fingerprint);
    expect(all[0]?.first_seen_at.getTime()).toBe(r1?.first_seen_at.getTime());
    expect((all[0]?.last_seen_at.getTime() ?? 0) > (r1?.last_seen_at.getTime() ?? 0)).toBe(true);
    // 동시에 불러도 한 줄
    await Promise.all([recordRatesPublication(), recordRatesPublication()]);
    expect(await rows()).toHaveLength(1);
  });

  it('1시간 작업이 등록되어 있고 수동 실행 허용 목록에 있으며, 탈퇴 정책에서 계정 무관 표로 분류된다', async () => {
    registerJobs();
    expect(getJob('rates-publication')?.schedule).toEqual({ kind: 'every', minutes: 60 });
    expect(MANUAL_JOBS).toContain('rates-publication');
    const r = await ratesPublicationJob();
    expect(r.rows).toBe(1);
    expect(POLICY.find((p) => p.table === 'rates_publications')).toMatchObject({ t1: 'keep', t2: 'forever' });
  });
});

describe('관리자 조회와 CLI', () => {
  it('목록은 최신순에 content 제외, 단건은 content 포함, 없는 id 404, 잘못된 id 400, 인증 없이 401', async () => {
    await recordRatesPublication();
    await getPool().query(
      `INSERT INTO rates_publications (fingerprint, versions, content) VALUES (repeat('b', 64), '{"cosmetic":"old"}'::jsonb, '옛 문서')`,
    );
    const list = await adminGet(app, viewer, '/admin/rates/publications');
    expect(list.status).toBe(200);
    expect(list.body.data.items).toHaveLength(2);
    expect(list.body.data.items[0].fingerprint).toBe('b'.repeat(64));
    expect(list.body.data.items[0]).not.toHaveProperty('content');
    const one = await adminGet(app, viewer, `/admin/rates/publications/${list.body.data.items[1].id}`);
    expect(one.status).toBe(200);
    expect(one.body.data.content).toContain('확률형 아이템 정보 공개');
    expect((await adminGet(app, viewer, '/admin/rates/publications/999999')).status).toBe(404);
    expect((await adminGet(app, viewer, '/admin/rates/publications/abc')).status).toBe(400);
    expect((await request(app).get('/admin/rates/publications')).status).toBe(401);
  });

  it('CLI에 rates publications / rates show 명령이 있다', () => {
    const paths = COMMANDS.filter((c) => c.path[0] === 'rates').map((c) => c.path[1]);
    expect(paths).toEqual(['publications', 'show']);
  });
});

describe('확률 공개 페이지', () => {
  it('현재 지문 앞 12자리를 한 줄로 보여 준다(ko/en), 저장되는 내용에는 이 줄이 없다', async () => {
    const fp = buildRatesPublication(getConfig().gameDataDir).fingerprint.slice(0, 12);
    const ko = await request(pub).get('/policies/ko/probability');
    expect(ko.status).toBe(200);
    expect(ko.text).toContain(`확률표 지문: ${fp}`);
    const en = await request(pub).get('/policies/en/probability');
    expect(en.text).toContain(`Rate table fingerprint: ${fp}`);
    expect(buildRatesPublication(getConfig().gameDataDir).content).not.toContain('확률표 지문');
  });
});
