// 요일 던전 유니크 보너스(0043): 판당 22%, 유니크 없이 7판이 지나면 8판째 보장, 그 단계에 없는 부위를 먼저 준다
import type { Queryable } from '../../db/pool';
import type { DungeonDef, EconomyData } from '../../gamedata/economyData';
import { tierOfLevel } from '../../utils/gearTier';
import type { Rng } from '../../utils/rng';
import { LUCK_CARDS, UNIQUE_BONUS, rollUniqueBonus, uniqueBonusApplies, uniqueCategoriesOwned, type Card, type DiffNumbers } from './dungeonRules';

/** 지금 misses(유니크 없이 지난 판 수). 행이 없으면 0 */
export async function readMisses(db: Queryable, characterId: number): Promise<number> {
  const r = await db.query<{ misses: number }>('SELECT misses FROM dungeon_unique_pity WHERE character_id = $1', [characterId]);
  return r.rows[0]?.misses ?? 0;
}

/**
 * 클리어 보상 카드(확정 재료·골드 + 운 카드)에 유니크 보너스를 적용한다. 같은 트랜잭션 안에서 부른다(행 잠금).
 * 유니크가 나오면 마지막 운 카드를 그 유니크로 바꾼다. 반환: 바뀐 카드 목록, 다음 보장까지 남은 판 수
 */
export async function applyUniqueBonus(
  db: Queryable,
  characterId: number,
  eco: EconomyData,
  d: DungeonDef,
  diff: DiffNumbers,
  cls: string,
  cards: Card[],
  rng: Rng,
): Promise<{ cards: Card[]; unique: string | null; left: number }> {
  if (!uniqueBonusApplies(d) || cards.length < 2 + LUCK_CARDS) return { cards, unique: null, left: 0 };
  await db.query('INSERT INTO dungeon_unique_pity (character_id) VALUES ($1) ON CONFLICT DO NOTHING', [characterId]);
  const row = await db.query<{ misses: number }>('SELECT misses FROM dungeon_unique_pity WHERE character_id = $1 FOR UPDATE', [characterId]);
  const misses = row.rows[0]?.misses ?? 0;
  const keys = await db.query<{ item_key: string }>(
    "SELECT item_key FROM character_items WHERE character_id = $1 AND location IN ('bag', 'storage', 'worn')",
    [characterId],
  );
  const owned = uniqueCategoriesOwned(eco, tierOfLevel(diff.recommendedLevel), keys.rows.map((r) => r.item_key));
  const rolled = rollUniqueBonus(eco, diff, cls, rng, misses, owned);
  await db.query('UPDATE dungeon_unique_pity SET misses = $2, updated_at = now() WHERE character_id = $1', [characterId, rolled.misses]);
  const out = cards.slice();
  if (rolled.key) {
    // 운 카드 중 대박(유니크·레전더리)이 아닌 마지막 카드를 바꾼다. 모두 대박이면 한 장을 더한다(대박을 지우지 않는다)
    const isJackpot = (c: Card): boolean => {
      const base = c.item_key.split('+')[0] as string;
      const r = eco.shop.equipment.get(base)?.rarity;
      return r === 'Unique' || r === 'Legendary';
    };
    let at = -1;
    for (let i = out.length - 1; i >= out.length - LUCK_CARDS && i >= 0; i--) {
      if (!isJackpot(out[i] as Card)) { at = i; break; }
    }
    if (at >= 0) out[at] = { item_key: rolled.key, count: 1 };
    else out.push({ item_key: rolled.key, count: 1 });
  }
  return { cards: out, unique: rolled.key, left: pityLeft(rolled.misses) };
}

/** 보장까지 남은 판 수(1 = 다음 판 보장) */
export function pityLeft(misses: number): number {
  return Math.max(1, UNIQUE_BONUS.pityRuns - misses);
}
