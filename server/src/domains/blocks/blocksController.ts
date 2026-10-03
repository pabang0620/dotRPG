import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import * as service from './blocksService';
import type { CharacterParams, IdParams } from './blocksValidation';

const acc = (res: Response) => getAccount(res.locals).id;

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.listBlocks(acc(res)));
  } catch (err) {
    next(err);
  }
}

export async function put(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, CharacterParams>(res);
    const r = await service.putBlock(acc(res), params.character_id);
    successResponse(res, r.data, '', r.status);
  } catch (err) {
    next(err);
  }
}

export async function remove(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, IdParams>(res);
    successResponse(res, await service.deleteBlock(acc(res), params.id));
  } catch (err) {
    next(err);
  }
}
