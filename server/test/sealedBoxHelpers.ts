// 14단계 테스트 공용: 봉인된 상자 난수 고정, 계정·캐릭터 만들기
import type { Express } from 'express';
import request from 'supertest';
import { getSealedBox, sealedTable } from '../src/gamedata/sealedBox';
import { setRng } from '../src/utils/rng';
import { auth } from './helpers';
import { post, type Hero } from './economyHelpers';
import { heroOf, newPayer, seedStars, type Payer } from './payHelpers';

export const ROLL_SCALE = 100_000_000;

/** 표에서 rowId 칸의 가운데에 떨어지는 난수 값(rng.int(0, 1e8) 결과) */
export function rollOf(rowId: string, boosted: boolean): number {
  let acc = 0;
  for (const t of sealedTable(getSealedBox(), boosted)) {
    if (t.row.id === rowId) return Math.floor(((acc + t.rate / 2) / 100) * ROLL_SCALE);
    acc += t.rate;
  }
  throw new Error(`row ${rowId} 없음`);
}

/**
 * 상자 굴림(max 1e8)을 차례로 정한다. 목록을 다 쓰면 마지막 값을 되풀이한다.
 * 확률 상자 굴림(max 100)은 luck 값
 */
export function scriptRng(rolls: number[], luck = 0): void {
  const q = [...rolls];
  setRng({
    int: (min, max) => (max === ROLL_SCALE ? (q.length > 1 ? (q.shift() as number) : (q[0] as number)) : max === 100 ? luck : min),
    unit: () => 1,
  });
}

export interface Player {
  p: Payer;
  hero: Hero;
}

export async function newPlayer(app: Express, freeStars = 0): Promise<Player> {
  const p = await newPayer(app);
  const hero = await heroOf(app, p);
  if (freeStars > 0) await seedStars(p.accountId, freeStars);
  return { p, hero };
}

export const pullApi = (app: Express, pl: Player, count: 1 | 11, requestId?: string) => post(app, pl.hero, '/starshop/sealed/pull', { count }, requestId);
export const openApi = (app: Express, pl: Player, itemKey: string, requestId?: string) => post(app, pl.hero, '/items/open', { item_key: itemKey }, requestId);
export const sealedInfo = (app: Express, pl: Player) => request(app).get('/starshop/sealed').set(auth({ access: pl.p.access } as never));
