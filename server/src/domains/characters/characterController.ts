import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { replayResponse, successResponse } from '../../utils/response';
import type { CharacterParams, CreateCharacterBody, StateBody } from './characterValidation';
import * as service from './characterService';

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    successResponse(res, await service.listCharacters(getAccount(res.locals).id));
  } catch (err) {
    next(err);
  }
}

export async function create(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body } = getValidated<CreateCharacterBody>(res);
    const r = await service.createCharacter(getAccount(res.locals).id, body);
    if (r.replay) replayResponse(res, r.status, r.body);
    else successResponse(res, r.body.data, r.body.message, r.status);
  } catch (err) {
    next(err);
  }
}

export async function get(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, CharacterParams>(res);
    successResponse(res, await service.getCharacter(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function remove(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, CharacterParams>(res);
    successResponse(res, await service.deleteCharacter(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function saveState(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<StateBody, CharacterParams>(res);
    successResponse(res, await service.saveState(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}
