import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';

// 3단계가 쓰는 필드만 검증한다. 알 수 없는 필드는 무시(looseObject).
// 필드 이름은 server/data/*.json 쪽을 따른다(Docs/server/phase3_mapping.md와 다른 곳은 보고 참고).
const schemaVer = z.literal(1);
const int = z.number().int();
const nonNegInt = z.number().int().min(0);

const monsterDefSchema = z.looseObject({
  id: z.string().min(1),
  kind: z.string(),
  hp: z.number().positive(),
  xp: nonNegInt,
  noLoot: z.boolean(),
  boss: z.boolean(),
  raid: z.boolean(),
  goldMin: nonNegInt,
  goldMax: nonNegInt,
  goldPerHitMin: nonNegInt.default(0),
  goldPerHitMax: nonNegInt.default(0),
  xpByLevel: z.array(nonNegInt).optional(),
  respawnSeconds: z.number().positive().optional(),
  /** 필드 보스의 장비(천분율로 처치마다 굴린다) */
  bossGear: z.string().nullable().optional(),
  bossGearPermille: nonNegInt.default(0),
});

const monstersSchema = z.looseObject({
  schema: schemaVer,
  fieldGoldMin: nonNegInt,
  fieldGoldMaxExclusive: nonNegInt,
  equipmentDropChance: z.number().min(0).max(1),
  hpPerLevel: z.number().min(0),
  xpPerLevel: z.number().min(0),
  loot: z.looseObject({
    goldPileDivisor: z.number().int().positive(),
    goldPileMin: z.number().int().positive(),
    goldPileMax: z.number().int().positive(),
  }),
  monsters: z.array(monsterDefSchema),
  // 필드 해골은 monsters[]가 아니라 최상위 fieldSkeleton 객체다
  fieldSkeleton: monsterDefSchema,
});

const progressionSchema = z.looseObject({
  schema: schemaVer,
  maxLevel: z.number().int().positive(),
  xpToNext: z.array(z.number().int().positive()),
});

const playerSchema = z.looseObject({
  schema: schemaVer,
  attackDamage: z.number().positive(),
  attackCooldown: z.number().positive(),
  maxHealth: z.number().int().positive(),
  mageBoltDamage: z.number().positive(),
  // 4단계: 전투력(CharacterStats.Power) 중 서버가 알 수 있는 항(패시브 제외)
  power: z.looseObject({
    attackWeight: z.number(),
    hpWeight: z.number(),
    mpWeight: z.number(),
    blockWeight: z.number(),
    speedWeight: z.number(),
    levelWeight: z.number(),
    baseMana: z.number(),
    hpPerLevel: z.number(),
    mpPerLevel: z.number(),
    maxBlock: z.number(),
  }),
});

const gameconfigSchema = z.looseObject({
  schema: schemaVer,
  usableItems: z.array(z.looseObject({ id: z.string().min(1), kind: z.string() })),
  storageCapacity: z.number().int().positive(),
  chestReward: z.looseObject({ itemKey: z.string().min(1), count: z.number().int().positive() }),
});

const equipmentSchema = z.looseObject({
  id: z.string().min(1),
  category: z.enum(['Weapon', 'Necklace', 'Ring', 'Top', 'Bottom']),
  rarity: z.enum(['Common', 'Uncommon', 'Rare', 'Epic', 'Unique', 'Legendary']),
  classOnly: z.string().nullable(),
  attack: z.number(),
  starter: z.boolean(),
  dropWeight: nonNegInt,
  tier: nonNegInt,
  sellPrice: nonNegInt,
  /** 필드 보스 전용 장비: 일반 드롭·던전 카드·캐시샵에 넣지 않는다 */
  bossOnly: z.boolean().default(false),
  /** 착용 레벨과 레벨 단계 번호(0 = Lv.1 ... 7 = Lv.40, 8 = Lv.45, 9 = Lv.50: 챕터 3) */
  reqLevel: z.number().int().min(1).max(99).default(1),
  levelTier: z.number().int().min(0).max(9).default(0),
  /** 챕터 번호(3 = 챕터 3, 없거나 0 = 기존). chapterGate가 잠금에 쓴다 */
  chapter: nonNegInt.default(0),
  /** 성장 옵션(%): 몬스터 경험치, 스킬 범위(클라이언트 전투) */
  xpBonus: nonNegInt.default(0),
  aoeBonus: nonNegInt.default(0),
});

