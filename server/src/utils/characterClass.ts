// 기본 직업(characters.class)과 전직 번호, 장비 착용 판정을 한곳에 둔다(Docs/PLAN_CLASS_ROGUE.md 4.2절·5.1절).

/** 기본 직업 목록. 순서는 C# CharacterClass 열거와 같다(0 전사, 1 마법사, 2 도적) */
export const CHARACTER_CLASSES = ['warrior', 'mage', 'rogue'] as const;
export type CharacterClassId = (typeof CHARACTER_CLASSES)[number];

export function isCharacterClass(v: unknown): v is CharacterClassId {
  return typeof v === 'string' && (CHARACTER_CLASSES as readonly string[]).includes(v);
}

/** 전직 번호(1 파이터, 2 수호자, 3 메이지, 4 비숍, 5 어쌔신, 6 닌자)의 기본 직업. 범위 밖이면 null */
export function baseClassOfCareer(career: number): CharacterClassId | null {
  if (career === 1 || career === 2) return 'warrior';
  if (career === 3 || career === 4) return 'mage';
  if (career === 5 || career === 6) return 'rogue';
  return null;
}

/** 장비가 쓰는 직업 정보. classes(직업 목록, null·없음 = 모두)가 정본이고 classOnly는 옛 내보내기 호환용 첫 값이다 */
export interface ClassRestricted {
  classOnly: string | null;
  classes?: readonly string[] | null;
}

/** 이 직업이 이 장비를 쓸 수 있는가 */
export function usableBy(e: ClassRestricted, cls: string): boolean {
  if (e.classes && e.classes.length > 0) return e.classes.includes(cls);
  return e.classOnly === null || e.classOnly === cls;
}
