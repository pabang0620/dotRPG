import { createHash, randomBytes, randomUUID } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import type { Express } from 'express';
import request from 'supertest';
import { createApp } from '../src/app';
import { initConfig, getConfig } from '../src/config/env';
import { closePool, getPool } from '../src/db/pool';
import { isOpenToday } from '../src/domains/dungeons/dungeonRules';
import { getGameData, initGameData } from '../src/gamedata/loader';
import { getNow, setClockOverride } from '../src/utils/clock';
import { getRateLimitStore } from '../src/middleware/rateLimiter';

export const CLIENT_VERSION = '0.2.0';
// GAME_DATA_DIR이 있으면(임시 폴더로 복사한 데이터로 시험할 때) 그 폴더를 쓴다
export const DATA_DIR = process.env.GAME_DATA_DIR ?? path.resolve(__dirname, '..', 'data');
export const DATA_VERSION = (
  JSON.parse(fs.readFileSync(path.join(DATA_DIR, 'data_version.json'), 'utf8')) as { version: string }
).version;

export function buildApp(envOverrides: Record<string, string> = {}): Express {
  initConfig({ ...process.env, ...envOverrides });
  initGameData(getConfig().gameDataDir);
  getRateLimitStore().clear();
  return createApp();
}

export async function resetDb(): Promise<void> {
  const c = await getPool().connect();
  try {
    await c.query('BEGIN');
    // 원장 트리거와 FK를 이 트랜잭션에서만 끈다 (테스트 정리 전용)
    await c.query('SET LOCAL session_replication_role = replica');
    for (const t of [
      'raid_shop_purchases',
      // 탈퇴(0027): 계정을 가리키는 표를 먼저
      'account_destruction_log',
      'withdrawn_identities',
      'account_withdrawals',
      'revive_log',
      'account_level_rewards',
      'account_pass_claims',
      'account_growth_pass',
      'sealed_pulls',
      'account_sealed_state',
      // 11단계: 결제·별조각(원장 트리거는 위에서 끈 상태)
      'star_spend_allocs',
      'star_paid_lots',
      'star_order_events',
      'payment_flags',
      'star_admin_grants',
      'star_orders',
      'payment_profiles',
      'star_ledger',
      'star_wallets',
      'gacha_pulls',
      'star_synth_log',
      'account_collections',
      'account_cosmetics',
      'mail_campaign_deliveries',
      'mail_attachments',
      'mail_campaign_attachments',
      'sweep_ticket_ledger',
      'dungeon_sweeps',
      'sweep_ticket_lots',
      'account_week_counters',
      'auction_trade_flags',
      'economy_holds',
      'income_hourly',
      'play_time_hourly',
      'online_sessions',
      'login_events',
      'account_devices',
      'account_ips',
      'character_career_trials',
      'character_career',
      'admin_grants',
      'held_run_reviews',
      'admin_account_notes',
      'admin_audit_log',
      'admin_sessions',
      'job_runs',
      'maintenance_windows',
      'admin_users',
      'auction_flags',
      'auction_sinks',
      'mails',
      'mail_campaigns',
      'auction_price_daily',
      'auction_trades',
      'auction_bids',
      'auction_listings',
      'report_lines',
      'reports',
      'account_sanctions',
      'party_invites',
      'friendships',
      'blocks',
      'chat_messages',
      'anomaly_log',
      'enhance_log',
      'character_enhance_pity',
      'daily_quests',
      'client_errors',
      'quest_claims',
      'site_deliveries',
      'character_chests',
      'character_node_state',
      'drops',
      'kill_stats',
      'kill_log',
      'field_session_members',
      'field_sessions',
      'relay_room_stats',
      'raid_claims',
      'party_run_host_reports',
      'party_run_members',
      'party_runs',
      'party_applications',
      'party_members',
      'parties',
      'dungeon_runs',
      'xp_ledger',
      'item_ledger',
      'gold_ledger',
      'request_log',
      'character_items',
      'character_state',
      'characters',
      'refresh_tokens',
      'auth_identities',
      'accounts',
    ]) {
      await c.query(`DELETE FROM ${t}`);
    }
    await c.query('COMMIT');
  } catch (err) {
    await c.query('ROLLBACK');
    throw err;
  } finally {
    c.release();
  }
}