const shopSchema = z.looseObject({
  schema: schemaVer,
  enhancedSellBonusPerLevel: z.number().min(0),
  enhancedSellGoldRatio: z.number().min(0),
  stock: z.array(z.looseObject({ id: z.string().min(1), buyPrice: z.number().int().positive() })),
  equipment: z.array(equipmentSchema),
  /** 챕터 3 전용 아이템 id(장비 말고 재료 등). chapterGate가 잠금에 쓴다 */
  chapter3Items: z.array(z.string().min(1)).default([]),
  materials: z.array(
    z.looseObject({
      id: z.string().min(1),
      dropChance: z.number().min(0).max(1),
      minDrop: z.number().int().positive(),
      maxDrop: z.number().int().positive(),
      sellPrice: nonNegInt,
    }),
  ),
  sellPrices: z.array(z.looseObject({ id: z.string().min(1), sellPrice: nonNegInt })),
});

const enhanceLevelSchema = z.looseObject({
  from: nonNegInt,
  successPercent: z.number().int().min(0).max(100),
  failure: z.enum(['Keep', 'Drop3', 'Destroy']),
  pity: z.boolean(),
  gold: nonNegInt,
  bone: nonNegInt,
  ore: nonNegInt,
  essence: nonNegInt,
  statsAttack: nonNegInt,
});

const enhanceSchema = z.looseObject({
  schema: schemaVer,
  maxEnhance: z.number().int().positive(),
  maxPity: z.number().int().positive(),
  pityPerFailure: z.number().int().positive(),
  drop3Levels: z.number().int().positive(),
  rollRange: z.number().int().positive(),
  ticketItem: z.string().min(1),
  materials: z.looseObject({ bone: z.string(), ore: z.string(), essence: z.string() }),
  steps: z.array(z.looseObject({ id: z.string().min(1), levels: z.array(enhanceLevelSchema) })),
  /** 장비 승급(같은 부위 다음 등급, 강화 수치 유지). 재료는 레이드에서만 나오는 핵 */
  promote: z
    .looseObject({
      coreItem: z.string().min(1),
      rows: z.array(z.looseObject({ from: z.string().min(1), to: z.string().min(1), cores: z.number().int().positive(), gold: nonNegInt })),
      raidCoreMid: z.array(nonNegInt).length(4),
      raidCoreFinal: z.array(nonNegInt).length(4),
      /** 챕터 3 계승: Lv.40 유니크·레전더리를 같은 부위·등급의 Lv.50으로(강화 수치 유지). C# PromoteRules.InheritCost */
      inherit: z
        .looseObject({
          coreItem: z.string().min(1),
          shardItem: z.string().min(1),
          sealItem: z.string().min(1),
          fromTier: nonNegInt,
          toTier: nonNegInt,
          rows: z.array(
            z.looseObject({
              from: z.string().min(1),
              to: z.string().min(1),
              cores: nonNegInt,
              shards: nonNegInt,
              seals: nonNegInt,
              gold: nonNegInt,
            }),
          ),
        })
        .optional(),
    })
    .optional(),
});

export interface ItemInfo {
  kind: string;
  name: string;
  bind: 'none' | 'account' | 'character';
  usable: boolean;
  /** 소모품 효과 종류(없으면 null). EnhanceTicket은 power = 목표 강화 단계, LuckBox는 power = 단계·chance = % */
  use: string | null;
  power: number;
  chance: number;
  minutes: number;
}

const itemsSchema = z.looseObject({
  schema: schemaVer,
  items: z.array(
    z.looseObject({
      id: z.string().min(1),
      kind: z.string(),
      stackable: z.boolean(),
      name: z.string().min(1),
      bind: z.enum(['none', 'account', 'character']),
      usable: z.boolean(),
      // 14단계: 소모품 효과(HealHp/HealMp/Buff/EnhanceTicket/LuckBox/SealedBox). 옛 아이템에는 없다
      use: z.string().optional(),
      power: z.number().optional(),
      chance: z.number().optional(),
      minutes: z.number().optional(),
    }),
  ),
});

