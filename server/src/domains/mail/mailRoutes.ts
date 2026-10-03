import { Router } from 'express';
import { rateLimit } from '../../middleware/rateLimiter';
import { validate } from '../../middleware/validationMiddleware';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as c from './mailController';
import { claimAllBody, claimBody, listQuery, mailParams, summaryQuery } from './mailValidation';

export function createMailRouter(): Router {
  const r = Router();
  const listLimit = rateLimit({ name: 'mail-list', limit: (x) => x.rate.auctionMinePerSec, windowMs: 1000, key: charKey });
  const claimLimit = rateLimit({ name: 'mail-claim', limit: (x) => x.rate.mailClaimPerSec, windowMs: 1000, key: charKey });
  const claimAllLimit = rateLimit({ name: 'mail-claim-all', limit: (x) => x.rate.mailClaimAllPerSec, windowMs: 1000, key: charKey });
  const summaryLimit = rateLimit({ name: 'mail-summary', limit: (x) => x.rate.mailSummaryPer5Sec, windowMs: 5000, key: charKey });

  r.get('/characters/:uuid/mail', listLimit, validate({ params: charParams, query: listQuery }), c.list);
  // 고정 경로(summary, claim-all)를 /:mail_id 보다 먼저 둔다
  r.get('/characters/:uuid/mail/summary', summaryLimit, validate({ params: charParams, query: summaryQuery }), c.summary);
  r.post('/characters/:uuid/mail/claim-all', claimAllLimit, validate({ params: charParams, body: claimAllBody }), c.claimAll);
  r.post('/characters/:uuid/mail/:mail_id/claim', claimLimit, validate({ params: mailParams, body: claimBody }), c.claim);
  return r;
}
