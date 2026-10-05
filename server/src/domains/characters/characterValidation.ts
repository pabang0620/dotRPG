import { z } from 'zod';
import { careerSchema } from './careerRules';

export const NAME_MIN = 2;
export const NAME_MAX = 8;
const NAME_CHARS_RE = /^[가-힣A-Za-z0-9]+$/;

// 입력을 NFC로 정규화한 뒤 검사한다. 앞뒤 공백은 잘라 주지 않고 NAME_CHARS로 거절한다.
const name = z
  .string()
  .transform((s) => s.normalize('NFC'))
  .superRefine((s, ctx) => {
    const len = [...s].length;
    if (len < NAME_MIN || len > NAME_MAX) {
      ctx.addIssue({
        code: 'custom',
        message: `이름은 ${NAME_MIN}~${NAME_MAX}자여야 합니다`,
        params: { reason: 'NAME_LENGTH' },
      });
    }
    if (!NAME_CHARS_RE.test(s)) {
      ctx.addIssue({
        code: 'custom',
        message: '이름은 한글 완성형, 영문, 숫자만 쓸 수 있습니다',
        params: { reason: 'NAME_CHARS' },
      });
    }
  });

export const createCharacterBody = z.strictObject({
  request_id: z.uuid(),
  name,
  class: z.enum(['warrior', 'mage']),
});

export const characterParams = z.object({ uuid: z.uuid() });

const str64 = z.string().max(64);
const unique = <T extends z.ZodType>(arr: T) =>
  arr.refine((v) => new Set(v as unknown[]).size === (v as unknown[]).length, {
    message: '중복된 값이 있습니다',
  });

export const stateBody = z.strictObject({
  career: careerSchema.nullable().optional(),
  version: z.number().int().min(0),
  map_id: z.string().min(1).max(64),
  pos: z.strictObject({ x: z.number(), y: z.number() }).nullable(),
  facing: z.number().int().min(0).max(255),
  quests: z
    .array(
      z.strictObject({
        id: str64,
        status: z.number().int(),
        step: z.number().int(),
        counts: z.array(z.number().int()).max(64),
      }),
    )
    .max(200),
  story_flags: unique(z.array(str64).max(500)),
  tracked_quest: str64,
  passives: z.array(str64).max(200),
  skill_gems: z
    .array(
      z.strictObject({
        slot: z.number().int(),
        active: str64.nullable().optional(),
        supports: z.array(str64.nullable()).max(8),
      }),
    )
    .max(16),
});

export type CreateCharacterBody = z.infer<typeof createCharacterBody>;
export type CharacterParams = z.infer<typeof characterParams>;
export type StateBody = z.infer<typeof stateBody>;