const questSchema = z.looseObject({
  id: z.string().min(1),
  kind: z.string(),
  minLevel: nonNegInt,
  requires: z.array(z.string()),
  requiresFlags: z.array(z.string()),
  reward: z.looseObject({
    xp: nonNegInt,
    gold: nonNegInt,
    maxHealth: nonNegInt,
    items: z.array(z.looseObject({ id: z.string().min(1), count: z.number().int().positive() })),
    setFlags: z.array(z.string()),
  }),
  objectives: z.looseObject({
    levelNeed: nonNegInt,
    killNeeds: z.record(z.string(), z.number().int().positive()),
    consumes: z.array(z.looseObject({ itemKey: z.string().min(1), count: z.number().int().positive() })),
    dungeonNeeds: z.array(z.looseObject({ target: z.string(), count: z.number().int().positive() })),
    raidNeeds: z.array(z.looseObject({ target: z.string(), count: z.number().int().positive() })),
    flagNeeds: z.array(z.string()),
  }),
});
const questsSchema = z.looseObject({ schema: schemaVer, quests: z.array(questSchema) });

const mapExtraSchema = z.looseObject({
  schema: schemaVer,
  maps: z.array(
    z.looseObject({
      id: z.string().min(1),
      // Enemy levels can exceed the player cap in the post-40 underground world (C# HuntingGrounds.MaxMonsterLevel).
      fieldSpawns: z.array(z.looseObject({ monsterId: z.string(), points: z.number().int().positive(), level: z.number().int().min(1).max(60).default(1), xp: nonNegInt.optional(), respawnSeconds: z.number().positive().default(25) })).default([]),
      sharedField: z.boolean().optional(),
      fieldBoss: z
        .looseObject({ monsterId: z.string().min(1), level: z.number().int().min(1).max(60), xp: nonNegInt, intervalSeconds: z.number().int().positive() })
        .optional(),
      scriptedSpawns: z
        .array(z.looseObject({ monsterId: z.string(), total: z.number().int().positive() }))
        .default([]),
      chests: z.array(z.string()).default([]),
    }),
  ),
});

const groupSchema = z.looseObject({
  monsterId: z.string().min(1),
  count: z.number().int().positive(),
  levelOffset: int,
  isBoss: z.boolean(),
});
/** 13단계: 레이드 보상(확정 골드 + 카드 4장 모두 받기). 카드 표의 itemId 'gear'는 레이드 장비 카드 */
const raidRewardSchema = z.looseObject({
  goldMin: nonNegInt,
  goldMax: nonNegInt,
  goldStep: z.number().int().positive(),
  materialItem: z.string().min(1),
  gearCategories: z.array(z.string().min(1)).min(1),
  gearRarityWeights: z.partialRecord(z.enum(['Epic', 'Unique', 'Legendary']), nonNegInt),
  cards: z
    .array(z.looseObject({ itemId: z.string().min(1), min: z.number().int().positive(), max: z.number().int().positive(), weight: nonNegInt }))
    .min(1),
});

