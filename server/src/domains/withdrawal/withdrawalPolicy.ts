// 정책 레지스트리(설계 8.2의 4번, 10절): 표마다 "익명화 때(T1)"와 "보관 뒤(T2)" 처리를 한 곳에 적는다.
// 새 표를 만들면 여기에 분류하지 않는 한 test/withdrawalPolicy.test.ts(T-W30)가 실패한다.
// SQL 자리표시: :acc = 이 계정의 id 배열(bigint[]), :chr = 이 계정의 모든 캐릭터 id 배열(삭제된 것 포함, bigint[]). bindIds()가 $n 으로 바꾼다.
// 파기 순서는 DESTROY_ORDER 가 정한다(자식 -> 부모).

/** 익명화 때: delete 지운다 / anonymize 값을 교체한다 / keep 건드리지 않는다(원장·결제·진행 상태) */
export type T1Action = 'delete' | 'anonymize' | 'keep';

/**
 * 보관 뒤:
 *  destroy        이 계정 한 곳에 속한 행. retain_until 이 지나면 계정 단위로 파기
 *  destroy_shared 두 계정이 함께 가진 행. 모든 당사자가 파기 대상일 때만 파기(8.2의 2번)
 *  schedule       기존 정리 일정이 지운다(탈퇴가 앞당기지 않는다)
 *  forever        지우지 않는다(감사 근거·계정 무관 표)
 *  shell          accounts / characters 껍데기. 어떤 행도 참조하지 않을 때만 삭제
 */
export type T2Action = 'destroy' | 'destroy_shared' | 'schedule' | 'forever' | 'shell';

export interface PolicyEntry {
  table: string;
  t1: T1Action;
  /** 익명화 때 실행할 SQL(순서대로). t1 이 keep 이면 없다 */
  t1Sql?: string[];
  t2: T2Action;
  /** DELETE ... WHERE 뒤에 붙는 조건. 있으면 파기 대상 표다 */
  destroyWhere?: string;
  /** 분류 메모(감사·문서 대조용) */
  note?: string;
}

/** 파기 대상 계정: 익명화됐고 아직 보관 기한이 남은 탈퇴 행이 없는 계정 */
export const DESTROYABLE_ACCOUNTS = `(SELECT a.id FROM accounts a WHERE a.anonymized_at IS NOT NULL AND NOT EXISTS (
  SELECT 1 FROM account_withdrawals w WHERE w.account_id = a.id AND (w.state <> 'completed' OR w.retain_until > now())))`;
export const DESTROYABLE_CHARACTERS = `(SELECT c.id FROM characters c WHERE c.account_id IN ${DESTROYABLE_ACCOUNTS})`;

const ACC = '= ANY(:acc)';
const CHR = '= ANY(:chr)';
const inA = (col: string): string => `${col} IN ${DESTROYABLE_ACCOUNTS}`;
const inC = (col: string): string => `${col} IN ${DESTROYABLE_CHARACTERS}`;

const keepOwn = (table: string, destroyWhere: string, note?: string): PolicyEntry => ({
  table,
  t1: 'keep',
  t2: 'destroy',
  destroyWhere,
  ...(note ? { note } : {}),
});
const delT1 = (table: string, sql: string, t2: T2Action = 'schedule', note?: string): PolicyEntry => ({
  table,
  t1: 'delete',
  t1Sql: [sql],
  t2,
  ...(note ? { note } : {}),
});
const forever = (table: string, note?: string): PolicyEntry => ({ table, t1: 'keep', t2: 'forever', ...(note ? { note } : {}) });
const sched = (table: string, note?: string): PolicyEntry => ({ table, t1: 'keep', t2: 'schedule', ...(note ? { note } : {}) });

