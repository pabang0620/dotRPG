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

export const randomName = (): string => `영웅${randomBytes(3).toString('hex').slice(0, 5)}`.slice(0, 8);

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
