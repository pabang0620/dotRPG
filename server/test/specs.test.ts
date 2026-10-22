// 챕터 3 특화(Docs/ch3/SKILL_SPECIALIZATION.md 6절): 저장 형식, 서버 검증(레벨·포인트·스킬·중복·각성·닫힘), 확산형 AOE_CAP
import { aoeCapFor, SPREAD_AOE_CAP } from '../src/domains/kills/killRules';
import { careerSchema, hasSpreadSpec, specPoints, validateCareer, type CareerState } from '../src/domains/characters/careerRules';
import { getGameData } from '../src/gamedata/loader';
import { buildApp, shutdown } from './helpers';

type Spec = { skill: string; side: 'a' | 'b' };
const state = (career: number, specs: Spec[], extra: Partial<CareerState> = {}): CareerState => ({
  schema: 1, career, nodes: [], training: [], refunded: 0, questStage: 0, awakened: false, specs, ...extra,
});
const check = (s: CareerState, level: number, cls = 'warrior'): void => validateCareer(s, null, { level, class: cls }, [], () => false);

afterAll(async () => { await shutdown(); });

describe('specializations, chapter 3 open', () => {
  beforeAll(() => { buildApp({ CHAPTER3_OPEN: 'true' }); });

  test('the exported data has 96 specs, each skill a and b', () => {
    const defs = getGameData().economy.skillSpecs;
    expect(defs.size).toBe(96);
    expect(defs.get('lance:a')).toMatchObject({ axis: 'focus', cost: 2 });
    expect(defs.get('lance:b')).toMatchObject({ axis: 'spread', cost: 2 });
    expect(defs.get('f_rhythm:a')).toMatchObject({ axis: 'passive', cost: 1 });
  });

  test('points: one per level from Lv41, ten at Lv50', () => {
    expect(specPoints(40)).toBe(0);
    expect(specPoints(41)).toBe(1);
    expect(specPoints(50)).toBe(10);
  });

  test('base skills of the own class, within the points', () => {
    expect(() => check(state(0, [{ skill: 'crush', side: 'a' }]), 42)).not.toThrow();
    expect(() => check(state(0, [{ skill: 'crush', side: 'a' }]), 41)).toThrow(); // 2 points at Lv41 (1)
    expect(() => check(state(0, [{ skill: 'lance', side: 'a' }]), 45)).toThrow(); // mage skill on a warrior
  });

  test('unpromoted heroes cannot specialize career skills', () => {
    expect(() => check(state(0, [{ skill: 'f_break', side: 'a' }]), 50)).toThrow();
  });

  test('career skills, passives cost 1, duplicates and over budget refused', () => {
    expect(() => check(state(1, [{ skill: 'f_break', side: 'b' }, { skill: 'f_rhythm', side: 'a' }]), 43)).not.toThrow();
    expect(() => check(state(1, [{ skill: 'f_break', side: 'a' }, { skill: 'f_break', side: 'b' }]), 50)).toThrow();
    expect(() => check(state(1, [{ skill: 'f_break', side: 'a' }, { skill: 'f_focus', side: 'a' }, { skill: 'crush', side: 'a' }]), 45)).toThrow();
    expect(() => check(state(1, [{ skill: 'g_wall', side: 'a' }]), 50)).toThrow(); // guardian skill on a fighter
  });

  test('the awakening needs the awakening', () => {
    expect(() => check(state(1, [{ skill: 'f_awake', side: 'a' }]), 50)).toThrow();
    expect(() => check(state(1, [{ skill: 'f_awake', side: 'a' }], { questStage: 5, awakened: true }), 50)).not.toThrow();
  });

  test('saved styles follow the same rules', () => {
    const s = state(0, [], { styles: [{ nodes: [], gems: [], passives: [], specs: [{ skill: 'whirl', side: 'b' }, { skill: 'cry', side: 'a' }] }] });
    expect(() => check(s, 44)).not.toThrow();
    expect(() => check(s, 43)).toThrow();
  });

  test('spread B sides widen the multi-hit cap from Lv41 only', () => {
    const pol = { powerPassivePerLevel: 0.11, powerSkillFactor: 2, powerAoeCap: 5 };
    const spread = state(0, [{ skill: 'lance', side: 'b' }]);
    const focus = state(0, [{ skill: 'lance', side: 'a' }]);
    expect(hasSpreadSpec(spread)).toBe(true);
    expect(hasSpreadSpec(focus)).toBe(false);
    expect(aoeCapFor(pol, 45, hasSpreadSpec(spread))).toBe(SPREAD_AOE_CAP);
    expect(aoeCapFor(pol, 45, hasSpreadSpec(focus))).toBe(5);
    expect(aoeCapFor(pol, 40, true)).toBe(5);
  });
});

describe('specializations, chapter 3 closed', () => {
  beforeAll(() => { buildApp({ CHAPTER3_OPEN: 'false' }); });

  test('any choice is refused with CHAPTER_LOCKED, no choice is fine', () => {
    expect(() => check(state(0, [{ skill: 'crush', side: 'a' }]), 45)).toThrow(expect.objectContaining({ code: 'CHAPTER_LOCKED' }));
    expect(() => check(state(0, []), 40)).not.toThrow();
  });

  test('old clients (schema 1, no specs) and new ones (schema 2) both parse', () => {
    expect(careerSchema.safeParse(state(0, [])).success).toBe(true);
    const { specs: _drop, ...old } = state(0, []);
    expect(careerSchema.safeParse(old).success).toBe(true);
    expect(careerSchema.safeParse({ ...old, schema: 2 }).success).toBe(true);
    expect(careerSchema.safeParse({ ...old, specs: [{ skill: 'crush', side: 'c' }] }).success).toBe(false);
  });
});
