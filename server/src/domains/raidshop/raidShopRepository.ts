import type { PoolClient } from 'pg';

export interface NewPurchase {
  accountId: number;
  characterId: number;
  requestId: string;
  productId: string;
  materialKey: string;
  materialCost: number;
  resultKey: string;
  resultRarity: string;
  ratesVersion: string;
}

export async function insertPurchase(client: PoolClient, p: NewPurchase): Promise<void> {
  await client.query(
    `INSERT INTO raid_shop_purchases (account_id, character_id, request_id, product_id, material_key, material_cost,
                                      result_key, result_rarity, rates_version)
     VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)`,
    [p.accountId, p.characterId, p.requestId, p.productId, p.materialKey, p.materialCost, p.resultKey, p.resultRarity, p.ratesVersion],
  );
}