const dungeonsSchema = z.looseObject({
  schema: schemaVer,
  difficulties: z
    .array(
      z.looseObject({
        recommendedLevel: nonNegInt,
        hpMul: z.number().positive(),
        rewardMul: z.number().positive(),
        monsterLevel: nonNegInt,
        revives: nonNegInt,
        minGearRarity: z.enum(['Common', 'Uncommon', 'Rare', 'Epic', 'Unique', 'Legendary']),
        ticketWeight: nonNegInt,
        jackpotPerMille: nonNegInt.default(0),
      }),
    )
    .length(4),
  partyHpScale: z.array(z.number().positive()).min(1),
  aiHpWeight: z.number().min(0).max(1).default(1),
  raidAiHpWeight: z.number().min(0).max(1).default(1),
  dailyEntries: z.number().int().positive(),
  weekendOpensAll: z.boolean(),
  cards: z.looseObject({ count: z.number().int().positive(), gearRareWeight: z.number().int().positive(), gearDropTries: z.number().int().positive() }),
  ranking: z.looseObject({
    timeMax: nonNegInt,
    hitsMax: nonNegInt,
    killsMax: nonNegInt,
    comboMax: nonNegInt,
    revivePenalty: nonNegInt,
    timeZeroAt: z.number().gt(1),
    pointsPerHit: nonNegInt,
    comboTarget: z.number().int().positive(),
    thresholds: z.array(int).min(1),
    xpBonus: z.array(int).min(1),
  }),
  minClearSeconds: z.array(z.number().min(0)).length(4),
  raidMinClearSeconds: z.record(z.string(), z.number().min(0)).default({}),
  /** 영웅 레이드(레이드 난이도 1): 그 레이드의 raidNumbers에 곱하는 배율. 없으면 영웅 레이드가 없다 */
  raidHeroic: z
    .looseObject({ name: z.string().default('영웅 레이드'), hpMul: z.number().positive(), damageMul: z.number().positive(), rewardMul: z.number().positive() })
    .optional(),
  keyItem: z.string().min(1),
  mercenary: z.looseObject({ damageScale: z.number().positive(), maxCompanions: z.number().int().min(0) }),
  dungeons: z.array(
    z.looseObject({
      id: z.string().min(1),
      name: z.string().default(''),
      isRaid: z.boolean(),
      raidTier: z.string().default('None'),
      unlockQuest: z.string().default(''),
      keyCost: nonNegInt.default(0),
      keyMin: nonNegInt.default(0),
      keyMax: nonNegInt.default(0),
      maxParty: z.number().int().min(1).max(4).default(4),
      raidNumbers: z
        .looseObject({
          recommendedLevel: nonNegInt,
          hpMul: z.number().positive(),
          rewardMul: z.number().positive(),
          monsterLevel: nonNegInt,
          revives: nonNegInt,
          minGearRarity: z.enum(['Common', 'Uncommon', 'Rare', 'Epic', 'Unique', 'Legendary']),
          ticketWeight: nonNegInt,
          jackpotPerMille: nonNegInt.default(0),
        })
        .optional(),
      raidReward: raidRewardSchema.optional(),
      clearXp: nonNegInt,
      clearXpFloor: z.array(nonNegInt).length(4).default([0, 0, 0, 0]),
      xpMul: z.number().positive(),
      bossRoom: nonNegInt,
      openDays: z.array(z.string()),
      // 일반 던전(2026-10-14): 매일 개방, 하루 입장 횟수에 들어가지 않는다
      always: z.boolean().default(false),
      // 클리어 확정 보상(레이드 제외): 재료 1장 + 골드 1장, 이어서 운 카드(rewards 표)
      sure: z
        .looseObject({
          material: z.string().min(1),
          materialMin: z.number().int().positive(),
          materialMax: z.number().int().positive(),
          goldMin: z.number().int().positive(),
          goldMax: z.number().int().positive(),
        })
        .optional(),
      referenceSeconds: z.array(z.number().positive()).length(4),
      rewards: z.array(
        z.looseObject({ itemId: z.string().min(1), min: z.number().int().positive(), max: z.number().int().positive(), weight: nonNegInt }),
      ),
      rooms: z.array(
        z.looseObject({ mapId: z.string().min(1), isBoss: z.boolean(), groups: z.array(groupSchema).min(1) }),
      ),
    }),
  ),
});

type MonsterDef = z.infer<typeof monsterDefSchema>;
type Monsters = z.infer<typeof monstersSchema>;
export type EquipmentDef = z.infer<typeof equipmentSchema>;
export type EnhanceLevel = z.infer<typeof enhanceLevelSchema>;
export type QuestRule = z.infer<typeof questSchema>;
export type DungeonDef = z.infer<typeof dungeonsSchema>['dungeons'][number];
export type DifficultyDef = z.infer<typeof dungeonsSchema>['difficulties'][number];
export type { MonsterDef };

export interface InheritRow {
  to: string;
  cores: number;
  shards: number;
  seals: number;
  gold: number;
}

export const RARITY_ORDER = ['Common', 'Uncommon', 'Rare', 'Epic', 'Unique', 'Legendary'] as const;

export interface MapExtra {
  fieldSpawns: { monsterId: string; points: number; level: number; xp?: number; respawnSeconds: number }[];
  /** 파티 필드 세션을 만들 수 있는 맵(8단계). 데이터가 주지 않으면 fieldSpawns 유무로 본다 */
  sharedField: boolean;
  /** 필드 보스(15분 창마다 캐릭터당 1회 인정) */
  fieldBoss: { monsterId: string; level: number; xp: number; intervalSeconds: number } | null;
  scriptedSpawns: { monsterId: string; total: number }[];
  chests: Set<string>;
}

