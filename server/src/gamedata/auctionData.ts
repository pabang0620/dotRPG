import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';

// 6단계(경매): auction.json 검증. 비율은 basis point 정수다(phase6_mapping.md 3.1).
const posInt = z.number().int().positive();

const auctionSchema = z.looseObject({
  schema: z.literal(1),
  durations: z.array(posInt).min(1),
  feePct: z.looseObject({ equipment: z.number().int().min(0).max(50), stack: z.number().int().min(0).max(50) }),
  depositBps: z.number().int().min(0),
  depositMin: z.number().int().min(0),
  depositMax: posInt,
  maxListings: posInt,
  minBidStepBps: z.number().int().min(10000),
  priceFloorBps: posInt,
  priceCeilBps: posInt,
  maxStack: z.number().int().min(1).max(9999),
  extendWindowMinutes: z.number().int().min(0),
  extendMinutes: z.number().int().min(0),
  extendMax: z.number().int().min(0),
  mailDays: posInt,
  timeBands: z.array(posInt).min(1),
});

export type AuctionData = z.infer<typeof auctionSchema>;

export function loadAuctionData(dir: string): AuctionData {
  let raw: unknown;
  try {
    raw = JSON.parse(fs.readFileSync(path.join(dir, 'auction.json'), 'utf8'));
  } catch (err) {
    throw new Error(`게임 데이터 auction.json 을 읽을 수 없습니다: ${(err as Error).message}`);
  }
  const r = auctionSchema.safeParse(raw);
  if (!r.success) {
    const detail = r.error.issues
      .slice(0, 5)
      .map((i) => `${i.path.join('.')}: ${i.message}`)
      .join('; ');
    throw new Error(`게임 데이터 auction.json 검증 실패: ${detail}`);
  }
  const d = r.data;
  if (d.depositMin > d.depositMax) throw new Error('게임 데이터 검증 실패: auction.depositMin 이 depositMax 보다 큽니다');
  if (d.priceFloorBps > d.priceCeilBps) throw new Error('게임 데이터 검증 실패: auction.priceFloorBps 가 priceCeilBps 보다 큽니다');
  return d;
}
