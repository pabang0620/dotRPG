// 전직·각성 서버 기록 SQL(character_career, character_career_trials, character_state.career). 변경은 캐릭터 행을 잠근 트랜잭션 안에서만 부른다.
import type { Queryable } from '../../db/pool';
import type { CareerState } from './careerRules';

export interface GrantRow {
  career: number;
  stage: number;
  promoted_at: Date;
  stage_changed_at: Date;
  source: 'promote' | 'legacy_backfill' | 'admin';
}

export async function getGrant(db: Queryable, characterId: number): Promise<GrantRow | null> {
  const r = await db.query<GrantRow>('SELECT career, stage, promoted_at, stage_changed_at, source FROM character_career WHERE character_id = $1', [characterId]);
  return r.rows[0] ?? null;
}

export async function insertGrant(db: Queryable, characterId: number, career: number, now: Date): Promise<void> {
  await db.query(
    `INSERT INTO character_career (character_id, career, stage, promoted_at, stage_changed_at, source) VALUES ($1, $2, 0, $3, $3, 'promote')`,
    [characterId, career, now],
  );
}

export async function setStage(db: Queryable, characterId: number, stage: number, now: Date): Promise<void> {
  await db.query('UPDATE character_career SET stage = $2, stage_changed_at = $3 WHERE character_id = $1', [characterId, stage, now]);
}

/** 저장된 전직 JSON과 패시브(전직 때 훈련 목록 기본값 계산용). 행은 이미 캐릭터 락 아래에 있다 */
export async function stateCareer(db: Queryable, characterId: number): Promise<{ career: CareerState | null; passives: string[] }> {
  const r = await db.query<{ career: CareerState | null; passives: string[] }>('SELECT career, passives FROM character_state WHERE character_id = $1 FOR UPDATE', [characterId]);
  return r.rows[0] ?? { career: null, passives: [] };
}

export async function writeStateCareer(db: Queryable, characterId: number, career: CareerState): Promise<void> {
  await db.query('UPDATE character_state SET career = $2::jsonb WHERE character_id = $1', [characterId, JSON.stringify(career)]);
}

export interface TrialRow {
  id: number;
  career: number;
  started_at: Date;
}

export async function openTrial(db: Queryable, characterId: number): Promise<TrialRow | null> {
  const r = await db.query<{ id: string; career: number; started_at: Date }>(
    'SELECT id, career, started_at FROM character_career_trials WHERE character_id = $1 AND ended_at IS NULL FOR UPDATE',
    [characterId],
  );
  return r.rows[0] ? { id: Number(r.rows[0].id), career: r.rows[0].career, started_at: r.rows[0].started_at } : null;
}

export async function insertTrial(db: Queryable, characterId: number, career: number, now: Date): Promise<void> {
  await db.query('INSERT INTO character_career_trials (character_id, career, started_at) VALUES ($1, $2, $3)', [characterId, career, now]);
}

export async function closeTrial(db: Queryable, trialId: number, outcome: 'success' | 'fail' | 'expired', now: Date): Promise<void> {
  await db.query('UPDATE character_career_trials SET ended_at = $2, outcome = $3 WHERE id = $1', [trialId, now, outcome]);
}