export interface EconomyData {
  monsters: Map<string, MonsterDef>; // fieldSkeleton 포함
  monsterRules: Pick<Monsters, 'fieldGoldMin' | 'fieldGoldMaxExclusive' | 'equipmentDropChance' | 'hpPerLevel' | 'xpPerLevel' | 'loot'>;
  progression: { maxLevel: number; xpToNext: number[] };
  player: {
    attackDamage: number;
    attackCooldown: number;
    maxHealth: number;
    mageBoltDamage: number;
    power: z.infer<typeof playerSchema>['power'];
  };
  config: z.infer<typeof gameconfigSchema>;
  shop: {
    stock: Map<string, number>;
    equipment: Map<string, EquipmentDef>;
    equipmentList: EquipmentDef[];
    materials: z.infer<typeof shopSchema>['materials'];
    sellPrices: Map<string, number>;
    enhancedSellBonusPerLevel: number;
    enhancedSellGoldRatio: number;
  };
  enhance: {
    maxEnhance: number;
    maxPity: number;
    pityPerFailure: number;
    drop3Levels: number;
    rollRange: number;
    ticketItem: string;
    materials: { bone: string; ore: string; essence: string };
    steps: Map<string, EnhanceLevel[]>;
    promote: {
      coreItem: string;
      rows: Map<string, { to: string; cores: number; gold: number }>;
      raidCoreMid: number[];
      raidCoreFinal: number[];
    } | null;
    /** 챕터 3 계승표(없으면 null) */
    inherit: {
      coreItem: string;
      shardItem: string;
      sealItem: string;
      rows: Map<string, InheritRow>;
    } | null;
  };
  /** 챕터 태그: "kind:id" -> 챕터 번호(3만 들어 있다). chapterGate가 읽는다 */
  chapterTags: Map<string, number>;
  /** 챕터 3 특화: "skill:side" -> 정의. 파일이 없으면 빈 맵 */
  skillSpecs: Map<string, SkillSpecInfo>;
  items: Map<string, ItemInfo>;
  quests: Map<string, QuestRule>;
  mapExtra: Map<string, MapExtra>;
  dungeons: {
    byId: Map<string, DungeonDef>;
    difficulties: DifficultyDef[];
    partyHpScale: number[];
    /** AI 동료 1명을 사람 몇 명으로 셀지(인원 보정). DungeonDatabase.AiHpWeight */
    aiHpWeight: number;
    /** 레이드에서 AI 동료 1명을 사람 몇 명으로 셀지. DungeonDatabase.RaidAiHpWeight */
    raidAiHpWeight: number;
    dailyEntries: number;
    weekendOpensAll: boolean;
    cards: { count: number; gearRareWeight: number; gearDropTries: number };
    ranking: z.infer<typeof dungeonsSchema>['ranking'];
    minClearSeconds: number[];
    raidMinClearSeconds: Record<string, number>;
    raidHeroic: { name: string; hpMul: number; damageMul: number; rewardMul: number } | null;
    keyItem: string;
    mercenary: { damageScale: number; maxCompanions: number };
  };
}

// 챕터 3 특화(skill_specs.json, Docs/ch3/SKILL_SPECIALIZATION.md 4절). 파일이 없으면 특화 없음
const skillSpecsSchema = z.looseObject({
  schema: z.literal(1),
  points: z.looseObject({ from: z.number().int(), to: z.number().int(), active: z.number().int().positive(), passive: z.number().int().positive() }),
  specs: z.array(
    z.looseObject({
      id: z.string().min(1),
      skill: z.string().min(1),
      side: z.enum(['a', 'b']),
      axis: z.string().min(1),
      cost: z.number().int().positive(),
      chapter: z.number().int().optional(),
    }),
  ),
});
export interface SkillSpecInfo { id: string; skill: string; side: 'a' | 'b'; axis: string; cost: number }

function readJson<T extends z.ZodType>(dir: string, file: string, schema: T): z.infer<T> {
  let raw: unknown;
  try {
    raw = JSON.parse(fs.readFileSync(path.join(dir, file), 'utf8'));
  } catch (err) {
    throw new Error(`게임 데이터 ${file} 을 읽을 수 없습니다: ${(err as Error).message}`);
  }
  const r = schema.safeParse(raw);
  if (!r.success) {
    const detail = r.error.issues
      .slice(0, 5)
      .map((i) => `${i.path.join('.')}: ${i.message}`)
      .join('; ');
    throw new Error(`게임 데이터 ${file} 검증 실패: ${detail}`);
  }
  return r.data;
}

function uniqueBy<T>(rows: T[], key: (r: T) => string, what: string): Map<string, T> {
  const m = new Map<string, T>();
  for (const row of rows) {
    const k = key(row);
    if (m.has(k)) throw new Error(`게임 데이터 검증 실패: ${what} id 중복 ${k}`);
    m.set(k, row);
  }
  return m;
}

