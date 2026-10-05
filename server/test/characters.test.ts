import { randomUUID } from 'node:crypto';
import request from 'supertest';
import { getPool } from '../src/db/pool';
import {
  DATA_VERSION,
  auth,
  buildApp,
  createChar,
  emptyState,
  randomName,
  registerAccount,
  resetDb,
  shutdown,
  ver,
} from './helpers';

const app = buildApp();

beforeAll(resetDb);
afterAll(shutdown);

const starter = {
  gold: 100,
  items: { potion_hp: 3, potion_mp: 2, scroll_town: 1 } as Record<string, number>,
};

describe('POST /characters', () => {
  it('정상: 201, 시작 지급(골드·소모품·장비)과 원장 기록', async () => {
    const s = await registerAccount(app);
    const res = await createChar(app, s, '용사하나', 'warrior');
    expect(res.status).toBe(201);
    const c = res.body.data.character;
    expect(c).toMatchObject({ name: '용사하나', class: 'warrior', level: 1, xp: 0, gold: starter.gold });
    expect(c.state).toMatchObject({ version: 0, map_id: 'village', pos: null, facing: 0, passives: [] });
    const bag = Object.fromEntries(
      c.items.filter((i: { location: string }) => i.location === 'bag').map((i: { item_key: string; count: number }) => [i.item_key, i.count]),
    );
    expect(bag).toEqual(starter.items);
    expect(c.items.find((i: { location: string }) => i.location === 'worn')).toMatchObject({
      item_key: 'eq_sword_wood',
      slot: 0,
      count: 1,
    });
    expect(JSON.stringify(res.body)).not.toMatch(/"(account_id|character_id)"/);

    const db = getPool();
    const ch = await db.query('SELECT id, gold FROM characters WHERE uuid = $1', [c.id]);
    const gl = await db.query('SELECT delta, balance_after, reason, ref FROM gold_ledger WHERE character_id = $1', [ch.rows[0].id]);
    expect(gl.rows).toEqual([{ delta: '100', balance_after: '100', reason: 'starter', ref: c.id }]);
    expect(Number(ch.rows[0].gold)).toBe(100);
    const il = await db.query('SELECT item_key, delta FROM item_ledger WHERE character_id = $1', [ch.rows[0].id]);
    expect(il.rowCount).toBe(4); // 소모품 3 + 장비 1
  });

  it('직업별 시작 장비(마법사)', async () => {
    const s = await registerAccount(app);
    const res = await createChar(app, s, randomName(), 'mage');
    expect(res.status).toBe(201);
    expect(res.body.data.character.items.find((i: { location: string }) => i.location === 'worn').item_key).toBe('eq_staff_oak');
  });

  it('입력 오류: 이름 길이/문자/직업/request_id/허용 안 되는 필드', async () => {
    const s = await registerAccount(app);
    const short = await createChar(app, s, '가');
    expect(short.status).toBe(400);
    expect(short.body.errors.code).toBe('VALIDATION');
    expect(short.body.errors.fields[0]).toMatchObject({ path: 'name', code: 'NAME_LENGTH' });
    const long = await createChar(app, s, '가나다라마바사아자');
    expect(long.body.errors.fields[0].code).toBe('NAME_LENGTH');
    for (const bad of ['ㄱㅏ나', 'a b', 'a_b', ' 용사', '용사😀']) {
      const r = await createChar(app, s, bad);
      expect(r.status).toBe(400);
      expect(r.body.errors.fields.some((f: { code?: string }) => f.code === 'NAME_CHARS')).toBe(true);
    }
    const cls = await request(app).post('/characters').set(auth(s)).send({ request_id: randomUUID(), name: '용사둘', class: 'god' });
    expect(cls.status).toBe(400);
    const noId = await request(app).post('/characters').set(auth(s)).send({ name: '용사둘', class: 'mage' });
    expect(noId.status).toBe(400);
    const extra = await request(app).post('/characters').set(auth(s)).send({ request_id: randomUUID(), name: '용사둘', class: 'mage', gold: 999999 });
    expect(extra.status).toBe(400);
    // 아무것도 만들어지지 않았다
    const list = await request(app).get('/characters').set(auth(s));
    expect(list.body.data.characters).toHaveLength(0);
  });

  it('NFC 정규화: 분해형(NFD) 한글도 완성형으로 저장', async () => {
    const s = await registerAccount(app);
    const nfd = '용사'.normalize('NFD');
    const res = await createChar(app, s, nfd);
    expect(res.status).toBe(201);
    expect(res.body.data.character.name).toBe('용사');
  });

  it('이름 중복: 대소문자 무시 409 NAME_TAKEN, 삭제하면 바로 재사용', async () => {
    const a = await registerAccount(app);
    const b = await registerAccount(app);
    const made = await createChar(app, a, 'Hero77');
    expect(made.status).toBe(201);
    const dup = await createChar(app, b, 'hERO77');
    expect(dup.status).toBe(409);
    expect(dup.body.errors.code).toBe('NAME_TAKEN');
    const del = await request(app).delete(`/characters/${made.body.data.character.id}`).set(auth(a));
    expect(del.status).toBe(200);
    const again = await createChar(app, b, 'hERO77');
    expect(again.status).toBe(201);
  });

  it('재전송: 같은 request_id 두 번 -> 같은 응답, 지급은 한 번, Idempotent-Replay 헤더', async () => {
    const s = await registerAccount(app);
    const rid = randomUUID();
    const name = randomName();
    const first = await createChar(app, s, name, 'warrior', rid);
    const second = await createChar(app, s, name, 'warrior', rid);
    expect(first.status).toBe(201);
    expect(second.status).toBe(201);
    expect(second.headers['idempotent-replay']).toBe('true');
    expect(first.headers['idempotent-replay']).toBeUndefined();
    expect(second.body).toEqual(first.body);
    const id = first.body.data.character.id;
    const n = await getPool().query(
      `SELECT (SELECT count(*) FROM characters WHERE account_id = (SELECT id FROM accounts WHERE uuid = $1))::int AS chars,
              (SELECT count(*) FROM gold_ledger WHERE ref = $2)::int AS gold_rows,
              (SELECT count(*) FROM item_ledger WHERE ref = $2)::int AS item_rows`,
      [s.accountId, id],
    );
    expect(n.rows[0]).toEqual({ chars: 1, gold_rows: 1, item_rows: 4 });
  });

  it('재전송: 처음 이후 이름이 선점돼도 처음 응답을 준다', async () => {
    const a = await registerAccount(app);
    const rid = randomUUID();
    const name = randomName();
    const first = await createChar(app, a, name, 'warrior', rid);
    expect(first.status).toBe(201);
    await request(app).delete(`/characters/${first.body.data.character.id}`).set(auth(a));
    const b = await registerAccount(app);
    expect((await createChar(app, b, name)).status).toBe(201);
    const replay = await createChar(app, a, name, 'warrior', rid);
    expect(replay.status).toBe(201);
    expect(replay.body).toEqual(first.body);
  });

  it('같은 request_id에 다른 본문: 422 IDEMPOTENCY_MISMATCH', async () => {
    const s = await registerAccount(app);
    const rid = randomUUID();
    expect((await createChar(app, s, randomName(), 'warrior', rid)).status).toBe(201);
    const res = await createChar(app, s, randomName(), 'warrior', rid);
    expect(res.status).toBe(422);
    expect(res.body.errors.code).toBe('IDEMPOTENCY_MISMATCH');
  });

  it('실패한 요청은 request_log에 남지 않아 같은 id로 고쳐 다시 보낼 수 있다', async () => {
    const a = await registerAccount(app);
    const b = await registerAccount(app);
    const taken = randomName();
    await createChar(app, a, taken);
    const rid = randomUUID();
    const fail = await createChar(app, b, taken, 'warrior', rid);
    expect(fail.status).toBe(409);
    const retry = await createChar(app, b, randomName(), 'warrior', rid);
    expect(retry.status).toBe(201);
  });

  it('동시 요청: 같은 request_id 2개 -> 둘 다 같은 결과, 캐릭터 1개', async () => {
    const s = await registerAccount(app);
    const rid = randomUUID();
    const name = randomName();
    const [a, b] = await Promise.all([
      createChar(app, s, name, 'mage', rid),
      createChar(app, s, name, 'mage', rid),
    ]);
    expect([a.status, b.status]).toEqual([201, 201]);
    expect(a.body).toEqual(b.body);
    const list = await request(app).get('/characters').set(auth(s));
    expect(list.body.data.characters).toHaveLength(1);
    const gl = await getPool().query('SELECT count(*)::int AS n FROM gold_ledger WHERE ref = $1', [a.body.data.character.id]);
    expect(gl.rows[0].n).toBe(1);
  });

  it('동시 요청: 같은 이름, 다른 계정 -> 한 명만 성공', async () => {
    const a = await registerAccount(app);
    const b = await registerAccount(app);
    const name = randomName();
    const [ra, rb] = await Promise.all([createChar(app, a, name), createChar(app, b, name)]);
    expect([ra.status, rb.status].sort()).toEqual([201, 409]);
  });

  it('동시 요청: 한 계정이 6개를 동시에 만들어도 4개를 넘지 못한다 + 슬롯 초과는 422', async () => {
    const s = await registerAccount(app);
    const results = await Promise.all(Array.from({ length: 6 }, () => createChar(app, s, randomName())));
    const ok = results.filter((r) => r.status === 201);
    const full = results.filter((r) => r.status === 422);
    expect(ok).toHaveLength(4);
    expect(full).toHaveLength(2);
    expect(full[0]?.body.errors.code).toBe('CHARACTER_LIMIT_REACHED');
    const me = await request(app).get('/me').set(auth(s));
    expect(me.body.data.character_count).toBe(4);
    // 삭제하면 슬롯이 하나 비워진다
    const del = await request(app).delete(`/characters/${ok[0]?.body.data.character.id}`).set(auth(s));
    expect(del.status).toBe(200);
    expect((await createChar(app, s, randomName())).status).toBe(201);
  });

  it('속도 제한: 계정당 한도 초과 429', async () => {
    const limited = buildApp({ RATE_CHAR_CREATE_MAX: '2' });
    const s = await registerAccount(limited);
    expect((await createChar(limited, s, randomName())).status).toBe(201);
    expect((await createChar(limited, s, randomName())).status).toBe(201);
    const r = await createChar(limited, s, randomName());
    expect(r.status).toBe(429);
    expect(r.headers['retry-after']).toBeDefined();
    buildApp();
  });
});

