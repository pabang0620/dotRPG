import { z } from 'zod';
import { charParams } from '../economy/economyValidation';

export { charParams };

const requestId = z.uuid();

/** career: 1 Fighter, 2 Guardian, 3 Arcanist, 4 Bishop (C# Career 열거). 받지 않는 값: 비용, 시각, 단계 값 */
export const promoteBody = z.strictObject({ request_id: requestId, career: z.number().int().min(1).max(4) });
export const trialStartBody = z.strictObject({ request_id: requestId });
/** 클라이언트는 "무슨 일이 일어났나"만 말한다. 서버는 성공 주장을 최소 시간·마을 체류로 검증한다 */
export const trialFinishBody = z.strictObject({ request_id: requestId, result: z.enum(['success', 'fail']) });
/** 대화로 넘기는 단계만(0,1,3,4). 2는 시련(C3)만 넘기고, 5 이상은 400 */
export const advanceBody = z.strictObject({ request_id: requestId, from_stage: z.number().int().min(0).max(4) });

export type PromoteBody = z.infer<typeof promoteBody>;
export type TrialStartBody = z.infer<typeof trialStartBody>;
export type TrialFinishBody = z.infer<typeof trialFinishBody>;
export type AdvanceBody = z.infer<typeof advanceBody>;
