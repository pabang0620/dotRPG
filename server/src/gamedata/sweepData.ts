// 10단계: sweep.json(던전 클리어권 소탕 값). 값의 원본은 Unity의 소탕 상수이고 내보내기가 파일을 만든다.
// SWEEP_ENABLED가 켜지면 파일이 없거나 형식이 다를 때 기동을 멈춘다. 꺼져 있으면 파일이 없어도 null로 둔다.
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';

const posInt = z.number().int().positive();

const sweepSchema = z.looseObject({
  schema: z.literal(1),
  ticketItem: z.string().min(1),
  eventTicketItem: z.string().min(1),
  eventTicketDays: posInt,
  /** 소탕 자격 최고 등급(번호, 작을수록 좋다. B = 4) */
  minRank: z.number().int().min(0).max(8),
  xpBonusPercent: z.number().min(-100).max(1000),
  /** 서버는 카드 한 장 소탕만 지원한다(선택 단계가 없다) */
  cardCount: z.literal(1),
  gearKeepPercent: z.number().int().min(0).max(100),
  shop: z.looseObject({ basePrice: posInt, weeklyLimit: posInt }),
  weekly: z.looseObject({ directClears: posInt, rewardTickets: posInt }),
});

export type SweepData = z.infer<typeof sweepSchema>;

export function loadSweepData(dir: string, required: boolean, itemIds: Set<string>): SweepData | null {
  const full = path.join(dir, 'sweep.json');
  let raw: unknown;
  try {
    raw = JSON.parse(fs.readFileSync(full, 'utf8'));
  } catch (err) {
    if (!required) return null;
    throw new Error(`게임 데이터 sweep.json 을 읽을 수 없습니다: ${(err as Error).message}`);
  }
  const r = sweepSchema.safeParse(raw);
  if (!r.success) {
    if (!required) return null;
    const detail = r.error.issues
      .slice(0, 5)
      .map((i) => `${i.path.join('.')}: ${i.message}`)
      .join('; ');
    throw new Error(`게임 데이터 sweep.json 검증 실패: ${detail}`);
  }
  const d = r.data;
  if (d.ticketItem === d.eventTicketItem) throw new Error('게임 데이터 검증 실패: sweep 클리어권 키 두 개가 같습니다');
  for (const k of [d.ticketItem, d.eventTicketItem]) {
    if (required && !itemIds.has(k)) throw new Error(`게임 데이터 검증 실패: sweep 클리어권 ${k} 이 items.json에 없습니다`);
  }
  return d;
}

/** 클리어권 표시용 키인가(가방·창고·경매에 들어가면 안 되는 키) */
export function isSweepTicketKey(sweep: SweepData | null, key: string): boolean {
  return sweep !== null && (key === sweep.ticketItem || key === sweep.eventTicketItem);
}
