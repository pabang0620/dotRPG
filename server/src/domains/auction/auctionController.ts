import type { NextFunction, Request, Response } from 'express';
import { getAccount } from '../../middleware/authMiddleware';
import { getValidated } from '../../middleware/validationMiddleware';
import { successResponse } from '../../utils/response';
import { sendStored } from '../economy/economyController';
import { charParams } from '../economy/economyValidation';
import type { z } from 'zod';
import * as query from './auctionQueryService';
import * as service from './auctionService';
import type { CancelQuery, BidBody, BuyoutBody, ListBody, ListingParams, PricesParams, PricesQuery, SearchQuery } from './auctionValidation';

type CharP = z.infer<typeof charParams>;

export async function search(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query: q } = getValidated<unknown, CharP, SearchQuery>(res);
    const r = await query.searchAuction(getAccount(res.locals).id, params.uuid, q);
    successResponse(res, r.data, '', 200, r.meta);
  } catch (err) {
    next(err);
  }
}

export async function prices(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query: q } = getValidated<unknown, PricesParams, PricesQuery>(res);
    successResponse(res, await query.priceOf(getAccount(res.locals).id, params.uuid, params.item_key, q));
  } catch (err) {
    next(err);
  }
}

export async function sellable(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, CharP>(res);
    successResponse(res, await query.sellable(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function mine(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params } = getValidated<unknown, CharP>(res);
    successResponse(res, await query.mine(getAccount(res.locals).id, params.uuid));
  } catch (err) {
    next(err);
  }
}

export async function list(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<ListBody, CharP>(res);
    sendStored(res, await service.listItem(getAccount(res.locals).id, params.uuid, body));
  } catch (err) {
    next(err);
  }
}

export async function cancel(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { params, query: q } = getValidated<unknown, ListingParams, CancelQuery>(res);
    successResponse(res, await service.cancelListing(getAccount(res.locals).id, params.uuid, params.listing_id, q.request_id ?? null));
  } catch (err) {
    next(err);
  }
}

export async function buyout(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<BuyoutBody, ListingParams>(res);
    sendStored(res, await service.buyout(getAccount(res.locals).id, params.uuid, params.listing_id, body));
  } catch (err) {
    next(err);
  }
}

export async function bid(_req: Request, res: Response, next: NextFunction): Promise<void> {
  try {
    const { body, params } = getValidated<BidBody, ListingParams>(res);
    sendStored(res, await service.bid(getAccount(res.locals).id, params.uuid, params.listing_id, body));
  } catch (err) {
    next(err);
  }
}
