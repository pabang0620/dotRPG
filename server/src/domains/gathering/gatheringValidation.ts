import { z } from 'zod';
import { requestId } from '../economy/economyValidation';

const nodeId = z.string().min(3).max(64).regex(/^[A-Za-z0-9_]+:\d+:\d+$/, '노드 id 형식이 올바르지 않습니다');

/** 지급 수량·재생 시간은 받지 않는다(서버 데이터가 정한다) */
export const gatherBody = z.strictObject({
  request_id: requestId,
  map_id: z.string().min(1).max(32),
  node_id: nodeId,
});
export const chestBody = z.strictObject({ request_id: requestId, chest_id: nodeId });
export const deliveryBody = z.strictObject({ request_id: requestId, site_id: z.string().min(1).max(32) });
export const nodesParams = z.object({ uuid: z.uuid(), map_id: z.string().min(1).max(32) });

export type GatherBody = z.infer<typeof gatherBody>;
export type ChestBody = z.infer<typeof chestBody>;
export type DeliveryBody = z.infer<typeof deliveryBody>;
export type NodesParams = z.infer<typeof nodesParams>;