export async function shutdown(): Promise<void> {
  await closePool();
}

export function ver(extra: Record<string, string> = {}): Record<string, string> {
  return { 'X-Client-Version': CLIENT_VERSION, 'X-Data-Version': currentDataVersion(), ...extra };
}

/** 지금 올라간 게임 데이터의 버전(시험이 바꾼 데이터 폴더로 앱을 만들었으면 그 버전) */
function currentDataVersion(): string {
  try {
    return getGameData().dataVersion;
  } catch {
    return DATA_VERSION;
  }
}

/** 시험이 데이터 파일을 고친 뒤 data_version.json의 files 해시와 version을 다시 계산한다(GameDataExport.cs와 같은 방식) */
export function rehashDataDir(dir: string): void {
  const p = path.join(dir, 'data_version.json');
  const ver = JSON.parse(fs.readFileSync(p, 'utf8')) as { version: string; files: Record<string, string> };
  const names = Object.keys(ver.files).sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
  const parts: Buffer[] = [];
  for (const name of names) {
    const buf = fs.readFileSync(path.join(dir, name));
    ver.files[name] = createHash('sha256').update(buf).digest('hex');
    parts.push(buf);
  }
  ver.version = createHash('sha256').update(Buffer.concat(parts)).digest('hex').slice(0, 16);
  fs.writeFileSync(p, JSON.stringify(ver, null, 2));
}

export const randomLoginId = (): string => `t${randomBytes(6).toString('hex')}`;

export interface Session {
  loginId: string;
  password: string;
  accountId: string;
  access: string;
  refresh: string;
}

export async function registerAccount(app: Express): Promise<Session> {
  const loginId = randomLoginId();
  const password = 'password-1234';
  const res = await request(app)
    .post('/auth/dev/register')
    .set(ver())
    .send({ login_id: loginId, password });
  if (res.status !== 201) throw new Error(`register failed: ${res.status} ${JSON.stringify(res.body)}`);
  return {
    loginId,
    password,
    accountId: res.body.data.account.id,
    access: res.body.data.access_token,
    refresh: res.body.data.refresh_token,
  };
}

export const auth = (s: Session): Record<string, string> => ver({ Authorization: `Bearer ${s.access}` });

export async function createChar(
  app: Express,
  s: Session,
  name: string,
  cls: 'warrior' | 'mage' = 'warrior',
  requestId: string = randomUUID(),
) {
  return request(app)
    .post('/characters')
    .set(auth(s))
    .send({ request_id: requestId, name, class: cls });
}

// 9단계 예약어 규칙(혼동 문자 접기 5->s 등)에 우연히 걸리지 않도록 c·s·g·m·n·p·v·y와 숫자 5를 뺀 문자만 쓴다
const NAME_ALPHABET = 'abdef012346789';
export const randomName = (): string => `영웅${Array.from(randomBytes(6), (b) => NAME_ALPHABET[b % NAME_ALPHABET.length]).join('')}`;

export function emptyState(version: number, over: object = {}): Record<string, unknown> {
  return {
    version,
    map_id: 'village',
    pos: null,
    facing: 0,
    quests: [],
    story_flags: [],
    tracked_quest: '',
    passives: [],
    skill_gems: [],
    ...over,
  };
}

/**
 * 서버 시계를 요일던전 gold_vein 이 열리는 월요일 낮으로 옮기되 흐름은 실제 시간 그대로 둔다(중계 타이머 테스트용).
 * 실제 요일에 상관없이 gold_vein 파티가 만들어진다. 끝나면 setClockOverride(null).
 */
export function pinClockToMondayFlowing(): void {
  const offset = Date.parse('2026-10-05T03:00:00Z') - Date.now();
  setClockOverride(() => new Date(Date.now() + offset));
}

/** 오늘(서버 시계 기준) 열려 있는 요일던전 중 첫 번째 id. 요일에 따라 테스트가 깨지지 않게 gold_vein 대신 쓴다 */
export function openDungeonToday(): string {
  const eco = getGameData().economy;
  const now = getNow();
  for (const d of eco.dungeons.byId.values()) if (!d.isRaid && isOpenToday(eco, d, now)) return d.id;
  throw new Error('오늘 열린 요일던전이 없습니다');
}
