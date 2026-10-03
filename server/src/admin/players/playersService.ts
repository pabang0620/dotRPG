// PL1~PL8: 계정·캐릭터 조회, 원장 조회, 메모, 접속 끊기, 개발용 계정 발급
import { getConfig } from '../../config/env';
import { afterCommit, getPool, isUniqueViolation } from '../../db/pool';
import { CLOSE } from '../../domains/chat/wsProtocol';
import { kickAccount, registry } from '../../domains/chat/realtimeNotifier';
import { insertAccount, insertDevIdentity } from '../../domains/auth/authRepository';
import { AppError } from '../../utils/AppError';
import { hashPassword, newTempPassword } from '../auth/adminAuthService';
import { auditView, runAdminAction, type ActionResult } from '../common/audit';
import type { AdminCtx } from '../common/adminTypes';
import * as watch from '../watch/watchRepository';
import * as repo from './playersRepository';
import type { DevCreateBody, LedgerQuery, NoteBody } from './playersValidation';

const iso = (d: Date | null): string | null => (d ? d.toISOString() : null);
const FAR = new Date('9999-01-01T00:00:00Z').getTime();

const accountBrief = async (a: repo.AccountBrief) => {
  const db = getPool();
  const [ids, chars] = await Promise.all([repo.identitiesOf(db, a.id), repo.charactersOf(db, a.id)]);
  return {
    id: a.uuid,
    created_at: a.created_at.toISOString(),
    last_login_at: iso(a.last_login_at),
    banned_until: iso(a.banned_until),
    deleted: a.deleted_at !== null,
    identities: ids.map((i) => ({ provider: i.provider, subject: i.subject })),
    characters: chars.map((c) => ({ id: c.uuid, name: c.name, class: c.class, level: c.level, deleted: c.deleted_at !== null })),
  };
};

/** PL1: `uuid`(계정 또는 캐릭터), `name:캐릭터이름`, `steam:<steam_id64>`, `dev:<아이디>` 한 가지 형식만 받는다 */
export async function findAccounts(q: string) {
  const db = getPool();
  let rows: repo.AccountBrief[];
  const t = q.trim();
  if (/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(t)) rows = await repo.findByAccountOrCharacterUuid(db, t.toLowerCase());
  else if (t.startsWith('name:')) rows = await repo.findByCharacterName(db, t.slice(5));
  else if (t.startsWith('steam:')) rows = await repo.findByIdentity(db, 'steam', t.slice(6));
  else if (t.startsWith('dev:')) rows = await repo.findByIdentity(db, 'dev', t.slice(4).toLowerCase());
  else throw new AppError(422, 'q 형식은 uuid, name:이름, steam:ID, dev:아이디 중 하나입니다.', 'BAD_QUERY');
  return { accounts: await Promise.all(rows.map(accountBrief)) };
}

/** PL2 계정 상세(요약 판). 누가 봤는지 account.view로 남긴다 */
export async function accountDetail(admin: AdminCtx, ip: string, uuid: string) {
  const db = getPool();
  const a = await repo.accountByUuid(db, uuid);
  if (!a) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
  const cfg = getConfig().admin;
  const [brief, sanctions, reports, anomalies, flags, held, trades, notes, actions, score] = await Promise.all([
    accountBrief(a),
    repo.sanctionsOf(db, a.id),
    repo.reportSummary(db, a.id),
    repo.anomalySummary(db, a.id),
    repo.flagSummary(db, a.id),
    repo.heldRunCount(db, a.id),
    repo.tradeSummary(db, a.id),
    repo.notesOf(db, a.id),
    repo.recentAdminActions(db, uuid),
    watch.watchlist(db, cfg.watchlistWindowHours, 1, 1, a.id),
  ]);
  await auditView(admin, ip, 'account.view', 'account', uuid);
  const total = Number(reports.by.total);
  return {
    account: { ...brief, banned: a.banned_until !== null && a.banned_until.getTime() > Date.now(), permanent_ban: a.banned_until !== null && a.banned_until.getTime() > FAR },
    sanctions: {
      active: sanctions.filter((s) => s.active).map(sanctionView),
      recent: sanctions.map(sanctionView),
    },
    reports_against: { by_state: Object.fromEntries(reports.against.map((r) => [r.state, Number(r.n)])), recent: reports.recent.map((r) => ({ id: r.uuid, reason: r.reason, state: r.state, at: r.created_at.toISOString() })) },
    reports_made: { total, dismissed_ratio: total === 0 ? null : Number(reports.by.dismissed) / total },
    anomalies: anomalies.map((r) => ({ kind: r.kind, last_24h: Number(r.h24), last_7d: Number(r.d7), max_severity: r.maxsev })),
    auction_flags_7d: flags.map((r) => ({ kind: r.kind, count: Number(r.n) })),
    held_runs: held,
    trades_7d: { count: Number(trades.n), gold: Number(trades.gold), partner_accounts: Number(trades.partners) },
    notes: notes.map((n) => ({ id: n.uuid, kind: n.kind, note: n.note, by: n.admin, at: n.created_at.toISOString() })),
    admin_actions: actions.map((x) => ({ action: x.action, result: x.result, by: x.admin, at: x.created_at.toISOString() })),
    watch_score: score[0]?.score ?? 0,
  };
}

