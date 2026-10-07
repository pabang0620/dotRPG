// 별조각 상품표(server/data/star_products.json, Docs/server/phase11_payments.md 5.1). 사람이 관리하는 서버 전용 데이터라
// 클라이언트에 내려가지 않고 data_version 해시에도 넣지 않는다(가격 변경이 클라이언트 데이터 업데이트를 요구하지 않게).
// [운영 전 확정] 가격·묶음·steam_item_id 는 모두 시작값이다.
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';
import { getConfig } from '../config/env';

const productSchema = z.strictObject({
  id: z.string().regex(/^[a-z0-9_]{3,40}$/),
  steam_item_id: z.number().int().positive(),
  name: z.string().min(1).max(60),
  stars: z.number().int().positive(),
  enabled: z.boolean(),
  sort: z.number().int(),
  prices: z.record(z.string().regex(/^[A-Z]{3}$/), z.number().int().positive()),
});

const catalogSchema = z.looseObject({
  version: z.string().min(1).max(40),
  products: z.array(productSchema),
});

export type StarProduct = z.infer<typeof productSchema>;
export interface StarCatalog {
  version: string;
  products: StarProduct[];
}

/** 검증 오류 목록(빈 배열 = 정상). 스키마 위반은 던지지 않고 문자열로 돌려준다 */
export function validateCatalog(raw: unknown): { catalog: StarCatalog | null; errors: string[] } {
  const p = catalogSchema.safeParse(raw);
  if (!p.success) return { catalog: null, errors: p.error.issues.map((i) => `${i.path.join('.')}: ${i.message}`) };
  const c = p.data;
  const errors: string[] = [];
  const ids = new Set<string>();
  const items = new Set<number>();
  for (const x of c.products) {
    if (ids.has(x.id)) errors.push(`id 중복: ${x.id}`);
    if (items.has(x.steam_item_id)) errors.push(`steam_item_id 중복: ${x.steam_item_id}`);
    ids.add(x.id);
    items.add(x.steam_item_id);
    if (x.enabled && Object.keys(x.prices).length === 0) errors.push(`판매 중인 상품에 가격이 없습니다: ${x.id}`);
  }
  return { catalog: { version: c.version, products: c.products }, errors };
}

let current: StarCatalog | null = null;

export function setStarCatalogForTest(c: StarCatalog | null): void {
  if (process.env.NODE_ENV === 'production') throw new Error('운영에서는 상품표를 바꿀 수 없습니다');
  current = c;
}

/** 파일이 없으면 빈 상품표(상품표 없이도 기동한다, 16절 켜는 순서 1). 형식이 틀리면 운영에서는 던지고 개발에서는 빈 표 */
export function loadStarCatalog(dir: string = getConfig().gameDataDir, strict: boolean = getConfig().nodeEnv === 'production'): StarCatalog {
  const file = path.join(dir, 'star_products.json');
  if (!fs.existsSync(file)) {
    current = { version: 'none', products: [] };
    return current;
  }
  const { catalog, errors } = validateCatalog(JSON.parse(fs.readFileSync(file, 'utf8')) as unknown);
  if (!catalog || errors.length > 0) {
    if (strict) throw new Error(`star_products.json 검증 실패: ${errors.join('; ')}`);
    current = { version: 'invalid', products: [] };
    return current;
  }
  current = catalog;
  return catalog;
}

export function getStarCatalog(): StarCatalog {
  return current ?? loadStarCatalog();
}

export const productById = (id: string): StarProduct | undefined => getStarCatalog().products.find((p) => p.id === id);
