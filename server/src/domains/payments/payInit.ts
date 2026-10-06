// 서버 기동 때 한 번(Docs/server/phase11_payments.md 16절 기동 검사 4~5번, E12): 상품표 검증, 확률표 스냅샷(같은 버전에 다른 내용이면 기동 거부), 경고.
// 상품표가 없어도 기동한다(켜는 순서 1: 플래그 꺼진 채 서버를 먼저 배포한다).
import { getConfig } from '../../config/env';
import { getPool } from '../../db/pool';
import { loadStarCatalog } from '../../gamedata/starProducts';
import { logger } from '../../utils/logger';
import { ensureRatesSnapshot } from '../starshop/ratesSnapshot';

export async function initPayments(): Promise<void> {
  const cfg = getConfig();
  const catalog = loadStarCatalog();
  const snap = await ensureRatesSnapshot(getPool());
  logger.info({ rates_version: snap.version, snapshot_created: snap.created, products: catalog.products.length, catalog_version: catalog.version, payments_enabled: cfg.pay.enabled }, 'payments.init');
  const biggest = catalog.products.filter((p) => p.enabled).reduce((a, p) => Math.max(a, p.stars), 0);
  if (biggest > cfg.pay.limits.new.daily) logger.warn({ biggest, new_daily_limit: cfg.pay.limits.new.daily }, 'payments.product_exceeds_new_daily_limit');
  if (cfg.pay.enabled) {
    const owners = await getPool().query<{ n: string }>("SELECT count(*) AS n FROM admin_users WHERE role = 'owner' AND disabled_at IS NULL");
    if (Number(owners.rows[0]?.n ?? 0) < 2) logger.warn('payments.fewer_than_two_active_owners');
    if (cfg.pay.mode === 'mock') logger.warn('payments.mock_steam_mode: 가짜 Steam으로 결제가 동작합니다(시험 전용)');
  }
}
