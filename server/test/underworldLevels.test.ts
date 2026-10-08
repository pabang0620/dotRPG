import { carryReferenceLevel, isHardGap, xpFactor } from '../src/domains/fieldsessions/xpFactor';

const policy = { carrySlack: 5, carryStep: 0.15, carryMin: 0.02, carryHardGap: 15 };

describe('post-40 underground challenge levels', () => {
  it('does not punish a max-level party for fighting Lv.53 cave enemies', () => {
    const level = carryReferenceLevel(53, 'underground', 40);
    expect(xpFactor(level, 40, policy)).toBe(1);
    expect(isHardGap(level, 40, policy)).toBe(false);
  });

  it('retains hard carry protection for low-level underground passengers', () => {
    const level = carryReferenceLevel(53, 'underground', 40);
    expect(xpFactor(level, 20, policy)).toBe(0.02);
    expect(isHardGap(level, 20, policy)).toBe(true);
  });

  it('retains a partial penalty for characters below the endgame preparation level', () => {
    const level = carryReferenceLevel(47, 'underground', 40);
    expect(xpFactor(level, 30, policy)).toBe(0.25);
    expect(isHardGap(level, 30, policy)).toBe(false);
  });

  it('does not change surface or unclassified-map carry rules, including future high enemies', () => {
    for (const layer of ['surface', undefined] as const) {
      for (let enemy = 1; enemy <= 60; enemy++) {
        const level = carryReferenceLevel(enemy, layer, 40);
        expect(level).toBe(enemy);
        for (let player = 1; player <= 40; player++) {
          expect(xpFactor(level, player, policy)).toBe(xpFactor(enemy, player, policy));
          expect(isHardGap(level, player, policy)).toBe(isHardGap(enemy, player, policy));
        }
      }
    }
  });

  it('does not inflate a cave below the cap and follows the data-defined player cap', () => {
    expect(carryReferenceLevel(32, 'underground', 40)).toBe(32);
    expect(carryReferenceLevel(53, 'underground', 50)).toBe(50);
    expect(carryReferenceLevel(53, 'underground', 60)).toBe(53);
  });
});
