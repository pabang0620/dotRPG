import { Router, type NextFunction, type Request, type Response } from 'express';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { rateLimit } from '../../middleware/rateLimiter';
import { getValidated, validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import { charKey } from '../economy/economyRouting';
import { charParams } from '../economy/economyValidation';
import * as service from './guildService';
import {
  applyBody, createBody, patchBody, requestBody, searchQuery,
  type ApplyBody, type CreateBody, type PatchBody, type RequestBody, type SearchQuery,
} from './guildValidation';
import { z } from 'zod';

type Handler = (req: Request, res: Response, next: NextFunction) => Promise<void>;
const wrap = (fn: (res: Response) => Promise<void>): Handler => async (_req, res, next) => {
  try { await fn(res); } catch (err) { next(err); }
};
const accountKey = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);

/** 길드(Docs/PLAN_GUILD_WORLDBOSS_2026-10-20.md 7절). /characters 아래는 공통 버전·인증 미들웨어 뒤에 붙는다 */
export function createGuildRouter(): Router {
  const r = Router();
  const read = rateLimit({ name: 'guild-read', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey });
  const write = rateLimit({ name: 'guild-write', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: charKey });
  const base = '/characters/:uuid/guild';
  const acct = (res: Response) => getAccount(res.locals).id;

  r.get(base, read, validate({ params: charParams }), wrap(async (res) => {
    const { params } = getValidated<unknown, { uuid: string }>(res);
    successResponse(res, await service.status(acct(res), params.uuid));
  }));
  r.post(base, write, validate({ params: charParams, body: createBody }), wrap(async (res) => {
    const { params, body } = getValidated<CreateBody, { uuid: string }>(res);
    sendStored(res, await service.create(acct(res), params.uuid, body));
  }));
  r.patch(base, write, validate({ params: charParams, body: patchBody }), wrap(async (res) => {
    const { params, body } = getValidated<PatchBody, { uuid: string }>(res);
    sendStored(res, await service.patch(acct(res), params.uuid, body));
  }));
  r.post(`${base}/apply`, write, validate({ params: charParams, body: applyBody }), wrap(async (res) => {
    const { params, body } = getValidated<ApplyBody, { uuid: string }>(res);
    sendStored(res, await service.apply(acct(res), params.uuid, body));
  }));
  for (const action of ['leave', 'disband'] as const) {
    r.post(`${base}/${action}`, write, validate({ params: charParams, body: requestBody }), wrap(async (res) => {
      const { params, body } = getValidated<RequestBody, { uuid: string }>(res);
      sendStored(res, await service.leave(acct(res), params.uuid, action, body));
    }));
  }
  const appParams = charParams.extend({ id: z.uuid() });
  for (const action of ['accept', 'reject'] as const) {
    r.post(`${base}/applications/:id/${action}`, write, validate({ params: appParams, body: requestBody }), wrap(async (res) => {
      const { params, body } = getValidated<RequestBody, { uuid: string; id: string }>(res);
      sendStored(res, await service.decide(acct(res), params.uuid, params.id, action, body));
    }));
  }
  const memberParams = charParams.extend({ characterId: z.uuid() });
  for (const action of ['kick', 'promote', 'demote', 'transfer'] as const) {
    r.post(`${base}/members/:characterId/${action}`, write, validate({ params: memberParams, body: requestBody }), wrap(async (res) => {
      const { params, body } = getValidated<RequestBody, { uuid: string; characterId: string }>(res);
      sendStored(res, await service.memberAction(acct(res), params.uuid, params.characterId, action, body));
    }));
  }

  // 길드 찾기는 계정 단위(캐릭터 경로 밖): 클라이언트 버전만 검사
  const list = rateLimit({ name: 'guild-search', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: accountKey });
  r.get('/guilds', versionCheck({ data: false }), requireAuth, list, validate({ query: searchQuery }), wrap(async (res) => {
    const { query } = getValidated<unknown, unknown, SearchQuery>(res);
    successResponse(res, await service.search(query));
  }));
  return r;
}
