// CLI 명령 정의(phase7_ops.md 5.9). 명령 하나는 관리자 API 호출 하나(또는 몇 개)에 대응한다.
import { randomUUID } from 'node:crypto';
import fs from 'node:fs';

export interface CmdCtx {
  /** 위치 인자(명령 이름 뒤) */
  args: string[];
  flags: Record<string, string | boolean | undefined>;
  /** 로그인한 세션으로 호출한다. 변경 POST는 request_id를 자동으로 붙인다 */
  get: (route: string, query?: Record<string, string | number | undefined>) => Promise<unknown>;
  post: (route: string, body?: Record<string, unknown>) => Promise<unknown>;
  prompt: (q: string, secret: boolean) => Promise<string>;
}

export interface Command {
  path: string[];
  usage: string;
  run: (c: CmdCtx) => Promise<unknown>;
}

const need = (v: string | undefined, name: string): string => {
  if (!v) throw new Error(`${name} 가 필요합니다`);
  return v;
};
const flag = (c: CmdCtx, k: string): string | undefined => {
  const v = c.flags[k];
  return typeof v === 'string' ? v : undefined;
};
const mustFlag = (c: CmdCtx, k: string): string => need(flag(c, k), `--${k}`);

/** "30m", "2h", "1d" -> 분 */
export function parseMinutes(s: string): number {
  const m = /^(\d+)(m|h|d)$/.exec(s);
  if (!m) throw new Error(`시간 형식은 30m, 2h, 1d 입니다: ${s}`);
  const n = Number(m[1]);
  return m[2] === 'm' ? n : m[2] === 'h' ? n * 60 : n * 1440;
}

/** "24h", "7d" -> ISO 시각(지금 기준 과거) */
const sinceIso = (s: string): string => new Date(Date.now() - parseMinutes(s) * 60_000).toISOString();

const q = (c: CmdCtx, keys: string[]): Record<string, string | undefined> => Object.fromEntries(keys.map((k) => [k, flag(c, k)]));

