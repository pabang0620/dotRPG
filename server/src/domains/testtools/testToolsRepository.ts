// 시험 도구 SQL(Docs/PLAN_TEST_TOOLS_2026-10-29.md). 캐릭터 행을 잠근 경제 트랜잭션 안에서만 부른다.
import type { Queryable } from '../../db/pool';

/** 개발 로그인(provider dev) 계정인가 */
export async function isDevAccount(db: Queryable, accountId: number): Promise<boolean> {
  const r = await db.query("SELECT 1 FROM auth_identities WHERE account_id = $1 AND provider = 'dev' LIMIT 1", [accountId]);
  return r.rows.length > 0;
}

/** 처치 누적을 need 이상으로 올린다(이미 많으면 그대로) */
export async function raiseKills(db: Queryable, characterId: number, monsterId: string, need: number): Promise<void> {
  await db.query(
    `INSERT INTO kill_stats (character_id, monster_id, kills) VALUES ($1, $2, $3)
     ON CONFLICT (character_id, monster_id) DO UPDATE SET kills = GREATEST(kill_stats.kills, EXCLUDED.kills), updated_at = now()`,
    [characterId, monsterId, need],
  );
}

/** 클리어한 판 n개를 넣는다(scripts/test-checkpoint.mjs와 같은 모양: 3일 전, 입장 횟수에 세지 않음) */
export async function insertClears(db: Queryable, characterId: number, dungeonId: string, n: number, past: Date): Promise<void> {
  for (let i = 0; i < n; i++) {
    await db.query(
      `INSERT INTO dungeon_runs (character_id, dungeon_id, difficulty, state, reset_day, started_at, ended_at, rank, cards, counts_entry)
       VALUES ($1, $2, 0, 'cleared', $3, $3, $3, 0, '[]'::jsonb, false)`,
      [characterId, dungeonId, past],
    );
  }
}

export interface QuestSaveRow {
  id: string;
  step: number;
  counts: number[];
  status: number;
}

export async function questState(db: Queryable, characterId: number): Promise<{ quests: QuestSaveRow[]; storyFlags: string[] }> {
  const r = await db.query<{ quests: QuestSaveRow[] | null; story_flags: string[] | null }>(
    'SELECT quests, story_flags FROM character_state WHERE character_id = $1',
    [characterId],
  );
  return { quests: r.rows[0]?.quests ?? [], storyFlags: r.rows[0]?.story_flags ?? [] };
}

/** 퀘스트 상태와 이야기 플래그만 바꾼다. version은 올리지 않는다(전직 기록 careerGrantRepository와 같다): 클라이언트 저장이 충돌하지 않는다 */
export async function saveQuestState(db: Queryable, characterId: number, quests: QuestSaveRow[], storyFlags: string[]): Promise<void> {
  await db.query('UPDATE character_state SET quests = $2::jsonb, story_flags = $3 WHERE character_id = $1', [
    characterId,
    JSON.stringify(quests),
    storyFlags,
  ]);
}
