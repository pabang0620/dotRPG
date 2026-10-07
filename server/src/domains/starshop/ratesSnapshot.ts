// 확률표 버전 고정(Docs/server/phase11_payments.md 11.4): 기동할 때 현재 RATES_VERSION의 확률표 전체를 해시로 남기고,
// 같은 버전에 다른 내용이면 기동을 거부한다(확률을 바꾸고 버전을 올리지 않은 것). star_rates_snapshots는 추가 전용이다.
import type { Queryable } from '../../db/pool';
import { hashRequest } from '../../db/idempotency';
import {
  BANNER_WEIGHT,
  COLLECTIONS,
  DUPLICATE_REFUND,
  EPIC_SKIN_PRICE,
  EXCHANGE_PRICE,
  GAUGE,
  GEAR_BANNERS,
  GEAR_RARITY_PERMILLE,
  GEAR_RATE_SCALE,
  PULL_PRICE,
  RATES_VERSION,
  RATE_SCALE,
  STAR_COSMETICS,
  SYNTH,
  SYNTH_COUNT,
  TEN_COUNT,
  TEN_PRICE,
} from './starshopDefs';
import { gearTable } from './starshopService';
import { TIER_LEVELS } from '../../utils/gearTier';

type GearBanner = keyof typeof GEAR_BANNERS;

/** 그 버전의 확률·가격·천장·게이지·중복 규칙 전체(서버가 starshopDefs와 게임 데이터에서 만든다) */
export function buildRatesContent(): Record<string, unknown> {
  const classes = ['warrior', 'mage'];
  const gear: Record<string, unknown> = {};
  for (const b of Object.keys(GEAR_BANNERS) as GearBanner[]) {
    for (const cls of classes) {
      for (let tier = 0; tier < TIER_LEVELS.length; tier++) {
        gear[`${b}.${cls}.${tier}`] = gearTable(b, cls, tier).map((t) => ({ rarity: t.rarity, permille: t.permille, items: t.items.map((i) => i.id) }));
      }
    }
  }
  return {
    version: RATES_VERSION,
    rate_scale: RATE_SCALE,
    banner_weight: BANNER_WEIGHT,
    gauge: GAUGE,
    price: { one: PULL_PRICE, ten: TEN_PRICE, ten_count: TEN_COUNT },
    duplicate_refund: DUPLICATE_REFUND,
    synth: { count: SYNTH_COUNT, rules: SYNTH },
    exchange: { price: EXCHANGE_PRICE, epic_skin: EPIC_SKIN_PRICE },
    gear_rarity: { scale: GEAR_RATE_SCALE, permille: GEAR_RARITY_PERMILLE },
    cosmetics: STAR_COSMETICS.map((c) => ({ id: c.id, rarity: c.rarity, skin: c.skin ?? null })),
    collections: COLLECTIONS,
    gear,
  };
}

export class RatesVersionConflict extends Error {
  constructor(version: string) {
    super(`RATES_VERSION_CONFLICT: 확률표 버전 ${version} 의 내용이 이미 기록된 것과 다릅니다. 확률을 바꿨다면 RATES_VERSION 을 올려야 합니다`);
    this.name = 'RatesVersionConflict';
  }
}

/** 기동 때 한 번. 처음 보는 버전이면 기록하고, 같은 버전인데 내용이 다르면 RatesVersionConflict 를 던진다 */
export async function ensureRatesSnapshot(db: Queryable, content: Record<string, unknown> = buildRatesContent()): Promise<{ version: string; created: boolean }> {
  const version = String(content.version);
  const hash = hashRequest(content);
  const ins = await db.query('INSERT INTO star_rates_snapshots (version, content, content_hash) VALUES ($1, $2::jsonb, $3) ON CONFLICT (version) DO NOTHING', [
    version,
    JSON.stringify(content),
    hash,
  ]);
  if ((ins.rowCount ?? 0) > 0) return { version, created: true };
  const r = await db.query<{ content_hash: string }>('SELECT content_hash FROM star_rates_snapshots WHERE version = $1', [version]);
  if (r.rows[0]?.content_hash !== hash) throw new RatesVersionConflict(version);
  return { version, created: false };
}