export const COMMANDS: Command[] = [
  { path: ['whoami'], usage: 'whoami', run: (c) => c.get('/admin/me') },
  { path: ['admins', 'list'], usage: 'admins list', run: (c) => c.get('/admin/admins') },
  {
    path: ['admins', 'add'],
    usage: 'admins add <login_id> --name <표시이름> --role viewer|operator|owner',
    run: (c) => c.post('/admin/admins', { login_id: need(c.args[0], 'login_id'), display_name: flag(c, 'name') ?? need(c.args[0], 'login_id'), role: mustFlag(c, 'role') }),
  },
  ...(['disable', 'enable', 'unlock', 'reset-2fa', 'reset-password'] as const).map((a): Command => ({
    path: ['admins', a],
    usage: `admins ${a} <admin_uuid>`,
    run: (c) => c.post(`/admin/admins/${need(c.args[0], 'admin_uuid')}/actions`, { action: a.replace('-', '_') }),
  })),
  {
    path: ['admins', 'role'],
    usage: 'admins role <admin_uuid> <viewer|operator|owner>',
    run: (c) => c.post(`/admin/admins/${need(c.args[0], 'admin_uuid')}/actions`, { action: 'set_role', role: need(c.args[1], 'role') }),
  },
  {
    path: ['audit'],
    usage: 'audit [--admin <uuid>] [--target <uuid>] [--action <이름>] [--since 24h] [--limit n]',
    run: (c) => c.get('/admin/audit', { ...q(c, ['admin', 'target', 'action', 'limit', 'before']), ...(flag(c, 'since') ? { since: sinceIso(mustFlag(c, 'since')) } : {}) }),
  },
  { path: ['account', 'find'], usage: 'account find <uuid | name:이름 | steam:ID | dev:아이디>', run: (c) => c.get('/admin/accounts', { q: need(c.args[0], 'q') }) },
  { path: ['account', 'show'], usage: 'account show <account_uuid>', run: (c) => c.get(`/admin/accounts/${need(c.args[0], 'uuid')}`) },
  { path: ['char', 'show'], usage: 'char show <character_uuid>', run: (c) => c.get(`/admin/characters/${need(c.args[0], 'uuid')}`) },
  {
    path: ['char', 'ledger'],
    usage: 'char ledger <character_uuid> [--kind gold|item|xp] [--since 24h] [--reason r] [--limit n]',
    run: (c) => c.get(`/admin/characters/${need(c.args[0], 'uuid')}/ledger`, { ...q(c, ['kind', 'reason', 'limit', 'before', 'request']), ...(flag(c, 'since') ? { since: sinceIso(mustFlag(c, 'since')) } : {}) }),
  },
  {
    path: ['account', 'note'],
    usage: 'account note <account_uuid> <메모>',
    run: (c) => c.post(`/admin/accounts/${need(c.args[0], 'uuid')}/notes`, { kind: 'note', note: c.args.slice(1).join(' ') }),
  },
  {
    path: ['account', 'ack'],
    usage: 'account ack <account_uuid> <메모>',
    run: (c) => c.post(`/admin/accounts/${need(c.args[0], 'uuid')}/notes`, { kind: 'review_ack', note: c.args.slice(1).join(' ') || '검토 완료' }),
  },
  { path: ['account', 'kick'], usage: 'account kick <account_uuid>', run: (c) => c.post(`/admin/accounts/${need(c.args[0], 'uuid')}/kick`) },
  { path: ['account', 'dev-create'], usage: 'account dev-create <login_id>', run: (c) => c.post('/admin/accounts/dev', { login_id: need(c.args[0], 'login_id') }) },
  { path: ['account', 'dev-reset'], usage: 'account dev-reset <account_uuid>', run: (c) => c.post(`/admin/accounts/${need(c.args[0], 'uuid')}/dev-password`) },
  {
    path: ['sanction', 'add'],
    usage: 'sanction add <account_uuid> --kind warning|chat_mute|ban --reason abuse|spam|scam_ad|cheat|other [--for 1h|1d|7d|30d|permanent] [--report <uuid>] --note <사유>',
    run: (c) =>
      c.post(`/admin/accounts/${need(c.args[0], 'uuid')}/sanctions`, {
        kind: mustFlag(c, 'kind'),
        reason_code: mustFlag(c, 'reason'),
        ...(flag(c, 'for') ? { duration: flag(c, 'for') } : {}),
        ...(flag(c, 'report') ? { report_id: flag(c, 'report') } : {}),
        note: mustFlag(c, 'note'),
      }),
  },
  { path: ['sanction', 'revoke'], usage: 'sanction revoke <sanction_uuid> --note <사유>', run: (c) => c.post(`/admin/sanctions/${need(c.args[0], 'uuid')}/revoke`, { note: mustFlag(c, 'note') }) },
  { path: ['report', 'list'], usage: 'report list [--state open|reviewing] [--reason r]', run: (c) => c.get('/admin/reports', q(c, ['state', 'reason'])) },
  { path: ['report', 'show'], usage: 'report show <report_uuid>', run: (c) => c.get(`/admin/reports/${need(c.args[0], 'uuid')}`) },
  { path: ['report', 'take'], usage: 'report take <report_uuid>', run: (c) => c.post(`/admin/reports/${need(c.args[0], 'uuid')}/take`) },
  {
    path: ['report', 'resolve'],
    usage: 'report resolve <report_uuid> (--dismiss | --sanction --kind k --reason r [--for d]) --note <사유> [--no-merge]',
    run: (c) =>
      c.post(`/admin/reports/${need(c.args[0], 'uuid')}/resolve`, {
        decision: c.flags.dismiss ? 'dismiss' : 'sanction',
        note: mustFlag(c, 'note'),
        ...(c.flags.dismiss ? {} : { sanction: { kind: mustFlag(c, 'kind'), reason_code: mustFlag(c, 'reason'), ...(flag(c, 'for') ? { duration: flag(c, 'for') } : {}) } }),
        close_similar: !c.flags['no-merge'],
      }),
  },
  { path: ['held', 'list'], usage: 'held list', run: (c) => c.get('/admin/held-runs') },
  { path: ['held', 'show'], usage: 'held show <run_uuid>', run: (c) => c.get(`/admin/held-runs/${need(c.args[0], 'uuid')}`) },
  { path: ['held', 'release'], usage: 'held release <run_uuid> --note <사유>', run: (c) => c.post(`/admin/held-runs/${need(c.args[0], 'uuid')}/release`, { note: mustFlag(c, 'note') }) },
  { path: ['held', 'reject'], usage: 'held reject <run_uuid> --note <사유>', run: (c) => c.post(`/admin/held-runs/${need(c.args[0], 'uuid')}/reject`, { note: mustFlag(c, 'note') }) },
  { path: ['watch', 'list'], usage: 'watch list', run: (c) => c.get('/admin/watchlist') },
  { path: ['anomalies'], usage: 'anomalies [--account uuid] [--kind k] [--min-severity n] [--since 24h]', run: (c) => c.get('/admin/anomalies', { ...q(c, ['account', 'kind', 'before', 'limit']), min_severity: flag(c, 'min-severity'), ...(flag(c, 'since') ? { since: sinceIso(mustFlag(c, 'since')) } : {}) }) },
  { path: ['flags'], usage: 'flags [--account uuid] [--kind k] [--since 24h]', run: (c) => c.get('/admin/auction-flags', { ...q(c, ['account', 'kind', 'before', 'limit']), ...(flag(c, 'since') ? { since: sinceIso(mustFlag(c, 'since')) } : {}) }) },
  { path: ['economy', 'daily'], usage: 'economy daily [--day YYYY-MM-DD]', run: (c) => c.get('/admin/economy/daily', q(c, ['day'])) },
  {
    path: ['grant', 'add'],
    usage: 'grant add <character_uuid> --code compensation|event|refund|notice [--gold n] [--item key --count n] --memo <사유>',
    run: (c) =>
      c.post('/admin/grants', {
        character_id: need(c.args[0], 'character_uuid'),
        system_code: mustFlag(c, 'code'),
        ...(flag(c, 'gold') ? { gold: Number(flag(c, 'gold')) } : {}),
        ...(flag(c, 'item') ? { item: { item_key: mustFlag(c, 'item'), count: Number(flag(c, 'count') ?? '1') } } : {}),
        memo: mustFlag(c, 'memo'),
      }),
  },
  { path: ['grant', 'list'], usage: 'grant list [--character uuid]', run: (c) => c.get('/admin/grants', q(c, ['character', 'before', 'limit'])) },
  // 9단계 재화 이상 정지(economy holds): 목록·상세·수동 정지·해제·회수. 회수 금액은 서버가 원장에서 계산한다
  { path: ['holds', 'list'], usage: 'holds list [--state shadow|active|released|clawed_back] [--kind velocity|auction|linked|manual] [--q 이름]', run: (c) => c.get('/admin/economy/holds', q(c, ['state', 'kind', 'q', 'cursor', 'limit'])) },
  { path: ['holds', 'show'], usage: 'holds show <hold_uuid>', run: (c) => c.get(`/admin/economy/holds/${need(c.args[0], 'hold_uuid')}`) },
  {
    path: ['holds', 'add'],
    usage: 'holds add (--character <uuid> | --account <uuid>) --note <사유>',
    run: (c) => c.post('/admin/economy/holds', { ...(flag(c, 'character') ? { character_id: flag(c, 'character') } : { account_id: mustFlag(c, 'account') }), note: mustFlag(c, 'note') }),
  },
  {
    path: ['holds', 'release'],
    usage: 'holds release <hold_uuid> --note <사유> [--linked]',
    run: (c) => c.post(`/admin/economy/holds/${need(c.args[0], 'hold_uuid')}/release`, { note: mustFlag(c, 'note'), ...(c.flags.linked ? { release_linked: true } : {}) }),
  },
  {
    path: ['holds', 'clawback'],
    usage: 'holds clawback <hold_uuid> --note <사유> [--no-gold] [--no-items] [--no-mail]   (owner)',
    run: (c) => c.post(`/admin/economy/holds/${need(c.args[0], 'hold_uuid')}/clawback`, { note: mustFlag(c, 'note'), gold: !c.flags['no-gold'], items: !c.flags['no-items'], void_mail: !c.flags['no-mail'] }),
  },
  { path: ['char', 'velocity'], usage: 'char velocity <character_uuid>', run: (c) => c.get(`/admin/characters/${need(c.args[0], 'uuid')}/velocity`) },
  { path: ['account', 'links'], usage: 'account links <account_uuid>', run: (c) => c.get(`/admin/accounts/${need(c.args[0], 'uuid')}/links`) },
  // 10단계 운영 우편 캠페인 MC1~MC6
  {
    path: ['campaign', 'create'],
    usage: 'campaign create --file <캠페인.json>   (title, body, category, delivery_unit, target, mail_days, starts_at, ends_at, cap_count, attachments, memo)',
    run: (c) => c.post('/admin/mail-campaigns', JSON.parse(fs.readFileSync(mustFlag(c, 'file'), 'utf8')) as Record<string, unknown>),
  },
  { path: ['campaign', 'list'], usage: 'campaign list [--status pending|active|ended|cancelled]', run: (c) => c.get('/admin/mail-campaigns', q(c, ['status', 'limit', 'before'])) },
  { path: ['campaign', 'show'], usage: 'campaign show <campaign_uuid>', run: (c) => c.get(`/admin/mail-campaigns/${need(c.args[0], 'campaign_uuid')}`) },
  { path: ['campaign', 'approve'], usage: 'campaign approve <campaign_uuid>   (작성자 외 owner)', run: (c) => c.post(`/admin/mail-campaigns/${need(c.args[0], 'campaign_uuid')}/approve`) },
  {
    path: ['campaign', 'cancel'],
    usage: 'campaign cancel <campaign_uuid> --reason <사유> [--revoke]',
    run: (c) => c.post(`/admin/mail-campaigns/${need(c.args[0], 'campaign_uuid')}/cancel`, { reason: mustFlag(c, 'reason'), revoke_unclaimed: c.flags.revoke === true }),
  },
  { path: ['campaign', 'deliveries'], usage: 'campaign deliveries <campaign_uuid> [--state claimed|open|expired|revoked]', run: (c) => c.get(`/admin/mail-campaigns/${need(c.args[0], 'campaign_uuid')}/deliveries`, q(c, ['state', 'limit', 'before'])) },
  // 11단계 결제 PA1~PA14(서버에 환불 API는 없다: 환불은 Steamworks 파트너 사이트에서 하고 서버가 감시로 감지한다)
  { path: ['pay', 'order', 'list'], usage: 'pay order list [--state s] [--account uuid] [--review true|false]', run: (c) => c.get('/admin/payments/orders', { ...q(c, ['state', 'account', 'from', 'to', 'cursor', 'limit']), needs_review: flag(c, 'review') }) },
  { path: ['pay', 'order', 'show'], usage: 'pay order show <order_uuid>', run: (c) => c.get(`/admin/payments/orders/${need(c.args[0], 'order_uuid')}`) },
  { path: ['pay', 'order', 'recheck'], usage: 'pay order recheck <order_uuid>', run: (c) => c.post(`/admin/payments/orders/${need(c.args[0], 'order_uuid')}/recheck`) },
  { path: ['pay', 'account', 'show'], usage: 'pay account show <account_uuid>', run: (c) => c.get(`/admin/payments/accounts/${need(c.args[0], 'account_uuid')}`) },
  {
    path: ['pay', 'account', 'block'],
    usage: 'pay account block <account_uuid> --note <사유> [--reason manual|fraud_suspect]',
    run: (c) => c.post(`/admin/payments/accounts/${need(c.args[0], 'account_uuid')}/block`, { note: mustFlag(c, 'note'), reason: flag(c, 'reason') ?? 'manual' }),
  },
  { path: ['pay', 'account', 'unblock'], usage: 'pay account unblock <account_uuid> --note <근거>   (owner)', run: (c) => c.post(`/admin/payments/accounts/${need(c.args[0], 'account_uuid')}/unblock`, { note: mustFlag(c, 'note') }) },
  { path: ['pay', 'flag', 'list'], usage: 'pay flag list [--state open|confirmed|dismissed] [--account uuid]', run: (c) => c.get('/admin/payments/flags', q(c, ['state', 'account', 'cursor', 'limit'])) },
  {
    path: ['pay', 'flag', 'resolve'],
    usage: 'pay flag resolve <flag_uuid> --as confirmed|dismissed --note <메모>',
    run: (c) => c.post(`/admin/payments/flags/${need(c.args[0], 'flag_uuid')}/resolve`, { resolution: mustFlag(c, 'as'), note: mustFlag(c, 'note') }),
  },
  {
    path: ['stars', 'grant', 'create'],
    usage: 'stars grant create <account_uuid> (--stars n | --forgive-debt) [--order uuid] --memo <사유>   (승인 대기, 별조각은 승인 전에 움직이지 않는다)',
    run: (c) =>
      c.post('/admin/payments/star-grants', {
        kind: c.flags['forgive-debt'] ? 'debt_forgive' : 'grant',
        account_id: need(c.args[0], 'account_uuid'),
        ...(c.flags['forgive-debt'] ? {} : { stars: Number(mustFlag(c, 'stars')) }),
        ...(flag(c, 'order') ? { related_order_id: flag(c, 'order') } : {}),
        memo: mustFlag(c, 'memo'),
      }),
  },
  { path: ['stars', 'grant', 'list'], usage: 'stars grant list [--state pending|applied|cancelled|expired] [--account uuid]', run: (c) => c.get('/admin/payments/star-grants', q(c, ['state', 'account', 'cursor', 'limit'])) },
  { path: ['stars', 'grant', 'approve'], usage: 'stars grant approve <grant_uuid>   (작성자 외 owner)', run: (c) => c.post(`/admin/payments/star-grants/${need(c.args[0], 'grant_uuid')}/approve`) },
  { path: ['stars', 'grant', 'cancel'], usage: 'stars grant cancel <grant_uuid>', run: (c) => c.post(`/admin/payments/star-grants/${need(c.args[0], 'grant_uuid')}/cancel`) },
  {
    path: ['pay', 'revoke'],
    usage: 'pay revoke <order_uuid> --note <사유> [--apply <preview_hash>] [--no-cosmetics] [--no-gear] [--no-gauge]   (owner, 먼저 미리보기)',
    run: (c) =>
      c.post(`/admin/payments/orders/${need(c.args[0], 'order_uuid')}/revoke-outcomes`, {
        mode: flag(c, 'apply') ? 'apply' : 'preview',
        include: { cosmetics: !c.flags['no-cosmetics'], gear: !c.flags['no-gear'], gauge: !c.flags['no-gauge'] },
        ...(flag(c, 'apply') ? { preview_hash: flag(c, 'apply') } : {}),
        note: mustFlag(c, 'note'),
      }),
  },
  { path: ['pay', 'report'], usage: 'pay report   (대사 현황 + payment-report 수동 실행은 ops run payment-report)', run: (c) => c.get('/admin/payments/reconcile') },
  { path: ['maint', 'status'], usage: 'maint status', run: (c) => c.get('/admin/maintenance') },
  {
    path: ['maint', 'schedule'],
    usage: 'maint schedule (--in 30m | --at <ISO>) --duration 20m [--notice 문구]',
    run: (c) =>
      c.post('/admin/maintenance', {
        ...(flag(c, 'in') ? { start_in_minutes: parseMinutes(mustFlag(c, 'in')) } : { starts_at: mustFlag(c, 'at') }),
        duration_minutes: parseMinutes(mustFlag(c, 'duration')),
        ...(flag(c, 'notice') ? { notice: flag(c, 'notice') } : {}),
      }),
  },
  { path: ['maint', 'cancel'], usage: 'maint cancel <window_uuid>', run: (c) => c.post(`/admin/maintenance/${need(c.args[0], 'uuid')}/cancel`) },
  { path: ['maint', 'extend'], usage: 'maint extend <window_uuid> --by 30m', run: (c) => c.post(`/admin/maintenance/${need(c.args[0], 'uuid')}/extend`, { extend_minutes: parseMinutes(mustFlag(c, 'by')) }) },
  { path: ['maint', 'end'], usage: 'maint end <window_uuid>', run: (c) => c.post(`/admin/maintenance/${need(c.args[0], 'uuid')}/end`) },
  { path: ['maint', 'drain'], usage: 'maint drain', run: (c) => c.get('/admin/maintenance/drain') },
  { path: ['broadcast'], usage: 'broadcast <문구>', run: (c) => c.post('/admin/broadcast', { text: c.args.join(' ') }) },
  { path: ['ops', 'status'], usage: 'ops status', run: (c) => c.get('/admin/ops/status') },
  { path: ['ops', 'jobs'], usage: 'ops jobs', run: (c) => c.get('/admin/ops/jobs') },
  { path: ['ops', 'run'], usage: 'ops run <purge-hourly|purge-daily|stale-runs|integrity-nightly|payment-report|...> [--full]', run: (c) => c.post(`/admin/ops/jobs/${need(c.args[0], 'job')}/run`, c.flags.full ? { full: true } : {}) },
];

export const newRequestId = (): string => randomUUID();