export const POLICY: PolicyEntry[] = [
  // ---------- 10.1 계정·인증·접속: 익명화 때 지운다 ----------
  delT1('auth_identities', `DELETE FROM auth_identities WHERE account_id ${ACC}`),
  delT1('refresh_tokens', `DELETE FROM refresh_tokens WHERE account_id ${ACC}`),
  delT1('login_events', `DELETE FROM login_events WHERE account_id ${ACC}`),
  delT1('account_devices', `DELETE FROM account_devices WHERE account_id ${ACC}`),
  delT1('account_ips', `DELETE FROM account_ips WHERE account_id ${ACC}`),
  delT1('online_sessions', `DELETE FROM online_sessions WHERE account_id ${ACC}`),
  delT1('play_time_hourly', `DELETE FROM play_time_hourly WHERE character_id ${CHR}`),
  delT1('income_hourly', `DELETE FROM income_hourly WHERE character_id ${CHR}`),
  delT1('request_log', `DELETE FROM request_log WHERE account_id ${ACC}`),
  delT1('chat_messages', `DELETE FROM chat_messages WHERE sender_account_id ${ACC} OR recipient_account_id ${ACC}`),
  // 이상 기록: IP·기기가 들어갈 수 있는 두 종류만 지운다. 나머지는 조사 근거로 보관
  {
    table: 'anomaly_log',
    t1: 'delete',
    t1Sql: [`DELETE FROM anomaly_log WHERE kind IN ('ip_cluster', 'device_limit') AND (account_id ${ACC} OR character_id ${CHR})`],
    t2: 'destroy',
    destroyWhere: `account_id ${ACC} OR character_id ${CHR}`,
    note: '심각도별 기존 일정(30/180일)도 계속 돈다',
  },
  // ---------- 소셜: 이름 스냅샷 익명화 ----------
  {
    table: 'blocks',
    t1: 'anonymize',
    t1Sql: [
      `DELETE FROM blocks WHERE blocker_account_id ${ACC}`,
      `UPDATE blocks b SET blocked_name = c.name FROM characters c WHERE c.id = b.blocked_character_id AND b.blocked_account_id ${ACC}`,
    ],
    t2: 'destroy',
    destroyWhere: `blocked_account_id ${ACC} OR blocker_account_id ${ACC}`,
    note: '나를 차단한 행은 이름만 자리표시로, 내가 건 행은 지운다. 해제 행은 기존 90일 일정',
  },
  {
    table: 'reports',
    t1: 'anonymize',
    t1Sql: [`UPDATE reports r SET target_name = c.name FROM characters c WHERE c.id = r.target_character_id AND r.target_account_id ${ACC}`],
    t2: 'destroy_shared',
    destroyWhere: `(reporter_account_id ${ACC} OR target_account_id ${ACC}) AND ${inA('reporter_account_id')} AND ${inA('target_account_id')}`,
    note: '열린 신고가 있으면 익명화 자체를 보류한다',
  },
  {
    table: 'report_lines',
    t1: 'keep',
    t2: 'destroy_shared',
    destroyWhere: `report_id IN (SELECT id FROM reports WHERE (reporter_account_id ${ACC} OR target_account_id ${ACC}) AND ${inA('reporter_account_id')} AND ${inA('target_account_id')})`,
    note: '수정 불가 트리거. 종결 후 180일 기존 일정이 지운다(탈퇴가 앞당기지 않는다)',
  },
  {
    table: 'party_run_members',
    t1: 'anonymize',
    t1Sql: [`UPDATE party_run_members SET device_hash = NULL, steam_key = NULL, install_id = NULL WHERE account_id ${ACC}`],
    t2: 'destroy',
    destroyWhere: `account_id ${ACC}`,
  },
  // ---------- 10.2 캐릭터·진행·재화: 보관 후 파기 (자식 -> 부모 순서) ----------
  keepOwn('drops', `character_id ${CHR}`, '기존 일정(1일)'),
  keepOwn('kill_log', `character_id ${CHR}`, '기존 일정(7일)'),
  keepOwn('kill_stats', `character_id ${CHR}`),
  keepOwn('raid_claims', `character_id ${CHR}`),
  keepOwn('dungeon_runs', `character_id ${CHR}`),
  keepOwn('dungeon_sweeps', `character_id ${CHR}`),
  keepOwn('sweep_ticket_ledger', `account_id ${ACC} OR character_id ${CHR}`),
  keepOwn('sweep_ticket_lots', `account_id ${ACC}`),
  keepOwn('revive_log', `character_id ${CHR}`),
  keepOwn('character_achievements', `character_id ${CHR}`),
  keepOwn('character_career', `character_id ${CHR}`),
  keepOwn('character_career_trials', `character_id ${CHR}`),
  keepOwn('character_chests', `character_id ${CHR}`),
  keepOwn('character_enhance_pity', `character_id ${CHR}`),
  keepOwn('character_node_state', `character_id ${CHR}`),
  keepOwn('character_state', `character_id ${CHR}`),
  keepOwn('quest_claims', `character_id ${CHR}`),
  keepOwn('daily_quests', `character_id ${CHR}`),
  keepOwn('site_deliveries', `character_id ${CHR}`),
  keepOwn('character_items', `character_id ${CHR}`, '원장 보존식 I2 때문에 원장과 함께 파기'),
  keepOwn('gold_ledger', `character_id ${CHR}`),
  keepOwn('item_ledger', `character_id ${CHR}`),
  keepOwn('xp_ledger', `character_id ${CHR}`),
  keepOwn('enhance_log', `character_id ${CHR}`),
  keepOwn('account_level_rewards', `account_id ${ACC}`),
  keepOwn('account_pass_claims', `account_id ${ACC}`),
  keepOwn('sealed_pulls', `account_id ${ACC}`),
  keepOwn('raid_shop_purchases', `account_id ${ACC}`),
  keepOwn('gacha_pulls', `account_id ${ACC}`),
  keepOwn('star_synth_log', `account_id ${ACC}`),
  keepOwn('account_collections', `account_id ${ACC}`),
  keepOwn('account_cosmetics', `account_id ${ACC}`),
  keepOwn('account_growth_pass', `account_id ${ACC}`),
  keepOwn('account_sealed_state', `account_id ${ACC}`),
  keepOwn('account_week_counters', `account_id ${ACC}`, '기존 일정(60일)도 계속 돈다'),
  // ---------- 10.4 제재·운영 메모 ----------
  keepOwn('economy_holds', `account_id ${ACC} OR character_id ${CHR}`),
  keepOwn('account_sanctions', `account_id ${ACC}`, '조사 근거. 신고(reports)보다 먼저 지운다(report_id FK)'),
  keepOwn('admin_account_notes', `account_id ${ACC}`),
  keepOwn('admin_grants', `character_id ${CHR}`),
  forever('held_run_reviews', '운영 검토 메모. 허용 표 밖이라 파기하지 않는다(해당 판은 껍데기를 남긴다)'),
  forever('admin_audit_log', '감사 근거. 계정 uuid 외 식별 정보 없음'),
  forever('admin_users'),
  forever('admin_sessions'),
  forever('job_runs'),
  forever('maintenance_windows'),
  forever('schema_migrations'),
  // ---------- 10.5 경매·우편 ----------
  keepOwn('auction_flags', `account_id ${ACC} OR character_id ${CHR}`),
  keepOwn('auction_sinks', `character_id ${CHR}`),
  keepOwn('mail_attachments', `mail_id IN (SELECT id FROM mails WHERE character_id ${CHR})`),
  keepOwn('mail_campaign_deliveries', `account_id ${ACC} OR character_id ${CHR}`),
  keepOwn('mails', `character_id ${CHR}`),
  {
    table: 'auction_trade_flags',
    t1: 'keep',
    t2: 'destroy_shared',
    destroyWhere: `trade_id IN (SELECT id FROM auction_trades WHERE (buyer_account_id ${ACC} OR seller_account_id ${ACC}) AND ${inA('buyer_account_id')} AND ${inA('seller_account_id')})`,
  },
  {
    table: 'auction_trades',
    t1: 'keep',
    t2: 'destroy_shared',
    destroyWhere: `(buyer_account_id ${ACC} OR seller_account_id ${ACC}) AND ${inA('buyer_account_id')} AND ${inA('seller_account_id')}`,
  },
  {
    table: 'auction_bids',
    t1: 'keep',
    t2: 'destroy_shared',
    destroyWhere: `bidder_account_id ${ACC} AND listing_id IN (SELECT id FROM auction_listings WHERE ${inA('seller_account_id')})`,
  },
  {
    table: 'auction_listings',
    t1: 'keep',
    t2: 'destroy_shared',
    destroyWhere: `(seller_account_id ${ACC} OR current_bidder_account_id ${ACC}) AND ${inA('seller_account_id')} AND (current_bidder_account_id IS NULL OR ${inA('current_bidder_account_id')})`,
  },
  forever('auction_price_daily', '시세 집계(계정 무관)'),
  // ---------- 소셜·파티 ----------
  {
    table: 'friendships',
    t1: 'keep',
    t2: 'destroy_shared',
    destroyWhere: `(requester_account_id ${ACC} OR target_account_id ${ACC}) AND ${inA('requester_account_id')} AND ${inA('target_account_id')}`,
    note: 'T0에서 removed/cancelled 처리. 끝난 행은 기존 90일 일정',
  },
  keepOwn('party_applications', `account_id ${ACC}`, '기존 일정(30일)'),
  keepOwn('party_members', `account_id ${ACC}`, '기존 일정(30일)'),
  {
    table: 'party_invites',
    t1: 'keep',
    t2: 'destroy_shared',
    destroyWhere: `(inviter_character_id ${CHR} OR invitee_character_id ${CHR}) AND ${inC('inviter_character_id')} AND ${inC('invitee_character_id')}`,
    note: 'T0에서 대기 건 cancelled. 기존 일정(7일)',
  },
  keepOwn('field_session_members', `account_id ${ACC}`, '종료 후 기존 일정(30일)'),
  { table: 'party_run_host_reports', t1: 'keep', t2: 'destroy_shared', destroyWhere: `host_character_id ${CHR}` },
  { table: 'party_runs', t1: 'keep', t2: 'destroy_shared', destroyWhere: `host_character_id ${CHR}`, note: '남은 멤버·자식 행이 있으면 FK로 막혀 껍데기를 남긴다' },
  { table: 'field_sessions', t1: 'keep', t2: 'destroy_shared', destroyWhere: `host_character_id ${CHR}` },
  { table: 'parties', t1: 'keep', t2: 'destroy_shared', destroyWhere: `leader_character_id ${CHR}` },
  sched('relay_room_stats', '계정 무관(방 단위 집계). 기존 90일 일정'),
  // ---------- 10.3 결제 (자식 -> 부모) ----------
  keepOwn('star_spend_allocs', `ledger_id IN (SELECT id FROM star_ledger WHERE account_id ${ACC}) OR order_id IN (SELECT id FROM star_orders WHERE account_id ${ACC})`),
  keepOwn('star_ledger', `account_id ${ACC}`),
  keepOwn('star_paid_lots', `account_id ${ACC}`),
  keepOwn('star_order_events', `order_id IN (SELECT id FROM star_orders WHERE account_id ${ACC})`),
  keepOwn('payment_flags', `account_id ${ACC}`),
  keepOwn('star_admin_grants', `account_id ${ACC}`),
  keepOwn('star_orders', `account_id ${ACC}`, 'steam_id 는 결제 기록으로 보관. ip/device_hash 는 기존 180일 일정'),
  keepOwn('star_wallets', `account_id ${ACC}`),
  keepOwn('payment_profiles', `account_id ${ACC}`),
  forever('star_rates_snapshots', '확률표 스냅샷(계정 무관)'),
  forever('mail_campaigns', '운영 캠페인(계정 무관)'),
  forever('mail_campaign_attachments', '운영 캠페인(계정 무관)'),
  // ---------- 이 기능의 표 ----------
  { table: 'account_withdrawals', t1: 'keep', t2: 'shell', note: '익명화 작업이 completed 로 바꾸고, 파기 작업이 마지막에 삭제한다' },
  { table: 'withdrawn_identities', t1: 'keep', t2: 'schedule', note: 'expires_at 뒤 purge-daily 가 지운다(최대 3년)' },
  { table: 'account_destruction_log', t1: 'keep', t2: 'forever', note: '파기 관리대장. PII 없음' },
  // ---------- 껍데기(맨 마지막) ----------
  { table: 'characters', t1: 'anonymize', t2: 'shell', note: '모든 캐릭터 deleted_at + 자리표시 이름. 파기 때 참조가 없으면 삭제' },
  { table: 'accounts', t1: 'anonymize', t2: 'shell', note: 'anonymized_at, 접속 시각 NULL. 파기 때 참조가 없으면 삭제' },
];

