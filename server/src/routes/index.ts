import { Router } from 'express';
import { createAuthRouter } from '../domains/auth/authRoutes';
import { createCharacterRouter } from '../domains/characters/characterRoutes';
import { createSystemRouter } from '../domains/system/systemRoutes';

export function createRouter(): Router {
  const r = Router();
  r.use(createSystemRouter());
  r.use(createAuthRouter());
  r.use(createCharacterRouter());
  return r;
}
