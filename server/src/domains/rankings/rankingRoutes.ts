import { Router, type NextFunction, type Request, type Response } from 'express';
import { z } from 'zod';
import { getPool } from '../../db/pool';
import { getAccount, requireAuth } from '../../middleware/authMiddleware';
import { rateLimit } from '../../middleware/rateLimiter';
import { getValidated, validate } from '../../middleware/validationMiddleware';
import { versionCheck } from '../../middleware/versionCheck';
import { AppError } from '../../utils/AppError';
import { successResponse } from '../../utils/response';
import * as charRepo from '../characters/characterRepository';
import * as guildRepo from '../guilds/guildRepository';
import { board, type Board, type Week } from './rankingService';

const params = z.object({ board: z.enum(['world_boss', 'guild_boss', 'power']) });
/** character_id(내 캐릭터 uuid)를 주면 me에 그 캐릭터(길드보스는 그 길드)의 순위가 온다 */
const query = z.object({ week: z.enum(['current', 'previous']).optional().default('current'), character_id: z.uuid().optional() });
const accountKey = (_req: unknown, locals: Record<string, unknown>): string => String(getAccount(locals).id);

export function createRankingRouter(): Router {
  const r = Router();
  const limit = rateLimit({ name: 'rankings', limit: (c) => c.rate.slowPerSec, windowMs: 1000, key: accountKey });
  r.get('/rankings/:board', versionCheck({ data: false }), requireAuth, limit, validate({ params, query }), async (_req: Request, res: Response, next: NextFunction) => {
    try {
      const v = getValidated<unknown, { board: Board }, { week: Week; character_id?: string }>(res);
      let me: { characterId: number; guildId: number | null } | null = null;
      if (v.query.character_id) {
        const c = await charRepo.findOwnedAlive(getPool(), getAccount(res.locals).id, v.query.character_id);
        if (!c) throw new AppError(404, '캐릭터를 찾을 수 없습니다.', 'CHARACTER_NOT_FOUND');
        me = { characterId: c.id, guildId: (await guildRepo.membershipOf(getPool(), c.id))?.guild_id ?? null };
      }
      successResponse(res, await board(v.params.board, v.query.week, me));
    } catch (err) {
      next(err);
    }
  });
  return r;
}