const sanctionView = (s: Awaited<ReturnType<typeof repo.sanctionsOf>>[number]) => ({
  id: s.uuid,
  kind: s.kind,
  source: s.source,
  reason_code: s.reason_code,
  starts_at: s.starts_at.toISOString(),
  ends_at: iso(s.ends_at),
  by: s.created_by,
  revoked: s.revoked_at !== null,
  note: s.note,
});

/** PL3 */
export async function characterDetail(admin: AdminCtx, ip: string, uuid: string) {
  const db = getPool();
  const c = await repo.characterByUuid(db, uuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const [items, counters] = await Promise.all([repo.itemsOf(db, c.id), repo.characterCounters(db, c.id)]);
  await auditView(admin, ip, 'character.view', 'character', uuid);
  return {
    character: {
      id: c.uuid,
      account_id: c.account_uuid,
      name: c.name,
      class: c.class,
      level: c.level,
      xp: c.xp,
      gold: c.gold,
      created_at: c.created_at.toISOString(),
      deleted: c.deleted_at !== null,
    },
    items: items.map((i) => ({ location: i.location, item_key: i.item_key, bind: i.bind, count: i.count })),
    counters: {
      dungeon_cleared: Number(counters.cleared),
      dungeon_held: Number(counters.held),
      open_mails: Number(counters.open_mails),
      active_listings: Number(counters.listings),
    },
  };
}

const encCursor = (id: string): string => Buffer.from(id).toString('base64url');
const decCursor = (c: string): number | null => {
  const n = Number(Buffer.from(c, 'base64url').toString());
  return Number.isSafeInteger(n) && n > 0 ? n : null;
};

/** PL4: 원장 조회(키셋 페이지). 커서는 불투명 문자열이다 */
export async function ledger(admin: AdminCtx, ip: string, uuid: string, q: LedgerQuery) {
  const db = getPool();
  const c = await repo.characterByUuid(db, uuid);
  if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
  const before = q.before ? decCursor(q.before) : null;
  if (q.before && before === null) throw new AppError(422, '커서가 올바르지 않습니다.', 'BAD_CURSOR');
  const rows = await repo.ledgerRows(db, q.kind, c.id, { reason: q.reason ?? null, request: q.request ?? null, since: q.since ?? null, before, limit: q.limit });
  await auditView(admin, ip, 'ledger.view', 'character', uuid, { kind: q.kind, ...(q.reason ? { reason: q.reason } : {}) });
  const page = rows.slice(0, q.limit);
  const last = page[page.length - 1];
  return {
    data: {
      kind: q.kind,
      lines: page.map((r) => ({
        at: r.created_at.toISOString(),
        delta: Number(r.delta),
        balance_after: r.balance_after === null ? null : Number(r.balance_after),
        reason: r.reason,
        ref: r.ref,
        request_id: r.request_id,
        ...(r.item_key ? { item_key: r.item_key, location: r.location } : {}),
        ...(r.level_after !== null ? { level_after: r.level_after } : {}),
      })),
    },
    next_before: rows.length > q.limit && last ? encCursor(last.id) : null,
  };
}

/** PL5: 메모 / 검토 확인(review_ack). 본문은 감사 params에 길이만 남긴다 */
export function addNote(admin: AdminCtx, ip: string, uuid: string, body: NoteBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: body.kind === 'review_ack' ? 'account.review_ack' : 'account.note',
    targetType: 'account',
    targetUuid: uuid,
    requestId: body.request_id,
    params: { kind: body.kind, note_length: body.note.length },
    handler: async (client) => {
      const a = await repo.accountByUuid(client, uuid);
      if (!a) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
      const id = await repo.insertNote(client, a.id, admin.id, body.kind, body.note);
      return { status: 201, data: { note: { id, kind: body.kind } } };
    },
  });
}