function validateRaidReward(d: z.infer<typeof dungeonsSchema>['dungeons'][number], itemMap: Map<string, unknown>): void {
  const rr = d.raidReward;
  const fail = (msg: string): never => {
    throw new Error(`게임 데이터 검증 실패: 레이드 ${d.id} raidReward ${msg}`);
  };
  if (!rr) return fail('가 없습니다');
  if (rr.goldMax < rr.goldMin || (rr.goldMax - rr.goldMin) % rr.goldStep !== 0) fail('골드 범위가 단위로 나누어 떨어지지 않습니다');
  if (rr.cards.reduce((a, c) => a + c.weight, 0) <= 0) fail('카드 가중치 합이 0입니다');
  if (!itemMap.has(rr.materialItem)) fail(`전용 재료 ${rr.materialItem} 이 items.json에 없습니다`);
  for (const c of rr.cards) {
    if (c.itemId !== 'gear' && !itemMap.has(c.itemId)) fail(`카드 ${c.itemId} 이 items.json에 없습니다`);
    if (c.max < c.min) fail(`카드 ${c.itemId} 의 수량 범위가 거꾸로입니다`);
  }
  if (Object.values(rr.gearRarityWeights).reduce((a, w) => a + w, 0) <= 0) fail('장비 등급 가중치 합이 0입니다');
}

