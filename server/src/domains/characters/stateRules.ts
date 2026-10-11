import { validateCareer, validCareerActive } from './careerRules';
import type { CareerState, ServerCareer } from './careerRules';
import type { GameData } from '../../gamedata/loader';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';
import type { StateBody } from './characterValidation';
import type { StateRow } from './characterRepository';

function fail(code: string, message: string, extra?: Record<string, unknown>): never {
  throw new AppError(422, message, code, extra);
}

/** 서버가 가진 레벨·직업과 stored 상태를 기준으로 클라이언트 스냅샷을 검증한다. 문제가 있으면 AppError(422). */
export function validateState(
  data: GameData,
  input: StateBody,
  character: { level: number; class: string },
  stored: StateRow,
  server?: ServerCareer,
): { passives: string[] } {
  checkFacing(data, input);
  checkMap(data, input);
  const passives = checkPassives(data, input.passives, character.level);
  validateCareer(input.career, stored.career, character, stored.passives, id=>data.passive.nodes.has(id), server);
  checkGems(data, input.skill_gems, character, input.career ?? stored.career);
  checkStyles(data, input.career, character);
  checkQuests(data, input, stored);
  return { passives };
}

function checkFacing(data: GameData, input: StateBody): void {
  if (input.facing >= data.facingCount) {
    throw new AppError(400, '입력값이 올바르지 않습니다.', 'VALIDATION', {
      fields: [{ path: 'facing', message: `facing은 0~${data.facingCount - 1}이어야 합니다` }],
    });
  }
}

function checkMap(data: GameData, input: StateBody): void {
  const map = data.maps.get(input.map_id);
  if (!map || map.instanced) fail('INVALID_MAP', '저장할 수 없는 맵입니다.');
  if (input.pos && map.bounds) {
    const { minX, minY, maxX, maxY } = map.bounds;
    const { x, y } = input.pos;
    if (x < minX || x > maxX || y < minY || y > maxY) {
      fail('INVALID_POSITION', '맵 범위를 벗어난 위치입니다.');
    }
  }
}

function checkPassives(data: GameData, ids: string[], level: number): string[] {
  const tree = data.passive;
  const reason = (r: string): never => fail('INVALID_PASSIVES', '패시브 구성이 올바르지 않습니다.', { reason: r });
  // 시작 노드는 있으면 무시한다
  const list = ids.filter((id) => id !== tree.start);
  const seen = new Set<string>();
  for (const id of list) {
    const node = tree.nodes.get(id);
    if (!node || node.kind === 'Start') reason('UNKNOWN_NODE');
    if (seen.has(id)) reason('DUPLICATE');
    seen.add(id);
  }
  if (list.length > level - 1) reason('OVER_POINTS');
  // 시작 노드에서 링크를 따라 모두 이어져야 한다
  const reached = new Set<string>([tree.start]);
  const stack = [tree.start];
  while (stack.length > 0) {
    const cur = tree.nodes.get(stack.pop() as string);
    for (const next of cur?.links ?? []) {
      if (seen.has(next) && !reached.has(next)) {
        reached.add(next);
        stack.push(next);
      }
    }
  }
  if (reached.size - 1 !== seen.size) reason('NOT_CONNECTED');
  return list;
}

/** 스킬 스타일(Docs/PLAN_SKILL_STYLES.md): 저장된 스타일마다 패시브·젬을 지금 값과 같은 규칙으로 본다 */
function checkStyles(data: GameData, career: CareerState | null | undefined, character: { level: number; class: string }): void {
  const width = 1 + data.gems.supportsPerSlot;
  for (const st of career?.styles ?? []) {
    checkPassives(data, st.passives, character.level);
    if (st.gems.length % width !== 0 || st.gems.length / width > data.gems.slots) fail('INVALID_GEMS', '젬 구성이 올바르지 않습니다.', { reason: 'BAD_STYLE_GEMS' });
    const entries: StateBody['skill_gems'] = [];
    for (let slot = 0; slot * width < st.gems.length; slot++) {
      const row = st.gems.slice(slot * width, slot * width + width);
      entries.push({ slot, active: row[0] || null, supports: row.slice(1).map((x) => x || null) });
    }
    // 주 스킬의 전직 기술 여부는 그 스타일의 배분으로 판단한다
    checkGems(data, entries, character, career ? { ...career, nodes: st.nodes } : career);
  }
}

