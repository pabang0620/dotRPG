import { partyHpScaleOf } from '../src/domains/dungeons/partyScale';

const eco = { dungeons: { partyHpScale: [1, 1.6, 2.1, 2.5], aiHpWeight: 0.4 } } as unknown as Parameters<typeof partyHpScaleOf>[0];

describe('partyHpScaleOf (AI 동료 인원 보정)', () => {
  it('사람만 있으면 표 그대로', () => {
    expect(partyHpScaleOf(eco, 1, 0)).toBe(1);
    expect(partyHpScaleOf(eco, 2, 0)).toBeCloseTo(1.6);
    expect(partyHpScaleOf(eco, 4, 0)).toBeCloseTo(2.5);
  });
  it('AI는 사람 0.4명으로 세고 표 사이는 보간', () => {
    expect(partyHpScaleOf(eco, 1, 3)).toBeCloseTo(1.6 + 0.5 * 0.2);
    expect(partyHpScaleOf(eco, 1, 1)).toBeCloseTo(1 + 0.6 * 0.4);
    expect(partyHpScaleOf(eco, 3, 1)).toBeCloseTo(2.1 + 0.4 * 0.4);
  });
  it('사람 0명 기록은 1명으로 본다', () => {
    expect(partyHpScaleOf(eco, 0, 0)).toBe(1);
  });
});
