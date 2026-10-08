import crypto from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';
import { loadAuctionData, type AuctionData } from './auctionData';
import { loadChatData, type ChatData } from './chatData';
import { loadEconomyData, type EconomyData } from './economyData';
import { getConfig } from '../config/env';
import { loadSweepData, type SweepData } from './sweepData';

// 알 수 없는 필드는 무시(looseObject)해서 데이터에 필드가 늘어도 기동이 깨지지 않는다.
const schemaVer = z.literal(1);

const dataVersionSchema = z.looseObject({
  schema: schemaVer,
  version: z.string().regex(/^[0-9a-f]{16}$/),
  exportedAt: z.string().optional(),
});

const mapsSchema = z.looseObject({
  schema: schemaVer,
  maps: z.array(
    z.looseObject({
      id: z.string().min(1),
      instanced: z.boolean(),
      safe: z.boolean().optional(),
      bounds: z
        .looseObject({
          minX: z.number(),
          minY: z.number(),
          maxX: z.number(),
          maxY: z.number(),
        })
        .optional(),
    }),
  ),
});

const gearSchema = z.looseObject({ itemKey: z.string().min(1), slot: z.number().int().min(0) });
const starterSchema = z.looseObject({
  schema: schemaVer,
  startMap: z.string().min(1),
  gold: z.number().int().min(0),
  items: z.array(z.looseObject({ itemKey: z.string().min(1), count: z.number().int().positive() })),
  gear: z.looseObject({ warrior: gearSchema.optional(), mage: gearSchema.optional() }),
});

const itemsSchema = z.looseObject({
  schema: schemaVer,
  items: z.array(z.looseObject({ id: z.string().min(1) })),
});

const passiveSchema = z.looseObject({
  schema: schemaVer,
  start: z.string().min(1),
  nodes: z.array(
    z.looseObject({ id: z.string().min(1), kind: z.string(), links: z.array(z.string()) }),
  ),
});

const gemsSchema = z.looseObject({
  schema: schemaVer,
  slots: z.number().int().positive(),
  supportsPerSlot: z.number().int().positive(),
  slotLevels: z.array(z.number().int()),
  gems: z.array(
    z.looseObject({
      id: z.string().min(1),
      kind: z.enum(['active', 'support']),
      classOnly: z.string().nullable().optional(),
      unlockLevel: z.number().int(),
    }),
  ),
});

const questIndexSchema = z.looseObject({
  schema: schemaVer,
  quests: z.array(
    z.looseObject({
      id: z.string().min(1),
      stepCount: z.number().int().min(0),
      maxObjectives: z.number().int().min(0),
    }),
  ),
  flags: z.array(z.string()),
});

const enumsSchema = z.looseObject({
  schema: schemaVer,
  facingCount: z.number().int().positive(),
  questStatusMax: z.number().int().min(0),
});

export interface MapInfo {
  id: string;
  instanced: boolean;
  /** 마을(전투 없는 맵). 마을에서는 같은 채널 사람들이 서로 보인다 */
  safe?: boolean;
  bounds?: { minX: number; minY: number; maxX: number; maxY: number };
}
export interface PassiveNode {
  id: string;
  kind: string;
  links: string[];
}
export interface GemInfo {
  id: string;
  kind: 'active' | 'support';
  classOnly: string | null;
  unlockLevel: number;
}
export interface QuestInfo {
  id: string;
  stepCount: number;
  maxObjectives: number;
}
export interface StarterGear {
  itemKey: string;
  slot: number;
}

export interface GameData {
  dataVersion: string;
  maps: Map<string, MapInfo>;
  starter: {
    startMap: string;
    gold: number;
    items: { itemKey: string; count: number }[];
    gear: Record<string, StarterGear | undefined>;
  };
  itemIds: Set<string>;
  passive: { start: string; nodes: Map<string, PassiveNode> };
  gems: {
    slots: number;
    supportsPerSlot: number;
    slotLevels: number[];
    byId: Map<string, GemInfo>;
  };
  quests: { byId: Map<string, QuestInfo>; flags: Set<string> };
  facingCount: number;
  questStatusMax: number;
  /** 3단계(경제 판정)가 쓰는 데이터 */
  economy: EconomyData;
  /** 5단계(채팅) 규칙·신고 사유 */
  chat: ChatData;
  /** 6단계(경매) 수수료·보증금·기간·가격 한도 */
  auction: AuctionData;
  /** 10단계(소탕): SWEEP_ENABLED가 꺼져 있고 파일이 없거나 깨졌으면 null */
  sweep: SweepData | null;
}