describe('버전 검사와 인증 (/characters*)', () => {
  it('데이터 버전 불일치 426 DATA_OUTDATED, 없으면 400', async () => {
    const s = await registerAccount(app);
    const old = await request(app).get('/characters').set({ 'X-Client-Version': '0.2.0', 'X-Data-Version': '0000000000000000', Authorization: `Bearer ${s.access}` });
    expect(old.status).toBe(426);
    expect(old.body.errors).toMatchObject({ code: 'DATA_OUTDATED', client_data_version: '0000000000000000', data_version: DATA_VERSION });
    const none = await request(app).get('/characters').set({ 'X-Client-Version': '0.2.0', Authorization: `Bearer ${s.access}` });
    expect(none.status).toBe(400);
    expect(none.body.errors.code).toBe('DATA_VERSION_MISSING');
  });

  it('클라이언트 버전이 낮으면 인증보다 먼저 426', async () => {
    const res = await request(app).get('/characters').set({ 'X-Client-Version': '0.1.0', 'X-Data-Version': DATA_VERSION });
    expect(res.status).toBe(426);
    expect(res.body.errors.code).toBe('CLIENT_OUTDATED');
  });

  it('토큰이 없으면 401', async () => {
    const res = await request(app).get('/characters').set(ver());
    expect(res.status).toBe(401);
    expect(res.body.errors.code).toBe('TOKEN_MISSING');
  });
});

