// server/data/chat.json: 채팅 규칙(클라이언트 ChatRules와 같은 값)과 신고 사유. 없거나 틀리면 기동이 멈춘다.
import fs from 'node:fs';
import path from 'node:path';
import { z } from 'zod';

export const REPORT_REASON_CODES = ['abuse', 'spam', 'scam_ad', 'cheat', 'other'] as const;
export type ReportReason = (typeof REPORT_REASON_CODES)[number];

const chatSchema = z.looseObject({
  schema: z.literal(1),
  maxLength: z.number().int().min(1).max(200),
  minIntervalSeconds: z.number().positive(),
  repeatLimit: z.number().int().min(2),
  muteSeconds: z.number().int().positive(),
  reportLines: z.number().int().min(1).max(100),
  reportReasons: z.array(z.looseObject({ code: z.enum(REPORT_REASON_CODES), label: z.string().min(1) })).min(1),
});

export interface ChatData {
  maxLength: number;
  minIntervalSeconds: number;
  repeatLimit: number;
  muteSeconds: number;
  reportLines: number;
  reportReasons: { code: ReportReason; label: string }[];
}

export function loadChatData(dir: string): ChatData {
  const file = path.join(dir, 'chat.json');
  let raw: unknown;
  try {
    raw = JSON.parse(fs.readFileSync(file, 'utf8'));
  } catch (err) {
    throw new Error(`게임 데이터 chat.json 을 읽을 수 없습니다: ${(err as Error).message}`);
  }
  const r = chatSchema.safeParse(raw);
  if (!r.success) {
    const detail = r.error.issues
      .slice(0, 5)
      .map((i) => `${i.path.join('.')}: ${i.message}`)
      .join('; ');
    throw new Error(`게임 데이터 chat.json 검증 실패: ${detail}`);
  }
  const codes = new Set(r.data.reportReasons.map((x) => x.code));
  if (codes.size !== r.data.reportReasons.length || REPORT_REASON_CODES.some((c) => !codes.has(c))) {
    throw new Error('게임 데이터 chat.json 검증 실패: reportReasons 는 사유 코드 5개를 한 번씩 가져야 합니다');
  }
  return {
    maxLength: r.data.maxLength,
    minIntervalSeconds: r.data.minIntervalSeconds,
    repeatLimit: r.data.repeatLimit,
    muteSeconds: r.data.muteSeconds,
    reportLines: r.data.reportLines,
    reportReasons: r.data.reportReasons.map((x) => ({ code: x.code, label: x.label })),
  };
}