/** 파기 순서(자식 -> 부모, FK 기준). destroyWhere 가 있는 모든 표가 여기 한 번씩 있어야 한다(테스트가 확인) */
export const DESTROY_ORDER: string[] = [
  'anomaly_log', 'drops', 'kill_log', 'kill_stats', 'raid_claims', 'party_run_members', 'dungeon_sweeps', 'sweep_ticket_ledger',
  'sweep_ticket_lots', 'dungeon_runs', 'revive_log', 'character_achievements', 'character_career', 'character_career_trials',
  'character_chests', 'character_enhance_pity', 'character_node_state', 'character_state', 'quest_claims', 'daily_quests', 'site_deliveries',
  'character_items', 'gold_ledger', 'item_ledger', 'xp_ledger', 'enhance_log', 'account_level_rewards', 'account_pass_claims',
  'sealed_pulls', 'raid_shop_purchases', 'gacha_pulls', 'star_synth_log', 'account_collections', 'account_cosmetics', 'account_growth_pass',
  'account_sealed_state', 'account_week_counters', 'economy_holds', 'account_sanctions', 'admin_account_notes', 'admin_grants',
  'auction_flags', 'auction_sinks', 'mail_attachments', 'mail_campaign_deliveries', 'mails', 'auction_trade_flags', 'auction_trades',
  'auction_bids', 'auction_listings', 'friendships', 'blocks', 'report_lines', 'reports', 'party_applications', 'party_members',
  'party_invites', 'field_session_members', 'party_run_host_reports', 'party_runs', 'field_sessions', 'parties', 'star_spend_allocs',
  'star_ledger', 'star_paid_lots', 'star_order_events', 'payment_flags', 'star_admin_grants', 'star_orders', 'star_wallets',
  'payment_profiles',
];