describe('GET /characters, GET/DELETE /characters/:uuid', () => {
  it('목록: 내 것만, 생성 순서, 위치·updated_at 포함', async () => {
    const a = await registerAccount(app);
    const b = await registerAccount(app);
    const c1 = await createChar(app, a, randomName());
    const c2 = await createChar(app, a, randomName(), 'mage');
    await createChar(app, b, randomName());
    const res = await request(app).get('/characters').set(auth(a));
    expect(res.status).toBe(200);
    expect(res.body.data.limit).toBe(4);
    expect(res.body.data.characters.map((c: { id: string }) => c.id)).toEqual([c1.body.data.character.id, c2.body.data.character.id]);
    expect(res.body.data.characters[0]).toMatchObject({ level: 1, map_id: 'village', pos: null });
    expect(res.body.data.characters[0].updated_at).toEqual(expect.any(String));
  });

  it('상세: 정상 + 남의 캐릭터·없는 id·삭제된 캐릭터는 같은 404', async () => {
    const a = await registerAccount(app);
    const b = await registerAccount(app);
    const c = await createChar(app, a, randomName());
    const id = c.body.data.character.id;
    const ok = await request(app).get(`/characters/${id}`).set(auth(a));
    expect(ok.status).toBe(200);
    expect(ok.body.data.character.id).toBe(id);

    const other = await request(app).get(`/characters/${id}`).set(auth(b));
    const missing = await request(app).get(`/characters/${randomUUID()}`).set(auth(a));
    expect(other.status).toBe(404);
    expect(other.body).toEqual(missing.body);
    expect(other.body.errors.code).toBe('CHARACTER_NOT_FOUND');

    await request(app).delete(`/characters/${id}`).set(auth(a));
    const gone = await request(app).get(`/characters/${id}`).set(auth(a));
    expect(gone.status).toBe(404);
    expect(gone.body).toEqual(missing.body);
  });

  it('uuid 형식이 아니면 400', async () => {
    const s = await registerAccount(app);
    const res = await request(app).get('/characters/not-a-uuid').set(auth(s));
    expect(res.status).toBe(400);
  });

  it('삭제: 소프트 삭제(행·원장 유지), 재삭제 200, 남의 것·없는 것 404', async () => {
    const a = await registerAccount(app);
    const b = await registerAccount(app);
    const id = (await createChar(app, a, randomName())).body.data.character.id;
    const stranger = await request(app).delete(`/characters/${id}`).set(auth(b));
    expect(stranger.status).toBe(404);
    const del = await request(app).delete(`/characters/${id}`).set(auth(a));
    expect(del.status).toBe(200);
    expect(del.body.data).toEqual({ deleted: true });
    const again = await request(app).delete(`/characters/${id}`).set(auth(a));
    expect(again.status).toBe(200);
    const none = await request(app).delete(`/characters/${randomUUID()}`).set(auth(a));
    expect(none.status).toBe(404);
    const row = await getPool().query(
      `SELECT c.deleted_at, (SELECT count(*) FROM gold_ledger g WHERE g.character_id = c.id)::int AS g,
              (SELECT count(*) FROM character_items i WHERE i.character_id = c.id)::int AS i
         FROM characters c WHERE c.uuid = $1`,
      [id],
    );
    expect(row.rows[0].deleted_at).not.toBeNull();
    expect(row.rows[0].g).toBe(1);
    expect(row.rows[0].i).toBe(4);
  });
});

