// 정책 문서 페이지와 동의 기록(0037), 동의 강제(POLICY_CONSENT_ENFORCE)
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import request from 'supertest';
import { clearConsentCache } from '../src/domains/policies/consentGate';
import { renderMarkdown, fillPlaceholders } from '../src/domains/policies/policyRenderer';
import { setPoliciesDirForTest } from '../src/domains/policies/policyPages';
import { REQUIRED } from '../src/domains/policies/policyVersions';
import { RATES_VERSION } from '../src/domains/starshop/starshopDefs';
import { auth, buildApp, registerAccount, resetDb, shutdown, ver } from './helpers';

let app = buildApp();
let tmp: string;

beforeAll(async () => {
  await resetDb();
  tmp = fs.mkdtempSync(path.join(os.tmpdir(), 'policies-'));
  fs.mkdirSync(path.join(tmp, 'ko'));
  fs.writeFileSync(path.join(tmp, 'ko', 'terms.md'), '# 이용약관\n\n상호: {{name}}\n\n<script>alert(1)</script>\n\n| 항목 | 내용 |\n|---|---|\n| 이메일 | {{email}} |\n');
  fs.writeFileSync(path.join(tmp, 'operator.json'), JSON.stringify({ name: '', email: 'help@example.com', effective_date: '2026-10-14' }));
  setPoliciesDirForTest(tmp);
});
afterAll(async () => {
  setPoliciesDirForTest(null);
  fs.rmSync(tmp, { recursive: true, force: true });
  await shutdown();
});

const allRequired = () => ({ agree: REQUIRED.map((r) => ({ id: r.id, version: r.version })) });

describe('렌더러', () => {
  it('HTML은 이스케이프하고 위험한 링크는 글자만 남긴다', () => {
    const html = renderMarkdown('# 제목\n<script>alert(1)</script>\n\n[나쁜](javascript:alert(1)) [좋은](https://example.com) **굵게**');
    expect(html).toContain('&lt;script&gt;');
    expect(html).not.toContain('<script>');
    expect(html).not.toContain('javascript:');
    expect(html).toContain('<a href="https://example.com">좋은</a>');
    expect(html).toContain('<strong>굵게</strong>');
  });

  it('빈 자리표시는 (미정)/(TBD)', () => {
    expect(fillPlaceholders('{{name}}/{{email}}', { name: '', email: 'a@b.c' }, 'ko')).toBe('(미정)/a@b.c');
    expect(fillPlaceholders('{{name}}', {}, 'en')).toBe('(TBD)');
  });
});

describe('GET /policies', () => {
  it('필요한 동의 버전과 문서 목록', async () => {
    const res = await request(app).get('/policies');
    expect(res.status).toBe(200);
    expect(res.body.data.required).toEqual(REQUIRED.map((r) => ({ id: r.id, version: r.version })));
    expect(res.body.data.docs).toEqual(expect.arrayContaining(['terms', 'privacy', 'operation', 'paid', 'youth', 'probability']));
    expect(res.body.data.languages).toEqual(['ko', 'en']);
  });

  it('문서 페이지: 자리표시 채움, 표, 스크립트 이스케이프', async () => {
    const res = await request(app).get('/policies/ko/terms');
    expect(res.status).toBe(200);
    expect(res.headers['content-type']).toMatch(/text\/html/);
    expect(res.text).toContain('상호: (미정)');
    expect(res.text).toContain('<td>help@example.com</td>');
    expect(res.text).not.toContain('<script>alert(1)</script>');
    expect(res.text).toContain('시행일: 2026-10-14');
  });

  it('그 언어 문서가 없으면 다른 언어 문서를 보여 준다', async () => {
    const res = await request(app).get('/policies/en/terms');
    expect(res.status).toBe(200);
    expect(res.text).toContain('이용약관');
  });

  it('모르는 문서는 404, 파일이 없는 문서도 404', async () => {
    const a = await request(app).get('/policies/ko/unknown');
    expect(a.status).toBe(404);
    expect(a.body.errors.code).toBe('NOT_FOUND');
    const b = await request(app).get('/policies/ko/youth');
    expect(b.status).toBe(404);
  });

  it('확률 공개는 서버 확률표 버전을 담고, 목록에 없는 언어는 영어로', async () => {
    const ko = await request(app).get('/policies/ko/probability');
    expect(ko.status).toBe(200);
    expect(ko.text).toContain(RATES_VERSION);
    expect(ko.text).toContain('확률형 아이템 정보 공개');
    const other = await request(app).get('/policies/de/probability');
    expect(other.text).toContain('Probability Disclosure');
  });
});

describe('동의 기록 /me/consents', () => {
  it('처음에는 필요, 모두 동의하면 해제, 다른 버전은 422', async () => {
    const s = await registerAccount(app);
    const first = await request(app).get('/me/consents').set(auth(s));
    expect(first.status).toBe(200);
    expect(first.body.data.needs).toBe(true);
    expect(first.body.data.required.every((r: { agreed: boolean }) => !r.agreed)).toBe(true);

    const bad = await request(app).post('/me/consents').set(auth(s)).send({ agree: [{ id: 'terms', version: '1999-01-01' }] });
    expect(bad.status).toBe(422);
    expect(bad.body.errors.code).toBe('VALIDATION');

    const ok = await request(app).post('/me/consents').set(auth(s)).send(allRequired());
    expect(ok.status).toBe(200);
    expect(ok.body.data.needs).toBe(false);
    // 같은 동의를 다시 보내도 그대로
    const again = await request(app).post('/me/consents').set(auth(s)).send(allRequired());
    expect(again.body.data.needs).toBe(false);
  });

  it('로그인 없이는 401', async () => {
    const res = await request(app).get('/me/consents').set(ver());
    expect(res.status).toBe(401);
  });
});

describe('동의 강제(POLICY_CONSENT_ENFORCE=true)', () => {
  afterAll(() => {
    clearConsentCache();
    app = buildApp();
  });

  it('동의 전 게임 API는 428, 동의·정책은 열려 있고, 동의하면 통과', async () => {
    app = buildApp({ POLICY_CONSENT_ENFORCE: 'true' });
    const s = await registerAccount(app);
    const blocked = await request(app).get('/characters').set(auth(s));
    expect(blocked.status).toBe(428);
    expect(blocked.body.errors.code).toBe('CONSENT_REQUIRED');
    expect((await request(app).get('/me/consents').set(auth(s))).status).toBe(200);
    expect((await request(app).get('/policies')).status).toBe(200);
    await request(app).post('/me/consents').set(auth(s)).send(allRequired()).expect(200);
    const open = await request(app).get('/characters').set(auth(s));
    expect(open.status).toBe(200);
  });

  it('꺼져 있으면 동의 없이도 통과', async () => {
    app = buildApp();
    const s = await registerAccount(app);
    expect((await request(app).get('/characters').set(auth(s))).status).toBe(200);
  });
});
