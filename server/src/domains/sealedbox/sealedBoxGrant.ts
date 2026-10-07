// 상자·패스 보상 지급: 아이템은 이 캐릭터의 가방, 던전 클리어권은 계정 지갑(가방에 넣을 수 없다).
// 호출 쪽이 runEconomy 안(캐릭터 행을 잠근 트랜잭션)에서 부른다.
import { getGameData } from '../../gamedata/loader';
import { isSweepTicketKey } from '../../gamedata/sweepData';
import type { EconCtx } from '../economy/economyContext';
import * as tickets from '../sweep/ticketWallet';

export interface ItemCount {
  item_key: string;
  count: number;
}

/** 같은 키를 합쳐 원장 줄을 줄이고 지급한다 */
export async function grantItems(ctx: EconCtx, items: ItemCount[], reason: 'sealed_box' | 'box_open' | 'pass_reward', ref: string): Promise<void> {
  const sum = new Map<string, number>();
  for (const i of items) sum.set(i.item_key, (sum.get(i.item_key) ?? 0) + i.count);
  const sweep = getGameData().sweep;
  for (const [key, n] of sum) {
    if (isSweepTicketKey(sweep, key)) {
      // 락 순서 ②: 캐릭터 행 다음에 계정 행
      await tickets.lockAccount(ctx.client, ctx.char.accountId);
      await tickets.addNormal(ctx.client, { accountId: ctx.char.accountId, characterId: ctx.char.id, requestId: ctx.requestId, now: ctx.now }, n, 'sealed_box', ref);
    } else {
      await ctx.addItem('bag', key, n, reason, ref);
    }
  }
}
