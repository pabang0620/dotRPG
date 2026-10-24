-- 0054_skin_tiers(2026-10-24, 스킨 등급 개편: Docs/PLAN_SKIN_TIERS_2026-10-23.md)
-- 1) 전설 등급: 뽑기 기록(gacha_pulls.rarity)에 'legend'를 받는다. 장비의 'legendary'와는 다른 값이다.
-- 2) 유니크 -> 전설 합성: 합성 기록(star_synth_log.from_rarity)에 'unique', 지갑에 연속 실패 수(synth_fail_legend).
-- 3) 히든 게이지(hidden_pity): 스킨 뽑기 1회에 1칸, 0에서 시작한다. 스킨 선택 게이지(skin_pity)는 그대로 이월.
-- 4) 없어지는 스킨 세트 8개의 등록 기록을 지운다(외형은 소모하지 않았으므로 새 세트에 다시 등록하면 된다).
-- ============ UP ============
ALTER TABLE star_wallets ADD COLUMN hidden_pity INT NOT NULL DEFAULT 0 CHECK (hidden_pity >= 0);
ALTER TABLE star_wallets ADD COLUMN synth_fail_legend INT NOT NULL DEFAULT 0 CHECK (synth_fail_legend >= 0);
COMMENT ON COLUMN star_wallets.hidden_pity IS '히든 게이지(스킨 뽑기 1회 1칸). 에픽 히든 350, 유니크 히든 2000, 전설 히든 3000칸으로 하나를 고른다';
ALTER TABLE gacha_pulls DROP CONSTRAINT IF EXISTS gacha_pulls_rarity_check;
ALTER TABLE gacha_pulls ADD CONSTRAINT gacha_pulls_rarity_check
  CHECK (rarity IN ('common', 'uncommon', 'rare', 'epic', 'unique', 'legend', 'legendary'));
ALTER TABLE star_synth_log DROP CONSTRAINT IF EXISTS star_synth_log_from_rarity_check;
ALTER TABLE star_synth_log ADD CONSTRAINT star_synth_log_from_rarity_check CHECK (from_rarity IN ('common', 'rare', 'epic', 'unique'));
DELETE FROM account_collections
 WHERE set_id IN ('col_sword', 'col_magic', 'col_knight', 'col_witch', 'col_valor', 'col_arcana', 'col_shade', 'col_blades');
-- ============ DOWN ============
-- 전설 뽑기·유니크 합성 기록이 있으면 되돌릴 수 없다(기록 보존). 지운 세트 등록은 되살리지 않는다.
ALTER TABLE star_synth_log DROP CONSTRAINT star_synth_log_from_rarity_check;
ALTER TABLE star_synth_log ADD CONSTRAINT star_synth_log_from_rarity_check CHECK (from_rarity IN ('common', 'rare', 'epic'));
ALTER TABLE gacha_pulls DROP CONSTRAINT gacha_pulls_rarity_check;
ALTER TABLE gacha_pulls ADD CONSTRAINT gacha_pulls_rarity_check
  CHECK (rarity IN ('common', 'uncommon', 'rare', 'epic', 'unique', 'legendary'));
ALTER TABLE star_wallets DROP COLUMN synth_fail_legend;
ALTER TABLE star_wallets DROP COLUMN hidden_pity;