export function loadEconomyData(dir: string, mapIds: Set<string>): EconomyData {
  const mon = readJson(dir, 'monsters.json', monstersSchema);
  const prog = readJson(dir, 'progression.json', progressionSchema);
  const player = readJson(dir, 'player.json', playerSchema);
  const config = readJson(dir, 'gameconfig.json', gameconfigSchema);
  const shop = readJson(dir, 'shop.json', shopSchema);
  const enh = readJson(dir, 'enhance.json', enhanceSchema);
  const items = readJson(dir, 'items.json', itemsSchema);
  const quests = readJson(dir, 'quest_index.json', questsSchema);
  const maps = readJson(dir, 'maps.json', mapExtraSchema);
  const dng = readJson(dir, 'dungeons.json', dungeonsSchema);

  const monsters = uniqueBy([...mon.monsters, mon.fieldSkeleton], (m) => m.id, '몬스터');
  if (prog.xpToNext.length < prog.maxLevel - 1) {
    throw new Error('게임 데이터 검증 실패: progression.xpToNext 가 maxLevel-1 개보다 적습니다');
  }
  const itemMap = new Map(
    items.items.map((i) => [i.id, { kind: i.kind, name: i.name, bind: i.bind, usable: i.usable, use: i.use ?? null, power: i.power ?? 0, chance: i.chance ?? 0, minutes: i.minutes ?? 0 }] as const),
  );
  const needItem = (id: string, where: string): void => {
    if (!itemMap.has(id)) throw new Error(`게임 데이터 검증 실패: ${where} 의 ${id} 이 items.json에 없습니다`);
  };

  const equipment = uniqueBy(shop.equipment, (e) => e.id, '장비');
  for (const e of equipment.values()) needItem(e.id, 'shop.equipment');
  for (const s of shop.stock) needItem(s.id, 'shop.stock');
  for (const m of shop.materials) needItem(m.id, 'shop.materials');
  const sellPrices = new Map<string, number>();
  for (const m of shop.materials) sellPrices.set(m.id, m.sellPrice);
  for (const s of shop.sellPrices) sellPrices.set(s.id, s.sellPrice);

  const steps = uniqueBy(enh.steps, (s) => s.id, '강화 단계표');
  const stepLevels = new Map<string, EnhanceLevel[]>();
  for (const [id, s] of steps) {
    if (!equipment.has(id)) throw new Error(`게임 데이터 검증 실패: enhance.steps ${id} 이 shop.equipment에 없습니다`);
    s.levels.forEach((l, i) => {
      if (l.from !== i) throw new Error(`게임 데이터 검증 실패: enhance.steps ${id} levels[${i}].from 이 ${l.from} 입니다`);
    });
    if (s.levels.length < enh.maxEnhance) {
      throw new Error(`게임 데이터 검증 실패: enhance.steps ${id} 의 levels 가 maxEnhance 보다 짧습니다`);
    }
    stepLevels.set(id, s.levels);
  }
  for (const id of equipment.keys()) {
    if (!stepLevels.has(id)) throw new Error(`게임 데이터 검증 실패: ${id} 의 강화 단계표가 없습니다`);
  }
  needItem(enh.ticketItem, 'enhance.ticketItem');
  let promote: EconomyData['enhance']['promote'] = null;
  if (enh.promote) {
    needItem(enh.promote.coreItem, 'enhance.promote.coreItem');
    const rows = new Map<string, { to: string; cores: number; gold: number }>();
    for (const r of enh.promote.rows) {
      if (!equipment.has(r.from) || !equipment.has(r.to)) throw new Error(`게임 데이터 검증 실패: enhance.promote ${r.from} -> ${r.to} 이 shop.equipment에 없습니다`);
      if (rows.has(r.from)) throw new Error(`게임 데이터 검증 실패: enhance.promote ${r.from} 중복`);
      rows.set(r.from, { to: r.to, cores: r.cores, gold: r.gold });
    }
    promote = { coreItem: enh.promote.coreItem, rows, raidCoreMid: enh.promote.raidCoreMid, raidCoreFinal: enh.promote.raidCoreFinal };
  }
  let inherit: EconomyData['enhance']['inherit'] = null;
  if (enh.promote?.inherit) {
    const t = enh.promote.inherit;
    for (const id of [t.coreItem, t.shardItem, t.sealItem]) needItem(id, 'enhance.promote.inherit');
    const rows = new Map<string, InheritRow>();
    for (const r of t.rows) {
      const from = equipment.get(r.from);
      const to = equipment.get(r.to);
      if (!from || !to) throw new Error(`게임 데이터 검증 실패: enhance.promote.inherit ${r.from} -> ${r.to} 이 shop.equipment에 없습니다`);
      if (from.rarity !== to.rarity || from.category !== to.category) {
        throw new Error(`게임 데이터 검증 실패: enhance.promote.inherit ${r.from} -> ${r.to} 의 등급·부위가 다릅니다`);
      }
      if (rows.has(r.from)) throw new Error(`게임 데이터 검증 실패: enhance.promote.inherit ${r.from} 중복`);
      rows.set(r.from, { to: r.to, cores: r.cores, shards: r.shards, seals: r.seals, gold: r.gold });
    }
    inherit = { coreItem: t.coreItem, shardItem: t.shardItem, sealItem: t.sealItem, rows };
  }

  // 챕터 태그 색인: 장비의 chapter 필드, chapter3Items(재료 등), 그리고 퀘스트·맵·던전·몬스터 데이터의 chapter 필드(3단계부터)
  const chapterTags = new Map<string, number>();
  const tag = (kind: string, id: string, chapter: unknown): void => {
    if (typeof chapter === 'number' && chapter >= 3) chapterTags.set(`${kind}:${id}`, chapter);
  };
  for (const e of shop.equipment) tag('item', e.id, e.chapter);
  for (const id of shop.chapter3Items) {
    needItem(id, 'shop.chapter3Items');
    tag('item', id, 3);
  }
  for (const q of quests.quests) tag('quest', q.id, (q as { chapter?: unknown }).chapter);
  for (const m of maps.maps) tag('map', m.id, (m as { chapter?: unknown }).chapter);
  for (const d of dng.dungeons) tag('dungeon', d.id, (d as { chapter?: unknown }).chapter);
  for (const m of mon.monsters) tag('monster', m.id, (m as { chapter?: unknown }).chapter);
  const skillSpecs = new Map<string, SkillSpecInfo>();
  if (fs.existsSync(path.join(dir, 'skill_specs.json'))) {
    const sp = readJson(dir, 'skill_specs.json', skillSpecsSchema);
    for (const x of sp.specs) {
      const key = `${x.skill}:${x.side}`;
      if (skillSpecs.has(key)) throw new Error(`게임 데이터 검증 실패: skill_specs ${key} 중복`);
      skillSpecs.set(key, { id: x.id, skill: x.skill, side: x.side, axis: x.axis, cost: x.cost });
      tag('spec', x.id, x.chapter);
    }
  }
  for (const id of [enh.materials.bone, enh.materials.ore, enh.materials.essence]) needItem(id, 'enhance.materials');

  for (const u of config.usableItems) needItem(u.id, 'gameconfig.usableItems');
  needItem(config.chestReward.itemKey, 'gameconfig.chestReward');

  const questMap = uniqueBy(quests.quests, (q) => q.id, '퀘스트');
  for (const q of questMap.values()) {
    for (const r of q.requires) {
      if (!questMap.has(r)) throw new Error(`게임 데이터 검증 실패: 퀘스트 ${q.id} 의 선행 ${r} 이 없습니다`);
    }
    for (const it of q.reward.items) needItem(it.id, `퀘스트 ${q.id} 보상`);
    for (const c of q.objectives.consumes) needItem(c.itemKey, `퀘스트 ${q.id} 소모`);
  }

  const mapExtra = new Map<string, MapExtra>();
  for (const m of maps.maps) {
    if (!mapIds.has(m.id)) continue;
    for (const s of [...m.fieldSpawns, ...m.scriptedSpawns]) {
      if (!monsters.has(s.monsterId)) {
        throw new Error(`게임 데이터 검증 실패: 맵 ${m.id} 의 몬스터 ${s.monsterId} 이 monsters.json에 없습니다`);
      }
    }
    mapExtra.set(m.id, {
      fieldSpawns: m.fieldSpawns,
      sharedField: m.sharedField ?? m.fieldSpawns.length > 0,
      fieldBoss: m.fieldBoss ?? null,
      scriptedSpawns: m.scriptedSpawns,
      chests: new Set(m.chests),
    });
  }

  const dungeons = uniqueBy(dng.dungeons, (d) => d.id, '던전');
  for (const d of dungeons.values()) {
    if (d.bossRoom >= d.rooms.length) throw new Error(`게임 데이터 검증 실패: 던전 ${d.id} 의 bossRoom 이 방 수를 넘습니다`);
    for (const r of d.rooms) {
      if (!mapIds.has(r.mapId)) throw new Error(`게임 데이터 검증 실패: 던전 ${d.id} 의 방 맵 ${r.mapId} 이 maps.json에 없습니다`);
      for (const g of r.groups) {
        if (!monsters.has(g.monsterId)) {
          throw new Error(`게임 데이터 검증 실패: 던전 ${d.id} 의 몬스터 ${g.monsterId} 이 monsters.json에 없습니다`);
        }
      }
    }
    for (const r of d.rewards) {
      if (r.itemId !== 'gear' && !itemMap.has(r.itemId)) {
        throw new Error(`게임 데이터 검증 실패: 던전 ${d.id} 보상 ${r.itemId} 이 items.json에 없습니다`);
      }
    }
    if (d.isRaid) validateRaidReward(d, itemMap);
  }
  if (dng.ranking.thresholds.length + 1 !== dng.ranking.xpBonus.length) {
    throw new Error('게임 데이터 검증 실패: ranking.xpBonus 는 thresholds 보다 1개 많아야 합니다');
  }

  return {
    monsters,
    monsterRules: {
      fieldGoldMin: mon.fieldGoldMin,
      fieldGoldMaxExclusive: mon.fieldGoldMaxExclusive,
      equipmentDropChance: mon.equipmentDropChance,
      hpPerLevel: mon.hpPerLevel,
      xpPerLevel: mon.xpPerLevel,
      loot: mon.loot,
    },
    progression: { maxLevel: prog.maxLevel, xpToNext: prog.xpToNext },
    player: {
      attackDamage: player.attackDamage,
      attackCooldown: player.attackCooldown,
      maxHealth: player.maxHealth,
      mageBoltDamage: player.mageBoltDamage,
      power: player.power,
    },
    config,
    shop: {
      stock: new Map(shop.stock.map((s) => [s.id, s.buyPrice] as const)),
      equipment,
      equipmentList: shop.equipment,
      materials: shop.materials,
      sellPrices,
      enhancedSellBonusPerLevel: shop.enhancedSellBonusPerLevel,
      enhancedSellGoldRatio: shop.enhancedSellGoldRatio,
    },
    enhance: {
      maxEnhance: enh.maxEnhance,
      maxPity: enh.maxPity,
      pityPerFailure: enh.pityPerFailure,
      drop3Levels: enh.drop3Levels,
      rollRange: enh.rollRange,
      ticketItem: enh.ticketItem,
      materials: { bone: enh.materials.bone, ore: enh.materials.ore, essence: enh.materials.essence },
      steps: stepLevels,
      promote,
      inherit,
    },
    chapterTags,
    skillSpecs,
    items: itemMap,
    quests: questMap,
    mapExtra,
    dungeons: {
      byId: dungeons,
      difficulties: dng.difficulties,
      partyHpScale: dng.partyHpScale,
      aiHpWeight: dng.aiHpWeight,
      raidAiHpWeight: dng.raidAiHpWeight,
      dailyEntries: dng.dailyEntries,
      weekendOpensAll: dng.weekendOpensAll,
      cards: dng.cards,
      ranking: dng.ranking,
      minClearSeconds: dng.minClearSeconds,
      raidMinClearSeconds: dng.raidMinClearSeconds,
      raidHeroic: dng.raidHeroic ?? null,
      keyItem: dng.keyItem,
      mercenary: { damageScale: dng.mercenary.damageScale, maxCompanions: dng.mercenary.maxCompanions },
    },
  };
}
