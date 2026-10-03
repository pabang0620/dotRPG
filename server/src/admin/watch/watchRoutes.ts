import { Router } from 'express';
import { validate } from '../../middleware/validationMiddleware';
import { adminRate, audit, requireAdmin } from '../common/adminAuth';
import * as c from './watchController';
import { anomalyQuery, emptyQuery, flagQuery } from './watchValidation';

export function createWatchRouter(): Router {
  const r = Router();
  r.get('/admin/anomalies', audit({ action: 'watch.anomalies' }), requireAdmin('viewer'), adminRate('read'), validate({ query: anomalyQuery }), c.anomalies);
  r.get('/admin/auction-flags', audit({ action: 'watch.auction_flags' }), requireAdmin('viewer'), adminRate('read'), validate({ query: flagQuery }), c.flags);
  r.get('/admin/watchlist', audit({ action: 'watch.list' }), requireAdmin('viewer'), adminRate('read'), validate({ query: emptyQuery }), c.watchlist);
  return r;
}
