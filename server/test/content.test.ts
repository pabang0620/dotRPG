// 챕터 3 이야기 묶음(Docs/ch3/LIVE_CONTENT.md 7절): GET /content/chapter3은 3장이 열린 서버만 응답한다
import fs from 'node:fs';
import path from 'node:path';
import request from 'supertest';
import { auth, buildApp, DATA_DIR, DATA_VERSION, registerAccount, resetDb, shutdown, ver } from './helpers';

beforeEach(async () => {
  await resetDb();
});
afterAll(async () => {
  buildApp();
  await shutdown();
});

const fetchContent = async (open: boolean, extra: Record<string, string> = {}) => {
  const app = buildApp(open ? { CHAPTER3_OPEN: 'true' } : {});
  const s = await registerAccount(app);
  return request(app).get('/content/chapter3').set(auth(s)).set(extra);
};

describe('GET /content/chapter3', () => {
  it('닫힘: 422 CHAPTER_LOCKED, 이야기 문장은 한 줄도 오지 않는다', async () => {
    const res = await fetchContent(false);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('CHAPTER_LOCKED');
    expect(JSON.stringify(res.body)).not.toContain('c3_depart');
  });

  it('로그인하지 않으면 받을 수 없다', async () => {
    const app = buildApp({ CHAPTER3_OPEN: 'true' });
    const res = await request(app).get('/content/chapter3').set(ver());
    expect(res.status).toBe(401);
  });

  it('열림: 묶음·11개 언어·데이터 버전을 주고, 같은 버전 재요청은 304', async () => {
    const res = await fetchContent(true);
    expect(res.status).toBe(200);
    expect(res.body.data.data_version).toBe(DATA_VERSION);
    const bundle = JSON.parse(res.body.data.bundle as string) as { chapter: { number: number }; quests: { id: string }[]; cutscenes: unknown[]; dialogues: unknown[] };
    expect(bundle.chapter.number).toBe(3);
    expect(bundle.quests.map((q) => q.id)).toContain('c3_depart');
    expect(bundle.cutscenes.length).toBeGreaterThan(0);
    expect(bundle.dialogues.length).toBeGreaterThan(0);
    expect(Object.keys(res.body.data.lang as object)).toHaveLength(11);
    const etag = res.headers.etag as string;
    expect(etag).toContain(DATA_VERSION);
    const again = await fetchContent(true, { 'If-None-Match': etag });
    expect(again.status).toBe(304);
  });

  it('묶음은 데이터 버전 해시 안에 있다(출시 빌드와 서버가 같은 묶음을 본다)', () => {
    const ver = JSON.parse(fs.readFileSync(path.join(DATA_DIR, 'data_version.json'), 'utf8')) as { files: Record<string, string> };
    expect(ver.files['chapter3.json']).toBeDefined();
  });
});
