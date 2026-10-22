import type { NextFunction, Request, Response } from 'express';
import { successResponse } from '../../utils/response';
import * as service from './contentService';

/** GET /content/chapter3: 데이터 버전을 ETag로 쓴다. 같은 버전을 이미 받은 클라이언트에는 304 */
export function chapter3(req: Request, res: Response, next: NextFunction): void {
  try {
    const content = service.getChapter3Content();
    const etag = `"ch3-${content.data_version}"`;
    res.setHeader('ETag', etag);
    res.setHeader('Cache-Control', 'private, no-cache');
    if (req.headers['if-none-match'] === etag) {
      res.status(304).end();
      return;
    }
    successResponse(res, content);
  } catch (err) {
    next(err);
  }
}
