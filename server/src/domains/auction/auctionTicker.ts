// 마감 정산 틱(phase6_api.md 10절): 1분 틱 + 요청 시 지연 정산. 정산은 listing 한 건당 트랜잭션 하나다.
import { getConfig } from '../../config/env';
import { getPool, withTransaction } from '../../db/pool';
import { getGameData } from '../../gamedata/loader';
import { getNow } from '../../utils/clock';
import { metrics } from '../../ops/metrics';
import { logger } from '../../utils/logger';
import { insertItemLedger } from '../economy/economyRepository';
import { expireAttachments } from '../mail/mailAttachments';
import * as mailRepo from '../mail/mailRepository';
import * as repo from './auctionRepository';
import { settleEnded } from './auctionSettle';

/** 마감된 등록 한 건을 정산한다. 이미 정산됐거나 다른 곳이 쓰는 중이면 false */
export async function settleListingById(id: number): Promise<boolean> {
  return withTransaction(async (client) => {
    const now = getNow();
    const l = await repo.lockDueListing(client, id, now);
    if (!l) return false;
    const result = await settleEnded({ client, now, requestId: null }, l);
    logger.info(
      { listing: l.uuid, result, price: l.currentBid, fee_pct: l.feePct },
      'auction.settled',
    );
    return true;
  });
}

/** 기한이 지난 미수령 우편 한 통을 폐기한다(첨부 아이템은 mail_expire 원장, 골드는 소각 기록) */
async function expireMailById(id: number): Promise<boolean> {
  return withTransaction(async (client) => {
    const now = getNow();
    const m = await mailRepo.lockExpiredMail(client, id, now);
    if (!m) return false;
    await mailRepo.markExpired(client, m.id, now);
    if (m.itemKey !== null && m.count !== null) {
      await insertItemLedger(client, m.characterId, m.itemKey, -m.count, 0, 'mail', 'mail_expire', m.uuid, null);
    }
    await repo.insertSink(client, 'mail_expire', m.gold, m.listingId, m.id, m.characterId, now);
    // 10단계 E9: 첨부 표가 있는 우편(캠페인 우편)의 아이템 -n, 골드 소각 기록
    if (m.attachN > 0) await expireAttachments(client, m, now);
    return true;
  });
}

/** 내 캐릭터가 판매자이거나 최고 입찰자인 마감 지난 등록을 먼저 정산한다(요청자 캐릭터 행은 잠그지 않는다) */
export async function settleDueOfCharacter(characterId: number): Promise<void> {
  const ids = await repo.dueListingIdsOfCharacter(getPool(), characterId, getNow());
  for (const id of ids) {
    try {
      await settleListingById(id);
    } catch (err) {
      logger.error({ err, listing_id: id }, 'auction.settle_failed');
    }
  }
}

export interface TickResult {
  settled: number;
  failed: number;
  mailsExpired: number;
  lagSeconds: number;
}

/** 틱 한 번: 마감 정산(최대 AUCTION_TICK_BATCH건) -> 기한 지난 우편 폐기. 한 건의 실패는 로그만 남기고 다음 건으로 간다 */
export async function runAuctionTick(): Promise<TickResult> {
  const cfg = getConfig().auction;
  const now = getNow();
  const res: TickResult = { settled: 0, failed: 0, mailsExpired: 0, lagSeconds: 0 };
  const due = await repo.dueListingIds(getPool(), now, cfg.tickBatch);
  if (due.length > 0) {
    // 가장 늦게 정산되는 건의 지연(정산 전에 잰다). 지연이 길어도 결과는 틀리지 않는다(입찰·구매는 ends_at으로 직접 막힌다)
    const oldest = await getPool().query<{ lag: number }>(
      `SELECT coalesce(max(extract(epoch FROM ($1::timestamptz - ends_at))), 0)::float AS lag
         FROM auction_listings WHERE status = 'active' AND ends_at <= $1`,
      [now],
    );
    res.lagSeconds = (oldest.rows[0] as { lag: number }).lag;
    if (res.lagSeconds > 3 * cfg.tickSeconds) logger.warn({ lag_seconds: res.lagSeconds }, 'auction.tick_lag');
  }
  for (const id of due) {
    try {
      if (await settleListingById(id)) res.settled++;
    } catch (err) {
      res.failed++;
      logger.error({ err, listing_id: id }, 'auction.settle_failed');
    }
  }
  const mails = await mailRepo.expiredMailIds(getPool(), now, cfg.tickBatch);
  for (const id of mails) {
    try {
      if (await expireMailById(id)) res.mailsExpired++;
    } catch (err) {
      logger.error({ err, mail_id: id }, 'mail.expire_failed');
    }
  }
  return res;
}

/** 서버 기동 시 한 번 호출한다: 즉시 1회(꺼져 있던 동안 마감된 건 복구) + 주기 실행. 겹쳐 돌지 않는다 */
export function startAuctionTicker(): () => void {
  const cfg = getConfig().auction;
  getGameData();
  let running = false;
  const run = (): void => {
    if (running) return;
    running = true;
    metrics
      .track('auction', runAuctionTick)
      .then((r) => {
        metrics.auctionLagSeconds = r.lagSeconds;
      })
      .catch((err: unknown) => logger.error({ err }, 'auction tick failed'))
      .finally(() => {
        running = false;
      });
  };
  run();
  const timer = setInterval(run, cfg.tickSeconds * 1000);
  timer.unref();
  return () => clearInterval(timer);
}
