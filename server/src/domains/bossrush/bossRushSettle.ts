// 주간 정산(boss-settle 작업): 끝난 주의 시즌을 한 번만 정산한다. 시즌 행 잠금(SKIP LOCKED) + settled_at.
// 월드보스: 참가 보상(골드·강화석), 격파 보상, 1~100위 칭호·이름 색. 길드보스: 참가 골드, 1위 길드 칭호, 1~10위 길드 이름 색.
import { randomUUID } from 'node:crypto';
import type { PoolClient } from 'pg';
import { withTransaction } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { getNow } from '../../utils/clock';
import { createSystemMail } from '../mail/mailRepository';
import * as guildRepo from '../guilds/guildRepository';
import { clearRankingCache, ranked } from '../rankings/rankingService';
import * as repo from './bossRushRepository';
import {
  GUILD_JOIN_GOLD, ORE_ITEM, WORLD_DEFEAT_GOLD, WORLD_JOIN_GOLD, WORLD_JOIN_ORE, guildRankColor, worldRankReward,
} from './bossRushRules';

const MAIL_DAYS = 30;
const WEEK_MS = 7 * 24 * 60 * 60 * 1000;
/** 칭호·이름 색은 다음 정산 뒤까지 끊기지 않게 한 주 + 1시간 */
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
  const until = new Date(s.week_end.getTime() + WEEK_MS + HONOR_EXTRA_MS);
  const people = await repo.participants(client, s.id);
  for (const p of people) {
    await mailGold(client, p.character_id, 'world_boss', WORLD_JOIN_GOLD + (s.defeated_at ? WORLD_DEFEAT_GOLD : 0), now);
    await mailItem(client, p.character_id, 'world_boss', ORE_ITEM, WORLD_JOIN_ORE, now);
  }
  const rows = (await ranked('world_boss', s.week_start, { fresh: true, db: client })).rows;
  for (let i = 0; i < rows.length && i < 100; i++) {
    const reward = worldRankReward(i + 1);
    if (reward) await repo.grantTitle(client, (rows[i] as { key: number }).key, s.id, reward.title, reward.color, until);
  }
  return people.length;
}

async function settleGuild(client: PoolClient, s: repo.Season, now: Date): Promise<number> {
  const until = new Date(s.week_end.getTime() + WEEK_MS + HONOR_EXTRA_MS);
  // 그 주에 길드를 옮겨 두 길드에서 도전했어도 참가 보상은 한 번
  const people = [...new Set((await repo.participants(client, s.id)).map((p) => p.character_id))];
  for (const id of people) await mailGold(client, id, 'guild_boss', GUILD_JOIN_GOLD, now);
  const rows = (await ranked('guild_boss', s.week_start, { fresh: true, db: client })).rows;
  for (let i = 0; i < rows.length && i < 10; i++) {
    const guildId = (rows[i] as { key: number }).key;
    const color = guildRankColor(i + 1);
    if (color) await repo.setGuildHonor(client, guildId, color, until);
    if (i === 0) for (const id of await guildRepo.memberIds(client, guildId)) await repo.grantTitle(client, id, s.id, 'rank_guild_1', null, until);
  }
  return people.length;
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