describe('PUT /characters/:uuid/state', () => {
  async function fresh() {
    const s = await registerAccount(app);
    const c = await createChar(app, s, randomName());
    return { s, id: c.body.data.character.id as string };
  }
  const put = (s: Parameters<typeof auth>[0], id: string, body: unknown) =>
    request(app).put(`/characters/${id}/state`).set(auth(s)).send(body as object);

  it.each(['village', 'canyon', 'winter'].flatMap(town => ['shop', 'blacksmith', 'storage'].map(service => `${town}_${service}`)))('%s: 실내 위치 저장·재접속 및 5×5 경계 검증', async mapId => {
    const { s, id } = await fresh();
    const pos = { x: 2.5, y: 1.35 };
    const res = await put(s, id, emptyState(0, { map_id: mapId, pos }));
    expect(res.status).toBe(200);
    const get = await request(app).get(`/characters/${id}`).set(auth(s));
    expect(get.body.data.character.state).toMatchObject({ version: 1, map_id: mapId, pos });
    const outside = await put(s, id, emptyState(1, { map_id: mapId, pos: { x: 6, y: 2 } }));
    expect(outside.status).toBe(422);
  });

  it('정상: 저장 후 version +1, GET으로 그대로 읽힌다', async () => {
    const { s, id } = await fresh();
    const body = emptyState(0, {
      map_id: 'forest',
      pos: { x: 10.5, y: 20.25 },
      facing: 3,
      quests: [{ id: 'c1_morning', status: 1, step: 2, counts: [1, 0] }],
      story_flags: ['some_unlisted_flag'],
      tracked_quest: 'c1_morning',
    });
    const res = await put(s, id, body);
    expect(res.status).toBe(200);
    expect(res.body.data.version).toBe(1);
    const get = await request(app).get(`/characters/${id}`).set(auth(s));
    expect(get.body.data.character.state).toMatchObject({
      version: 1,
      map_id: 'forest',
      pos: { x: 10.5, y: 20.25 },
      facing: 3,
      quests: [{ id: 'c1_morning', status: 1, step: 2, counts: [1, 0] }],
      story_flags: ['some_unlisted_flag'],
      tracked_quest: 'c1_morning',
    });
    // 재화는 그대로
    expect(get.body.data.character).toMatchObject({ level: 1, xp: 0, gold: starter.gold });
    const second = await put(s, id, emptyState(1, { map_id: 'forest', story_flags: ['some_unlisted_flag'], quests: body.quests }));
    expect(second.body.data.version).toBe(2);
  });

  it('입력 오류: level/gold/items 등 허용되지 않는 필드는 400이고 저장되지 않는다', async () => {
    const { s, id } = await fresh();
    for (const extra of [{ level: 99 }, { xp: 5 }, { gold: 1 }, { items: [] }]) {
      const res = await put(s, id, { ...emptyState(0), ...extra });
      expect(res.status).toBe(400);
      expect(res.body.errors.code).toBe('VALIDATION');
      expect(res.body.errors.fields[0].message).toBe('허용되지 않는 필드');
      expect(res.body.errors.fields[0].path).toBe(Object.keys(extra)[0]);
    }
    const bad = await put(s, id, emptyState(0, { pos: { x: 'a', y: 1 }, facing: -1 }));
    expect(bad.status).toBe(400);
    const nested = await put(s, id, emptyState(0, { quests: [{ id: 'c1_morning', status: 0, step: 0, counts: [], reward: 1 }] }));
    expect(nested.status).toBe(400);
    const dupFlags = await put(s, id, emptyState(0, { story_flags: ['a', 'a'] }));
    expect(dupFlags.status).toBe(400);
    const get = await request(app).get(`/characters/${id}`).set(auth(s));
    expect(get.body.data.character.state.version).toBe(0);
  });

  it('404: 남의 캐릭터', async () => {
    const { id } = await fresh();
    const other = await registerAccount(app);
    const res = await put(other, id, emptyState(0));
    expect(res.status).toBe(404);
  });

  it('409 VERSION_CONFLICT: current_version 포함', async () => {
    const { s, id } = await fresh();
    expect((await put(s, id, emptyState(0))).status).toBe(200);
    const stale = await put(s, id, emptyState(0));
    expect(stale.status).toBe(409);
    expect(stale.body.errors).toMatchObject({ code: 'VERSION_CONFLICT', current_version: 1 });
  });

  it('동시 저장: 같은 version 2개 -> 하나만 성공, 하나는 409', async () => {
    const { s, id } = await fresh();
    const [a, b] = await Promise.all([put(s, id, emptyState(0, { facing: 1 })), put(s, id, emptyState(0, { facing: 2 }))]);
    expect([a.status, b.status].sort()).toEqual([200, 409]);
    const get = await request(app).get(`/characters/${id}`).set(auth(s));
    expect(get.body.data.character.state.version).toBe(1);
  });

  it('속도 제한: 캐릭터당 1초에 1회', async () => {
    const limited = buildApp({ RATE_STATE_SAVE_MAX: '1' });
    const s = await registerAccount(limited);
    const id = (await createChar(limited, s, randomName())).body.data.character.id;
    const p = (v: number) => request(limited).put(`/characters/${id}/state`).set(auth(s)).send(emptyState(v));
    expect((await p(0)).status).toBe(200);
    const second = await p(1);
    expect(second.status).toBe(429);
    expect(second.headers['retry-after']).toBeDefined();
    buildApp();
  });

  it('INVALID_MAP: 없는 맵, 던전 방', async () => {
    const { s, id } = await fresh();
    for (const map_id of ['nowhere', 'dgn_canyon_1']) {
      const res = await put(s, id, emptyState(0, { map_id }));
      expect(res.status).toBe(422);
      expect(res.body.errors.code).toBe('INVALID_MAP');
    }
  });

  it('INVALID_POSITION: 맵 범위 밖, facing 상한은 400', async () => {
    const { s, id } = await fresh();
    const out = await put(s, id, emptyState(0, { pos: { x: 9999, y: 1 } }));
    expect(out.status).toBe(422);
    expect(out.body.errors.code).toBe('INVALID_POSITION');
    const facing = await put(s, id, emptyState(0, { facing: 8 }));
    expect(facing.status).toBe(400);
    expect(facing.body.errors.fields[0].path).toBe('facing');
  });

  describe('패시브', () => {
    it('레벨 1이면 노드를 찍을 수 없다(OVER_POINTS)', async () => {
      const { s, id } = await fresh();
      const res = await put(s, id, emptyState(0, { passives: ['Dt0'] }));
      expect(res.status).toBe(422);
      expect(res.body.errors).toMatchObject({ code: 'INVALID_PASSIVES', reason: 'OVER_POINTS' });
    });

    it('시작 노드만 보내면 무시하고 통과, 없는 노드/중복', async () => {
      const { s, id } = await fresh();
      expect((await put(s, id, emptyState(0, { passives: ['S'] }))).status).toBe(200);
      const unknown = await put(s, id, emptyState(1, { passives: ['zzz'] }));
      expect(unknown.body.errors.reason).toBe('UNKNOWN_NODE');
      const dup = await put(s, id, emptyState(1, { passives: ['Dt0', 'Dt0'] }));
      expect(dup.body.errors.reason).toBe('DUPLICATE');
    });

    it('레벨이 충분하면 연결된 노드는 통과, 끊긴 노드는 NOT_CONNECTED', async () => {
      const { s, id } = await fresh();
      await getPool().query('UPDATE characters SET level = 10 WHERE uuid = $1', [id]);
      const ok = await put(s, id, emptyState(0, { passives: ['Dt0', 'Da1'] }));
      expect(ok.status).toBe(200);
      const get = await request(app).get(`/characters/${id}`).set(auth(s));
      expect(get.body.data.character.state.passives).toEqual(['Dt0', 'Da1']);
      const cut = await put(s, id, emptyState(1, { passives: ['Da1', 'DA'] }));
      expect(cut.status).toBe(422);
      expect(cut.body.errors.reason).toBe('NOT_CONNECTED');
    });
  });

  describe('보조 젬', () => {
    const gem = (slot: number, supports: (string | null)[]) => ({ skill_gems: [{ slot, supports }] });

    it('레벨 1: 슬롯 0은 아직 닫혀 있다(SLOT_LOCKED)', async () => {
      const { s, id } = await fresh();
      const res = await put(s, id, emptyState(0, gem(0, ['sup_dmg', null])));
      expect(res.status).toBe(422);
      expect(res.body.errors).toMatchObject({ code: 'INVALID_GEMS', reason: 'SLOT_LOCKED' });
      // 비어 있는 슬롯 항목은 통과
      expect((await put(s, id, emptyState(0, gem(0, [null, null])))).status).toBe(200);
    });

    it('종류별 사유', async () => {
      const { s, id } = await fresh();
      await getPool().query('UPDATE characters SET level = 6 WHERE uuid = $1', [id]);
      const reasonOf = async (version: number, g: object) => (await put(s, id, emptyState(version, g))).body.errors?.reason;
      expect(await reasonOf(0, gem(0, ['nope', null]))).toBe('UNKNOWN_GEM');
      expect(await reasonOf(0, gem(0, ['crush', null]))).toBe('NOT_SUPPORT');
      expect(await reasonOf(0, gem(0, ['sup_multi', null]))).toBe('LOCKED_GEM'); // 해금 레벨 10
      expect(await reasonOf(0, gem(0, ['sup_dmg', 'sup_dmg']))).toBe('DUPLICATE_IN_SLOT');
      expect(await reasonOf(0, gem(9, [null, null]))).toBe('BAD_SLOT');
      expect(await reasonOf(0, gem(0, ['sup_dmg']))).toBe('BAD_SLOT');
      expect(await reasonOf(0, { skill_gems: [{ slot: 0, supports: [null, null] }, { slot: 0, supports: [null, null] }] })).toBe('BAD_SLOT');
      const ok = await put(s, id, emptyState(0, { skill_gems: [{ slot: 0, supports: ['sup_dmg', 'sup_eff'] }, { slot: 1, supports: ['sup_dmg', null] }] }));
      expect(ok.status).toBe(200);
    });
  });

  describe('퀘스트·플래그', () => {
    it('사유별 422', async () => {
      const { s, id } = await fresh();
      const q = (over: object) => ({ id: 'c1_morning', status: 1, step: 1, counts: [0], ...over });
      const reasonOf = async (quests: object[], extra: object = {}) =>
        (await put(s, id, emptyState(0, { quests, ...extra }))).body.errors?.reason;
      expect(await reasonOf([q({ id: 'nope' })])).toBe('UNKNOWN_QUEST');
      expect(await reasonOf([q({ status: 5 })])).toBe('BAD_STATUS');
      expect(await reasonOf([q({ step: 99 })])).toBe('BAD_STEP');
      expect(await reasonOf([q({ counts: [0, 0, 0] })])).toBe('BAD_COUNTS');
      expect(await reasonOf([q({ counts: [-1] })])).toBe('BAD_COUNTS');
      expect(await reasonOf([q({}), q({})])).toBe('DUPLICATE');
      expect(await reasonOf([], { tracked_quest: 'nope' })).toBe('UNKNOWN_QUEST');
    });

    it('퇴보 금지: 완료한 퀘스트는 되돌릴 수 없다(스토리 플래그는 컷신이 지울 수 있다)', async () => {
      const { s, id } = await fresh();
      const done = emptyState(0, {
        quests: [{ id: 'c1_morning', status: 4, step: 4, counts: [] }],
        story_flags: ['f1'],
      });
      expect((await put(s, id, done)).status).toBe(200);
      const undo = await put(s, id, emptyState(1, { quests: [{ id: 'c1_morning', status: 1, step: 1, counts: [] }], story_flags: ['f1'] }));
      expect(undo.status).toBe(422);
      expect(undo.body.errors).toMatchObject({ code: 'INVALID_QUEST_STATE', reason: 'REGRESSION' });
      const removed = await put(s, id, emptyState(1, { quests: [], story_flags: ['f1'] }));
      expect(removed.body.errors.reason).toBe('REGRESSION');
      const cleared = await put(s, id, emptyState(1, { quests: done.quests, story_flags: [] }));
      expect(cleared.status).toBe(200);
      const more = await put(s, id, emptyState(2, { quests: done.quests, story_flags: ['f1', 'f2'] }));
      expect(more.status).toBe(200);
    });
  });
});
