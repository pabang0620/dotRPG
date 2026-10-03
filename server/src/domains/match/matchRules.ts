// 자동 매칭 규칙(5.4)의 순수 함수: 가장 오래 기다린 티켓이 시드. 전투력 ±30% 안에서 모아 4명이면 즉시,
// 아니면 시드가 대기 시간(60초)에 닿았거나 강제 출발이면 모인 사람으로 파티를 만든다.
import type { QueueTicket } from './queueStore';

export function pickGroups(tickets: QueueTicket[], nowMs: number, ratio: number, queueMs: number): QueueTicket[][] {
  const left = [...tickets];
  const out: QueueTicket[][] = [];
  for (;;) {
    let formed: QueueTicket[] | null = null;
    for (const seed of left) {
      const group = [seed];
      for (const t of left) {
        if (group.length >= 4) break;
        if (t !== seed && Math.abs(t.power - seed.power) <= ratio * seed.power) group.push(t);
      }
      const due = seed.forceDepart || nowMs - seed.queuedAt.getTime() >= queueMs;
      if (group.length === 4 || due) {
        formed = group;
        break;
      }
    }
    if (!formed) return out;
    out.push(formed);
    for (const t of formed) left.splice(left.indexOf(t), 1);
  }
}
