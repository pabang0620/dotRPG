// 시즌 정산(boss-settle 작업): 끝난 시즌을 한 번만 정산한다. 시즌 행 잠금(SKIP LOCKED) + settled_at.
// 월드보스(2주 시즌): 참가 보상(골드·강화석), 격파 보상, 1~100위 영구 칭호·이름 색.
// 길드보스(1주): 참가 골드, 1위 길드 칭호, 1~10위 길드 이름 색, 1~10위 길드원 순위 보상(2026-10-30).
import { randomUUID } from 'node:crypto';
import type { PoolClient } from 'pg';
import { withTransaction } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { getNow } from '../../utils/clock';
import { createSystemMail } from '../mail/mailRepository';
import { clearRankingCache, ranked } from '../rankings/rankingService';
import * as repo from './bossRushRepository';
import {
  GUILD_JOIN_GOLD, ORE_ITEM, WORLD_DEFEAT_GOLD, WORLD_JOIN_GOLD, WORLD_JOIN_ORE, guildRankColor, guildTierReward, worldRankReward,
} from './bossRushRules';

const MAIL_DAYS = 30;
const WEEK_MS = 7 * 24 * 60 * 60 * 1000;
/** 길드보스 칭호·길드 이름 색은 다음 정산 뒤까지 끊기지 않게 한 주 + 1시간(월드보스 칭호는 영구) */
const HONOR_EXTRA_MS = 60 * 60 * 1000;

async function mailGold(client: PoolClient, characterId: number, code: 'world_boss' | 'guild_boss', gold: number, now: Date): Promise<void> {
  if (gold <= 0) return;
  await createSystemMail(client, { characterId, systemCode: code, gold, item: null, now, expiresAt: new Date(now.getTime() + MAIL_DAYS * 86_400_000), requestId: randomUUID() });
}

async function mailItem(client: PoolClient, characterId: number, code: 'world_boss' | 'guild_boss', itemKey: string, count: number, now: Date): Promise<void> {
  const bind = getGameData().economy.items.get(itemKey)?.bind ?? 'none';
  await createSystemMail(client, { characterId, systemCode: code, gold: 0, item: { itemKey, count, bind }, now, expiresAt: new Date(now.getTime() + MAIL_DAYS * 86_400_000), requestId: randomUUID() });
}

async function settleWorld(client: PoolClient, s: repo.Season, now: Date): Promise<number> {
  const people = await repo.participants(client, s.id);
  for (const p of people) {
    await mailGold(client, p.character_id, 'world_boss', WORLD_JOIN_GOLD + (s.defeated_at ? WORLD_DEFEAT_GOLD : 0), now);
    await mailItem(client, p.character_id, 'world_boss', ORE_ITEM, WORLD_JOIN_ORE, now);
  }
  const rows = (await ranked('world_boss', s.week_start, { fresh: true, db: client })).rows;
  for (let i = 0; i < rows.length && i < 100; i++) {
    const reward = worldRankReward(i + 1);
    // 월드보스 순위 칭호는 영구(2026-10-30 사용자 결정): 만료 없이 언제든 장착한다
    if (reward) await repo.grantTitle(client, (rows[i] as { key: number }).key, s.id, reward.title, reward.color, null);
  }
  return people.length;
}

async function settleGuild(client: PoolClient, s: repo.Season, now: Date): Promise<number> {
  const until = new Date(s.week_end.getTime() + WEEK_MS + HONOR_EXTRA_MS);
  // 그 주에 길드를 옮겨 두 길드에서 도전했어도 참가 보상은 한 번
  const attempts = await repo.participants(client, s.id);
  const people = [...new Set(attempts.map((p) => p.character_id))];
  for (const id of people) await mailGold(client, id, 'guild_boss', GUILD_JOIN_GOLD, now);
  const rows = (await ranked('guild_boss', s.week_start, { fresh: true, db: client })).rows;
  for (let i = 0; i < rows.length && i < 10; i++) {
    const guildId = (rows[i] as { key: number }).key;
    const color = guildRankColor(i + 1);
    if (color) await repo.setGuildHonor(client, guildId, color, until);
    // 1위 칭호는 정산 시각의 길드원이 아니라 그 주에 그 길드 소속으로 도전을 끝낸 사람에게 준다
    // (주가 끝난 뒤 들어온 사람은 못 받고, 기여하고 떠난 사람은 받는다)
    if (i === 0) {
      const winners = new Set(attempts.filter((p) => p.guild_id === guildId).map((p) => p.character_id));
      for (const id of winners) await repo.grantTitle(client, id, s.id, 'rank_guild_1', null, until);
    }
  }
  // 1~10위 길드원 순위 보상(2026-10-30): 정산 시각에 그 길드에 있고 주가 끝나기 전에 가입했으며 이 시즌에 그 길드로 도전을 끝낸 길드원만.
  // 한 사람은 한 길드에만 속하지만, 같은 정산에서 두 번 받지 않게 순위 높은 길드부터 한 번만 준다
  const rewarded = new Set<number>();
  for (let i = 0; i < rows.length && i < 10; i++) {
    const tier = guildTierReward(i + 1);
    if (!tier) continue;
    for (const id of await repo.guildMembersBefore(client, (rows[i] as { key: number }).key, s.week_end, s.id)) {
      if (rewarded.has(id)) continue;
      rewarded.add(id);
      await mailGold(client, id, 'guild_boss', tier.gold, now);
      for (const it of tier.items) await mailItem(client, id, 'guild_boss', it.itemKey, it.count, now);
    }
  }
  return people.length;
}

/** 만료된 랭킹 칭호를 장착한 캐릭터에서 뗀다(매일 작업 rank-title-expire. 정산 때도 한 번 더 돈다) */
export async function unequipExpiredRankTitles(): Promise<number> {
  return withTransaction((client) => repo.unequipExpiredTitles(client, getNow()));
}

/** 정산할 시즌이 없을 때까지 하나씩(시즌마다 트랜잭션 하나) */
export async function settleDueSeasons(): Promise<{ seasons: number; people: number }> {
  let seasons = 0;
  let people = 0;
  for (;;) {
    const n = await withTransaction(async (client) => {
      const now = getNow();
      const s = await repo.lockDueSeason(client, now);
      if (!s) return -1;
      const count = s.kind === 'world' ? await settleWorld(client, s, now) : await settleGuild(client, s, now);
      await repo.markSettled(client, s.id, now);
      await repo.unequipExpiredTitles(client, now);
      return count;
    });
    if (n < 0) break;
    seasons++;
    people += n;
  }
  if (seasons > 0) clearRankingCache();
  return { seasons, people };
}
