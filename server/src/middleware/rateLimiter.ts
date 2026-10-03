import type { Request, RequestHandler } from 'express';
import { getConfig, type AppConfig } from '../config/env';
import { AppError } from '../utils/AppError';

/** 나중에 Redis로 바꿀 수 있게 인터페이스 뒤에 둔다. 슬라이딩 윈도우(시각 목록). */
export interface RateLimitStore {
  /** 창 안의 횟수와, 가장 오래된 기록이 빠질 때까지 남은 초 */
  count(key: string, windowMs: number): { count: number; retryAfterSec: number };
  add(key: string, windowMs: number): void;
  reset(key: string): void;
  clear(): void;
}

export class MemoryRateLimitStore implements RateLimitStore {
  private hits = new Map<string, number[]>();

  private prune(key: string, windowMs: number): number[] {
    const now = Date.now();
    const list = (this.hits.get(key) ?? []).filter((t) => now - t < windowMs);
    if (list.length === 0) this.hits.delete(key);
    else this.hits.set(key, list);
    return list;
  }

  count(key: string, windowMs: number): { count: number; retryAfterSec: number } {
    const list = this.prune(key, windowMs);
    const oldest = list[0];
    const retryAfterSec = oldest === undefined ? 0 : Math.max(1, Math.ceil((oldest + windowMs - Date.now()) / 1000));
    return { count: list.length, retryAfterSec };
  }

  add(key: string, windowMs: number): void {
    const list = this.prune(key, windowMs);
    list.push(Date.now());
    this.hits.set(key, list);
    if (this.hits.size > 50_000) this.sweep(windowMs);
  }

  reset(key: string): void {
    this.hits.delete(key);
  }

  clear(): void {
    this.hits.clear();
  }

  // 메모리 폭주 방지: 가장 긴 창(1시간) 기준으로 오래된 키를 버린다
  private sweep(windowMs: number): void {
    const limit = Math.max(windowMs, 3_600_000);
    const now = Date.now();
    for (const [k, list] of this.hits) {
      const last = list[list.length - 1];
      if (last === undefined || now - last >= limit) this.hits.delete(k);
    }
  }
}

let store: RateLimitStore = new MemoryRateLimitStore();
export function getRateLimitStore(): RateLimitStore {
  return store;
}
export function setRateLimitStore(s: RateLimitStore): void {
  store = s;
}

export const MINUTE = 60_000;
export const HOUR = 3_600_000;

/** 한도를 넘겼으면 429를 던지고, 아니면 횟수를 1 올린다. */
export function consumeOrThrow(key: string, limit: number, windowMs: number): void {
  const s = getRateLimitStore();
  const { count, retryAfterSec } = s.count(key, windowMs);
  if (count >= limit) {
    throw new AppError(429, '요청이 너무 많습니다. 잠시 후 다시 시도해 주세요.', 'RATE_LIMITED', {
      retry_after_sec: retryAfterSec,
    });
  }
  s.add(key, windowMs);
}

export function rateLimit(opts: {
  name: string;
  limit: (c: AppConfig) => number;
  windowMs: number;
  key: (req: Request, locals: Record<string, unknown>) => string;
}): RequestHandler {
  return (req, res, next) => {
    try {
      consumeOrThrow(`${opts.name}:${opts.key(req, res.locals)}`, opts.limit(getConfig()), opts.windowMs);
      next();
    } catch (err) {
      next(err);
    }
  };
}

export const ipKey = (req: Request): string => req.ip ?? 'unknown';