/** PL6: 그 계정의 WebSocket을 끊는다(bye 4011 KICKED, reconnect:true). 접속을 막지는 않는다 */
export function kick(admin: AdminCtx, ip: string, uuid: string, requestId: string): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'account.kick',
    targetType: 'account',
    targetUuid: uuid,
    requestId,
    params: {},
    handler: async (client) => {
      const a = await repo.accountByUuid(client, uuid);
      if (!a) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
      const online = registry.ofAccount(a.id) !== undefined;
      if (online) afterCommit(client, () => void kickAccount(a.id, CLOSE.KICKED, 'KICKED'));
      return { status: 200, data: { kicked: online } };
    },
  });
}

/** PL7: 개발용 계정 발급. 임시 비밀번호는 응답에 한 번만 나가고 감사 로그에는 남지 않는다 */
export function createDevAccount(admin: AdminCtx, ip: string, body: DevCreateBody): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'account.dev_create',
    targetType: 'account',
    requestId: body.request_id,
    params: { login_id: body.login_id },
    handler: async (client) => {
      if (!getConfig().authDevEnabled) throw new AppError(409, '개발용 로그인이 꺼져 있습니다.', 'DEV_AUTH_DISABLED');
      const temp = newTempPassword();
      try {
        const acct = await insertAccount(client);
        await insertDevIdentity(client, acct.id, body.login_id, await hashPassword(temp));
        return {
          status: 201,
          data: { account: { id: acct.uuid, login_id: body.login_id }, temp_password: temp, note: '비밀번호는 지금 한 번만 표시됩니다.' },
          stored: { account: { id: acct.uuid, login_id: body.login_id } },
          targetUuid: acct.uuid,
        };
      } catch (err) {
        if (isUniqueViolation(err)) throw new AppError(409, '이미 사용 중인 아이디입니다.', 'LOGIN_ID_TAKEN');
        throw err;
      }
    },
  });
}

/** PL8 */
export function resetDevPassword(admin: AdminCtx, ip: string, uuid: string, requestId: string): Promise<ActionResult> {
  return runAdminAction({
    admin,
    ip,
    action: 'account.dev_reset',
    targetType: 'account',
    targetUuid: uuid,
    requestId,
    params: {},
    handler: async (client) => {
      if (!getConfig().authDevEnabled) throw new AppError(409, '개발용 로그인이 꺼져 있습니다.', 'DEV_AUTH_DISABLED');
      const a = await repo.accountByUuidForUpdate(client, uuid);
      if (!a) throw new AppError(404, '계정을 찾을 수 없습니다.', 'ACCOUNT_NOT_FOUND');
      const dev = await repo.devIdentityOf(client, a.id);
      if (!dev) throw new AppError(409, '개발용 로그인이 없는 계정입니다.', 'NO_DEV_IDENTITY');
      const temp = newTempPassword();
      await repo.setDevSecret(client, a.id, await hashPassword(temp));
      return {
        status: 200,
        data: { account: { id: uuid, login_id: dev.subject }, temp_password: temp, note: '비밀번호는 지금 한 번만 표시됩니다.' },
        stored: { account: { id: uuid, login_id: dev.subject } },
      };
    },
  });
}
