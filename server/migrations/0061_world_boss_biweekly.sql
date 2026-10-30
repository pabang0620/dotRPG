-- 0061_world_boss_biweekly(2026-10-30): 월드보스 시즌 1주 -> 2주, 월드보스 순위 칭호 영구(Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 9절).
-- 1) rank_titles.expires_at을 NULL 허용(NULL = 영구). 이미 받은 월드보스 칭호(rank_world_*)도 영구로 바꾼다. 길드보스 "최강 길드"는 그대로 만료.
-- 2) 진행 중인 1주 월드보스 시즌이 2주 기준점(2026-10-29 06:00 KST부터 격주)의 첫 주면 2주로 늘리고 체력도 두 배로 한다
--    (격파 전이면 남은 체력에 예전 최대 체력을 더한다. 이미 격파했으면 그대로). 둘째 주에 걸친 1주 시즌은 그대로 끝나고 다음 2주 시즌이 기준점에 맞춰 시작한다.
-- 선행: 0047(rank_titles, boss_seasons). 대상: PostgreSQL 14 이상. 새 테이블·열은 없다.
-- ============ UP ============
ALTER TABLE rank_titles ALTER COLUMN expires_at DROP NOT NULL;
UPDATE rank_titles SET expires_at = NULL WHERE title_id IN ('rank_world_1', 'rank_world_10', 'rank_world_100');
COMMENT ON TABLE rank_titles IS '랭킹 칭호. 업적 칭호처럼 장착한다(characters.title_achievement에 title_id). expires_at NULL = 영구(월드보스 순위 칭호), 길드보스 칭호는 다음 정산까지';
COMMENT ON COLUMN rank_titles.expires_at IS 'NULL이면 영구(월드보스 rank_world_*, 0061). 길드보스 rank_guild_1은 다음 주 정산 + 1시간';
UPDATE boss_seasons
   SET week_end = week_start + interval '14 days',
       max_hp = CASE WHEN defeated_at IS NULL THEN max_hp * 2 ELSE max_hp END,
       hp_left = CASE WHEN defeated_at IS NULL THEN hp_left + max_hp ELSE hp_left END
 WHERE kind = 'world' AND settled_at IS NULL AND week_end > now()
   AND week_end - week_start = interval '7 days'
   AND (((floor(extract(epoch FROM (week_start - timestamptz '2026-10-29 06:00:00+09')) / 604800)::bigint % 2) + 2) % 2) = 0;
COMMENT ON TABLE boss_seasons IS '보스 시즌. 길드보스는 1주(목요일 06:00 KST), 월드보스는 2주(2026-10-29 06:00 KST부터 격주, 0061)';
-- ============ DOWN ============
-- 영구 칭호는 다시 만료가 필요하다: 내린 시각부터 1주 뒤로 둔다. 늘린 시즌 길이·체력은 되돌리지 않는다(진행 중 기록 보존).
UPDATE rank_titles SET expires_at = now() + interval '7 days' WHERE expires_at IS NULL;
ALTER TABLE rank_titles ALTER COLUMN expires_at SET NOT NULL;