/**
 * dotrpg_purge 역할에 SELECT, DELETE 를 줄 표(최소 권한). 파기 DELETE 대상 + 껍데기 + 대상 계정 판정(DESTROYABLE_ACCOUNTS) 하위 조회용 표.
 * 파기 SQL 의 하위 조회가 읽는 표(accounts, characters, account_withdrawals, reports, mails 등)는 모두 이 목록 안에 있다.
 * admin_audit_log 등 forever 표는 넣지 않는다. ops/README.md 9절 GRANT 와 test/withdrawalPolicy.test.ts 가 이 목록을 쓴다.
 */
export const PURGE_GRANT_TABLES: string[] = [...DESTROY_ORDER, 'characters', 'account_withdrawals', 'accounts'];

/** 파기 대상 표(순서 = 삭제 순서) */
export const DESTROY_STEPS: (PolicyEntry & { destroyWhere: string })[] = DESTROY_ORDER.map((t) => {
  const p = POLICY.find((x) => x.table === t);
  if (!p || p.destroyWhere === undefined) throw new Error(`withdrawalPolicy: ${t} 에 destroyWhere 가 없습니다`);
  return p as PolicyEntry & { destroyWhere: string };
});

/** 익명화 때 실행할 문장(순서대로) */
export const T1_STEPS: { table: string; sql: string }[] = POLICY.flatMap((p) => (p.t1Sql ?? []).map((sql) => ({ table: p.table, sql })));