function checkGems(
  data: GameData,
  gems: StateBody['skill_gems'],
  character: { level: number; class: string },
  career?: CareerState | null,
): void {
  const g = data.gems;
  const reason = (r: string): never => fail('INVALID_GEMS', '젬 구성이 올바르지 않습니다.', { reason: r });
  const slots = new Set<number>();
  const activeIds = new Set<string>();
  for (const entry of gems) {
    if (entry.slot < 0 || entry.slot >= g.slots || slots.has(entry.slot)) reason('BAD_SLOT');
    slots.add(entry.slot);
    if (entry.supports.length !== g.supportsPerSlot) reason('BAD_SLOT');
    const open = character.level >= (g.slotLevels[entry.slot] ?? Number.MAX_SAFE_INTEGER);
    if(entry.active) {
      const active=data.gems.byId.get(entry.active);
      const careerOk=validCareerActive(entry.active,career,entry.slot);
      if(!open || entry.active==='blades' || entry.active==='meteor') reason('AWAKENING_OR_LEVEL_LOCK');
      if(!careerOk && (!active || active.kind!=='active' || active.unlockLevel>character.level || (active.classOnly && active.classOnly!==character.class) || entry.slot===4)) reason('ACTIVE_LOCKED');
      if(activeIds.has(entry.active)) reason('DUPLICATE_ACTIVE');
      activeIds.add(entry.active);
    }
    const inSlot = new Set<string>();
    for (const id of entry.supports) {
      if (id === null) continue;
      if (!open) reason('SLOT_LOCKED');
      const gem = g.byId.get(id);
      if (!gem) reason('UNKNOWN_GEM');
      else {
        if (gem.kind !== 'support') reason('NOT_SUPPORT');
        if (gem.unlockLevel > character.level) reason('LOCKED_GEM');
        if (gem.classOnly && gem.classOnly !== character.class) reason('WRONG_CLASS');
      }
      if (inSlot.has(id)) reason('DUPLICATE_IN_SLOT');
      inSlot.add(id);
    }
  }
}

function checkQuests(data: GameData, input: StateBody, stored: StateRow): void {
  const reason = (r: string): never => fail('INVALID_QUEST_STATE', '퀘스트 상태가 올바르지 않습니다.', { reason: r });
  const seen = new Set<string>();
  for (const q of input.quests) {
    const info = data.quests.byId.get(q.id);
    if (!info) reason('UNKNOWN_QUEST');
    else {
      if (seen.has(q.id)) reason('DUPLICATE');
      seen.add(q.id);
      if (q.status < 0 || q.status > data.questStatusMax) reason('BAD_STATUS');
      if (q.step < 0 || q.step > info.stepCount) reason('BAD_STEP');
      if (q.counts.length > info.maxObjectives || q.counts.some((c) => c < 0)) reason('BAD_COUNTS');
    }
  }
  if (input.tracked_quest !== '' && !data.quests.byId.has(input.tracked_quest)) reason('UNKNOWN_QUEST');

  // 퇴보 금지: 완료한 퀘스트는 완료로 남는다.
  // 스토리 플래그는 거절하지 않는다: 컷신이 임시 플래그를 지운다("unflag", 예: 습격의 밤이 끝나면 attack_night).
  // 플래그는 보상과 무관한 진행 표시이고, 보상은 퀘스트 청구(quest_claims)가 따로 막는다.
  const completed = data.questStatusMax;
  const next = new Map(input.quests.map((q) => [q.id, q]));
  for (const q of stored.quests) {
    if (q.status === completed && next.get(q.id)?.status !== completed) reason('REGRESSION');
  }
  const nextFlags = new Set(input.story_flags);
  const cleared = stored.story_flags.filter((f) => !nextFlags.has(f));
  if (cleared.length > 0) logger.info({ flags: cleared.slice(0, 20) }, 'story_flags cleared by the client');
  // tip_* 는 클라이언트 안내 표시(퀘스트와 무관)라 경고하지 않는다(15단계 G6)
  const unknownFlags = input.story_flags.filter((f) => !data.quests.flags.has(f) && !f.startsWith('tip_'));
  if (unknownFlags.length > 0) {
    logger.warn({ flags: unknownFlags.slice(0, 20) }, 'story_flags not in quest_index (accepted)');
  }
}
