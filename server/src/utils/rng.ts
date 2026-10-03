import { randomInt } from 'node:crypto';

/** 서버 난수. 운영은 crypto.randomInt, 테스트는 setRng로 값을 주입한다. */
export interface Rng {
  /** [minInclusive, maxExclusive) 정수 */
  int(minInclusive: number, maxExclusive: number): number;
  /** [0, 1] 실수(Unity Random.value와 같은 닫힌 구간). 확률 비교는 호출 쪽이 `u <= chance`로 한다 */
  unit(): number;
}

const UNIT_STEPS = 1_000_000;

export const cryptoRng: Rng = {
  int(minInclusive, maxExclusive) {
    if (maxExclusive <= minInclusive) return minInclusive;
    return randomInt(minInclusive, maxExclusive);
  },
  unit() {
    return randomInt(0, UNIT_STEPS + 1) / UNIT_STEPS;
  },
};

let current: Rng = cryptoRng;

export function getRng(): Rng {
  return current;
}

/** 테스트 전용. null이면 crypto 난수로 되돌린다. */
export function setRng(r: Rng | null): void {
  current = r ?? cryptoRng;
}
