// 장비 세트 효과(Docs/PLAN_GEAR_SETS_2026-10-29.md): 세는 규칙(단계·부위·등급·반지·계승·히든), 화력 상한 여유, 전투력
import { getConfig } from '../src/config/env';
import { getGameData, initGameData } from '../src/gamedata/loader';
import { computePower } from '../src/domains/characters/powerEstimate';
import { attackCap, gearAttack } from '../src/domains/kills/killRules';
import { setBonusOf } from '../src/utils/gearSet';

beforeAll(() => initGameData(getConfig().gameDataDir));

const eco = () => getGameData().economy;
const pol = () => getConfig().policy;
const kit = (lv: number, g: string) => [`eq_sword_${lv}_${g}`, `eq_plate_${lv}_${g}`, `eq_greaves_${lv}_${g}`, `eq_neck_${lv}_${g}`, `eq_ring_${lv}_${g}`];

describe('세트 세기', () => {
  it('Lv.40 에픽 5부위: 공격 +10%, 체력 +10%. 2·3부위 단계가 쌓인다', () => {
    const k = kit(40, 'e');
    expect(setBonusOf(eco(), k)).toEqual({ attackPct: 10, healthPct: 10 });
    expect(setBonusOf(eco(), k.slice(0, 4))).toEqual({ attackPct: 5, healthPct: 5 });
    expect(setBonusOf(eco(), k.slice(0, 2))).toEqual({ attackPct: 0, healthPct: 5 });
    expect(setBonusOf(eco(), k.slice(0, 1))).toEqual({ attackPct: 0, healthPct: 0 });
  });

  it('등급은 섞여도 같은 세트, 크기는 가장 낮은 등급: 레어가 끼면 체력만(사냥 장비의 처치 속도는 그대로)', () => {
    expect(setBonusOf(eco(), ['eq_sword_40_e+11', 'eq_plate_40_e+10', 'eq_greaves_40_e+10', 'eq_neck_40_r+8', 'eq_ring_40_r+8'])).toEqual({ attackPct: 0, healthPct: 10 });
    expect(setBonusOf(eco(), kit(50, 'un'))).toEqual({ attackPct: 12, healthPct: 12 });
    expect(setBonusOf(eco(), kit(50, 'l'))).toEqual({ attackPct: 14, healthPct: 14 });
  });

  it('반지 두 개는 한 부위, 부위마다 가장 높은 등급을 센다', () => {
    expect(setBonusOf(eco(), ['eq_sword_40_e', 'eq_ring_40_e', 'eq_ring_40_e'])).toEqual({ attackPct: 0, healthPct: 5 });
    // 반지 레어 + 에픽이면 에픽 반지를 센다 -> 에픽 3부위
    expect(setBonusOf(eco(), ['eq_sword_40_e', 'eq_neck_40_e', 'eq_ring_40_r', 'eq_ring_40_e'])).toEqual({ attackPct: 5, healthPct: 5 });
  });

  it('Lv.1~15 단계는 세트가 없다', () => {
    for (const lv of [1, 10, 15]) {
      const k = lv === 1 ? ['eq_sword_1_r', 'eq_plate_1_r', 'eq_greaves_1_r', 'eq_neck_1_r', 'eq_ring_1_r'] : kit(lv, 'e');
      expect(setBonusOf(eco(), k)).toEqual({ attackPct: 0, healthPct: 0 });
    }
  });

  it('계승(^k)·히든(~x) 키는 그 장비의 단계로 센다, 단계가 다른 세트는 따로 더한다', () => {
    const k = ['eq_sword_50_e+12^1~d', 'eq_plate_50_e+10^1~h', 'eq_greaves_50_e', 'eq_neck_50_e', 'eq_ring_50_e'];
    expect(setBonusOf(eco(), k)).toEqual({ attackPct: 10, healthPct: 10 });
    // Lv.40 2부위(체력 +5) + Lv.50 3부위(공격 +5, 체력 +5)
    expect(setBonusOf(eco(), ['eq_sword_40_e', 'eq_plate_40_e', 'eq_greaves_50_e', 'eq_neck_50_e', 'eq_ring_50_e'])).toEqual({ attackPct: 5, healthPct: 10 });
  });
});

describe('화력 상한·전투력', () => {
  it('세트 공격은 장비 공격 합에 곱해 상한을 넓힌다: 클라이언트 반올림 값보다 작지 않다', () => {
    const k = kit(50, 'e').map((x) => `${x}+5`);
    const raw = k.reduce((a, x) => a + gearAttack(eco(), x), 0);
    const capSet = attackCap(eco(), pol(), 50, k);
    // 반지를 Lv.45로 바꾸면 Lv.50은 4부위(공격 +5)
    const k4 = [...k.slice(0, 4), 'eq_ring_45_e+5'];
    const raw4 = k4.reduce((a, x) => a + gearAttack(eco(), x), 0);
    expect(attackCap(eco(), pol(), 50, k4) / (eco().player.attackDamage + Math.ceil(raw4 * 1.05))).toBeCloseTo(capSet / (eco().player.attackDamage + Math.ceil(raw * 1.1)), 9);
    // 상한 = (기본 + 올림(장비 x 1.1)) x 같은 배율: 클라이언트 공격(기본 + 반올림(장비 x 1.1))을 덮는다
    const noSet = attackCap(eco(), pol(), 50, [k[0] as string]) / (eco().player.attackDamage + gearAttack(eco(), k[0] as string));
    expect(capSet).toBeGreaterThanOrEqual((eco().player.attackDamage + Math.round(raw * 1.1)) * noSet - 1e-6);
    expect(capSet / ((eco().player.attackDamage + raw) * noSet)).toBeLessThan(1.11);
  });

  it('세트가 없으면 상한은 그대로다(Lv.15 에픽 한 벌)', () => {
    const k = kit(15, 'e');
    const raw = k.reduce((a, x) => a + gearAttack(eco(), x), 0);
    const one = attackCap(eco(), pol(), 15, [k[0] as string]) / (eco().player.attackDamage + gearAttack(eco(), k[0] as string));
    expect(attackCap(eco(), pol(), 15, k)).toBeCloseTo((eco().player.attackDamage + raw) * one, 6);
  });

  it('전투력: 세트 공격·체력 %를 장비 합에 더한다', () => {
    const p = eco().player.power;
    const full = kit(40, 'e');
    const mixed = ['eq_sword_40_e', 'eq_plate_40_e', 'eq_greaves_40_e', 'eq_neck_40_e', 'eq_ring_45_e'];
    const gearAtk = full.reduce((a, x) => a + gearAttack(eco(), x), 0);
    const field = (f: string) => full.reduce((a, x) => a + Number((eco().shop.equipment.get(x) as unknown as Record<string, number>)[f] ?? 0), 0);
    const base = computePower('warrior', 40, [], 0);
    const expected = base + gearAtk * 1.1 * p.attackWeight + field('maxHealth') * 1.1 * p.hpWeight + field('block') * p.blockWeight + field('speed') * p.speedWeight;
    expect(Math.abs(computePower('warrior', 40, full, 0) - expected)).toBeLessThanOrEqual(1);
    // 4부위 세트(공격 +5, 체력 +5)보다 5부위가 크다
    expect(computePower('warrior', 40, full, 0)).toBeGreaterThan(computePower('warrior', 40, mixed, 0));
  });
});
