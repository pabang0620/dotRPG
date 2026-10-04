import { z } from 'zod';
import { charParams, requestId } from '../economy/economyValidation';

export { charParams };

export const roomParams = z.object({ uuid: z.uuid(), kind: z.enum(['run', 'field']), id: z.uuid() });

const transportName = z.enum(['relay', 'steam', 'dev']);
const reasonEnum = z.enum(['connect_timeout', 'connect_failed', 'repeated_drop', 'host_unreachable']);

/** T1: 본문 없음(받지 않는 값: 좌석, 호스트, 시각) */
export const ticketBody = z.strictObject({});

/**
 * T2 `POST .../rooms/{kind}/{id}/transport` (클라이언트가 쓰는 형태): 실패한 전송과 본 전송 세대.
 * failed 는 실패한 전송 이름(또는 true: 현재 전송이 실패). 받지 않는 값: 원하는 전송, 시각
 */
export const transportBody = z.strictObject({
  request_id: requestId,
  failed: z.union([transportName, z.boolean()]).optional(),
  epoch: z.number().int().min(1),
  reason: reasonEnum.optional(),
});

/** T2 설계 문서 형태 `POST .../transport/fallback`: { request_id, observed_epoch, reason } */
export const fallbackBody = z.strictObject({
  request_id: requestId,
  observed_epoch: z.number().int().min(1),
  reason: reasonEnum,
});

export type RoomParams = z.infer<typeof roomParams>;
export type TransportBody = z.infer<typeof transportBody>;
export type FallbackBody = z.infer<typeof fallbackBody>;
