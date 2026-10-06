import { randomBytes, randomUUID } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import type { Express } from 'express';
import request from 'supertest';
import { createApp } from '../src/app';
import { initConfig, getConfig } from '../src/config/env';
import { closePool, getPool } from '../src/db/pool';
import { initGameData } from '../src/gamedata/loader';
import { getRateLimitStore } from '../src/middleware/rateLimiter';

export const CLIENT_VERSION = '0.2.0';
export const DATA_DIR = path.resolve(__dirname, '..', 'data');
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
  return { 'X-Client-Version': CLIENT_VERSION, 'X-Data-Version': DATA_VERSION, ...extra };
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