/** 계정 또는 캐릭터 id 열을 가진 표의 열 이름 규칙(T-W30 검사와 같다) */
export const OWNER_COLUMN_RE = /(^|_)(account|character)_id$/;

/** 레지스트리에 분류되지 않은 표 이름(주어진 DB 표 목록에서) */
export function unclassifiedTables(allTables: string[]): string[] {
  const known = new Set(POLICY.map((p) => p.table));
  return allTables.filter((t) => !known.has(t));
}

/** 분류된 표 중 DB에 없는 이름(오타 방지) */
export function unknownPolicyTables(allTables: string[]): string[] {
  const db = new Set(allTables);
  return POLICY.map((p) => p.table).filter((t) => !db.has(t));
}

/** :acc / :chr 자리표시를 $n::bigint[] 로 바꾸고, 실제로 쓴 배열만 파라미터로 돌려준다 */
export function bindIds(sql: string, accountIds: number[], characterIds: number[]): { text: string; params: unknown[] } {
  const params: unknown[] = [];
  const text = sql.replace(/:(acc|chr)\b/g, (_m, k: string) => {
    const v = k === 'acc' ? accountIds : characterIds;
    let i = params.indexOf(v);
    if (i < 0) {
      params.push(v);
      i = params.length - 1;
    }
    return `$${i + 1}::bigint[]`;
  });
  return { text, params };
}