function readJson<T extends z.ZodType>(dir: string, file: string, schema: T): z.infer<T> {
  const full = path.join(dir, file);
  let raw: unknown;
  try {
    raw = JSON.parse(fs.readFileSync(full, 'utf8'));
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

function uniqueMap<T extends { id: string }>(rows: T[], what: string): Map<string, T> {
  const m = new Map<string, T>();
  for (const row of rows) {
    if (m.has(row.id)) throw new Error(`게임 데이터 검증 실패: ${what} id 중복 ${row.id}`);
    m.set(row.id, row);
  }
  return m;
}

/** 1~3단계가 쓰는 파일을 읽는다. 교차 검증(시작 지급 id 존재, 패시브 링크 대칭 등)까지 하고 실패하면 던진다. */
export function loadGameData(dir: string): GameData {
  const ver = readJson(dir, 'data_version.json', dataVersionSchema);
  const maps = readJson(dir, 'maps.json', mapsSchema);
  const starter = readJson(dir, 'starter.json', starterSchema);
  const items = readJson(dir, 'items.json', itemsSchema);
  const passive = readJson(dir, 'passive_tree.json', passiveSchema);
  const gems = readJson(dir, 'skill_gems.json', gemsSchema);
  const quests = readJson(dir, 'quest_index.json', questIndexSchema);
  const enums = readJson(dir, 'enums.json', enumsSchema);

  const mapById = uniqueMap(maps.maps, '맵');
  const startInfo = mapById.get(starter.startMap);
  if (!startInfo || startInfo.instanced) {
    throw new Error(`게임 데이터 검증 실패: startMap ${starter.startMap} 이 유효한 비-던전 맵이 아닙니다`);
  }

  const itemIds = new Set(items.items.map((i) => i.id));
  const seenStarter = new Set<string>();
  for (const it of starter.items) {
    if (!itemIds.has(it.itemKey)) {
      throw new Error(`게임 데이터 검증 실패: starter 아이템 ${it.itemKey} 이 items.json에 없습니다`);
    }
    if (seenStarter.has(it.itemKey)) {
      throw new Error(`게임 데이터 검증 실패: starter 아이템 ${it.itemKey} 중복`);
    }
    seenStarter.add(it.itemKey);
  }
  for (const [cls, g] of [['warrior', starter.gear.warrior], ['mage', starter.gear.mage]] as const) {
    if (g && !itemIds.has(g.itemKey)) {
      throw new Error(`게임 데이터 검증 실패: starter gear(${cls}) ${g.itemKey} 이 items.json에 없습니다`);
    }
  }

  const nodes = uniqueMap(passive.nodes, '패시브 노드');
  if (!nodes.has(passive.start)) {
    throw new Error('게임 데이터 검증 실패: 패시브 시작 노드가 nodes에 없습니다');
  }
  for (const n of nodes.values()) {
    for (const l of n.links) {
      const other = nodes.get(l);
      if (!other) throw new Error(`게임 데이터 검증 실패: 패시브 링크 대상 ${l} 없음 (${n.id})`);
      if (!other.links.includes(n.id)) {
        throw new Error(`게임 데이터 검증 실패: 패시브 링크가 비대칭입니다 (${n.id} -> ${l})`);
      }
    }
  }

  if (gems.slotLevels.length !== gems.slots) {
    throw new Error('게임 데이터 검증 실패: slotLevels 길이가 slots와 다릅니다');
  }
  const gemById = uniqueMap(
    gems.gems.map((g) => ({
      id: g.id,
      kind: g.kind,
      classOnly: g.classOnly ?? null,
      unlockLevel: g.unlockLevel,
    })),
    '젬',
  );

  return {
    dataVersion: ver.version,
    maps: mapById,
    starter: {
      startMap: starter.startMap,
      gold: starter.gold,
      items: starter.items.map((i) => ({ itemKey: i.itemKey, count: i.count })),
      gear: { warrior: starter.gear.warrior, mage: starter.gear.mage },
    },
    itemIds,
    passive: { start: passive.start, nodes },
    gems: {
      slots: gems.slots,
      supportsPerSlot: gems.supportsPerSlot,
      slotLevels: gems.slotLevels,
      byId: gemById,
    },
    quests: { byId: uniqueMap(quests.quests, '퀘스트'), flags: new Set(quests.flags) },
    facingCount: enums.facingCount,
    questStatusMax: enums.questStatusMax,
    economy: loadEconomyData(dir, new Set(mapById.keys())),
    chat: loadChatData(dir),
    auction: loadAuctionData(dir),
    sweep: loadSweepData(dir, getConfig().sweep.enabled, itemIds),
  };
}

let cached: GameData | null = null;

const sha256 = (b: Buffer): string => crypto.createHash('sha256').update(b).digest('hex');

/**
 * data_version.json의 files 해시와 version을 실제 파일과 대조한다(GameDataExport.cs와 같은 방식).
 * 파일 해시 = 파일 바이트(UTF-8, BOM 없음)의 SHA-256 hex. version = 파일명 서수 정렬 순서로 내용을 이어 붙인 SHA-256 hex의 앞 16자.
 * files에 없는 서버 전용 파일은 대조하지 않는다.
 */
export function verifyDataHashes(dir: string): void {
  let raw: unknown;
  try {
    raw = JSON.parse(fs.readFileSync(path.join(dir, 'data_version.json'), 'utf8'));
  } catch (err) {
    throw new Error(`게임 데이터 data_version.json 을 읽을 수 없습니다: ${(err as Error).message}`);
  }
  const r = z
    .looseObject({ version: z.string(), files: z.record(z.string(), z.string()) })
    .safeParse(raw);
  if (!r.success) throw new Error('게임 데이터 data_version.json 에 files 해시 목록이 없습니다');
  const names = Object.keys(r.data.files).sort((a, b) => (a < b ? -1 : a > b ? 1 : 0));
  if (names.length === 0) throw new Error('게임 데이터 data_version.json 의 files 가 비어 있습니다');
  const parts: Buffer[] = [];
  for (const name of names) {
    let buf: Buffer;
    try {
      buf = fs.readFileSync(path.join(dir, name));
    } catch {
      throw new Error(`게임 데이터 해시 대조 실패: ${name} 파일이 없습니다`);
    }
    if (sha256(buf) !== r.data.files[name]) {
      throw new Error(`게임 데이터 해시 대조 실패: ${name} 내용이 data_version.json 의 해시와 다릅니다`);
    }
    parts.push(buf);
  }
  if (sha256(Buffer.concat(parts)).slice(0, 16) !== r.data.version) {
    throw new Error('게임 데이터 해시 대조 실패: data_version.json 의 version 이 파일 내용과 다릅니다');
  }
}

export function initGameData(dir: string): GameData {
  verifyDataHashes(dir);
  cached = loadGameData(dir);
  return cached;
}

export function getGameData(): GameData {
  if (!cached) throw new Error('게임 데이터가 로드되지 않았습니다');
  return cached;
}
