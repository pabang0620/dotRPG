// 0038 이름 변경 요청: 운영자가 부적절한 이름을 임시 이름으로 바꾸고, 플레이어가 새 이름을 한 번 고른다.
import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import { getRateLimitStore } from '../src/middleware/rateLimiter';
import { auth, buildApp, createChar, randomName, registerAccount, resetDb, shutdown, type Session } from './helpers';
import { adminApp, adminPost, auditRows, makeAdmin, type TestAdmin } from './opsHelpers';

const pub = buildApp();
const app = adminApp();
let operator: TestAdmin;
let viewer: TestAdmin;

beforeEach(async () => {
  await resetDb();
  getRateLimitStore().clear();
  [operator, viewer] = [await makeAdmin('operator'), await makeAdmin('viewer')];
});
afterAll(async () => {
  await shutdown();
});

async function hero(): Promise<{ s: Session; id: string; name: string }> {
  const s = await registerAccount(pub);
  const name = randomName();
  const res = await createChar(pub, s, name);
  expect(res.status).toBe(201);
  return { s, id: res.body.data.character.id, name };
}

const rename = (s: Session, id: string, name: string, requestId: string = randomUUID()) =>
  request(pub).post(`/characters/${id}/rename`).set(auth(s)).send({ request_id: requestId, name });

describe('[0038] 이름 변경 요청', () => {
  it('운영자는 임시 이름으로 바꾸고 경고를 남긴다. viewer는 할 수 없다', async () => {
    const h = await hero();
    expect((await adminPost(app, viewer, `/admin/characters/${h.id}/force-rename`, { note: '욕설' })).status).toBe(403);
    const r = await adminPost(app, operator, `/admin/characters/${h.id}/force-rename`, { note: '부적절한 이름입니다' });
    expect(r.status).toBe(200);
    expect(r.body.data).toMatchObject({ character_uuid: h.id, old_name: h.name });
    expect(r.body.data.temp_name).toMatch(/^이름변경\d{4}$/);
    const row = (await getPool().query('SELECT name, rename_required, rename_note FROM characters WHERE uuid = $1', [h.id])).rows[0];
    expect(row).toMatchObject({ name: r.body.data.temp_name, rename_required: true, rename_note: '부적절한 이름입니다' });
    const warn = (await getPool().query(`SELECT kind, reason_code FROM account_sanctions s JOIN accounts a ON a.id = s.account_id WHERE a.uuid = $1`, [h.s.accountId])).rows;
    expect(warn).toEqual([{ kind: 'warning', reason_code: 'other' }]);
    expect((await auditRows("action = 'character.force_rename' AND result = 'ok'")).length).toBe(1);
    expect((await adminPost(app, operator, `/admin/characters/${randomUUID()}/force-rename`, { note: 'x' })).status).toBe(404);
  });

  it('목록과 상세에 표시되고, 규칙에 맞는 새 이름으로 바꾸면 표시가 꺼진다', async () => {
    const h = await hero();
    const other = await hero();
    // 요청 전에는 바꿀 수 없다
    expect((await rename(h.s, h.id, randomName())).body.errors.code).toBe('RENAME_NOT_REQUIRED');
    await adminPost(app, operator, `/admin/characters/${h.id}/force-rename`, { note: '사유' });
    const list = await request(pub).get('/characters').set(auth(h.s));
    expect(list.body.data.characters[0]).toMatchObject({ id: h.id, rename_required: true, rename_note: '사유' });
    const detail = await request(pub).get(`/characters/${h.id}`).set(auth(h.s));
    expect(detail.body.data.character).toMatchObject({ rename_required: true, rename_note: '사유' });

    expect((await rename(h.s, h.id, '운영자')).body.errors.code).toBe('NAME_FORBIDDEN');
    expect((await rename(h.s, h.id, '시발놈')).body.errors.code).toBe('NAME_FORBIDDEN');
    expect((await rename(h.s, h.id, other.name)).body.errors.code).toBe('NAME_TAKEN');
    expect((await rename(h.s, h.id, 'a')).status).toBe(400);
    // 다른 사람의 캐릭터는 바꿀 수 없다
    expect((await rename(other.s, h.id, randomName())).status).toBe(404);

    const fresh = randomName();
    const rid = randomUUID();
    const ok = await rename(h.s, h.id, fresh, rid);
    expect(ok.status).toBe(200);
    expect(ok.body.data.character).toMatchObject({ id: h.id, name: fresh, rename_required: false, rename_note: null });
    // 같은 request_id 재전송은 처음 응답
    const again = await rename(h.s, h.id, fresh, rid);
    expect(again.status).toBe(200);
    expect(again.headers['idempotent-replay']).toBe('true');
    expect(again.body.data.character.name).toBe(fresh);
    // 한 번 고른 뒤에는 다시 바꿀 수 없다
    expect((await rename(h.s, h.id, randomName())).body.errors.code).toBe('RENAME_NOT_REQUIRED');
    const row = (await getPool().query('SELECT name, rename_required, rename_note FROM characters WHERE uuid = $1', [h.id])).rows[0];
    expect(row).toEqual({ name: fresh, rename_required: false, rename_note: null });
  });
});
