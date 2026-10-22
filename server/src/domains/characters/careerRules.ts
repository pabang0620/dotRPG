import { z } from 'zod';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';
import { chapter3Open, CHAPTER2_LEVEL_CAP, levelCap } from '../../gamedata/chapterGate';
import { getGameData } from '../../gamedata/loader';
import { baseClassOfCareer } from '../../utils/characterClass';

const nodeList = z.array(z.strictObject({ id: z.string().max(64), rank: z.number().int().min(1).max(3) })).max(9);
/** 챕터 3 특화 선택(Docs/ch3/SKILL_SPECIALIZATION.md 6절): 스킬마다 a 또는 b 하나 */
const specList = z.array(z.strictObject({ skill: z.string().max(64), side: z.enum(['a', 'b']) })).max(48);
/** 스킬 스타일 하나(Docs/PLAN_SKILL_STYLES.md 3절): 전직 배분, 스킬 키·보조 젬(슬롯 x (주 1 + 보조 2), 빈칸 ""), 패시브 */
const styleSchema = z.strictObject({
  nodes: nodeList,
  gems: z.array(z.string().max(64)).max(24), // 8 keys x (skill + 2 supports)
  passives: z.array(z.string().max(64)).max(200),
  specs: specList.optional(),
});
export const careerSchema = z.strictObject({
  // 옛 클라이언트(schema 1, specs 없음)도 그대로 받는다
  schema: z.union([z.literal(1), z.literal(2)]), career: z.number().int().min(0).max(6),
  nodes: nodeList,
  training: z.array(z.string().max(64)).max(200), refunded: z.number().int().min(0).max(39),
  questStage: z.number().int().min(0).max(5), awakened: z.boolean(),
  activeStyle: z.number().int().min(0).max(2).optional(),
  styles: z.array(styleSchema).max(3).optional(),
  specs: specList.optional(),
});
export type CareerStyle = z.infer<typeof styleSchema>;
export type CareerState = z.infer<typeof careerSchema>;
/** 직업별 주력 사냥기(연사기)는 모두 Lv18에 열린다(클라이언트 CareerSkill.MainHunters와 같다) */
export const MAIN_HUNTERS = ['f_break', 'g_wall', 'm_storm', 'b_light', 'a_rend', 'n_volley'];
export const careerIds = [[], ['f_rhythm','f_cross','f_rush','f_flurry','f_edge','f_break','f_focus','f_execute','f_awake','f_stance'],
 ['g_steel','g_guard','g_wall','g_oath','g_retal','g_taunt','g_bash','g_counter','g_awake'],
 ['m_elements','m_fire','m_ice','m_storm','m_flow','m_orbit','m_veil','m_rift','m_awake','m_barrier'],
 ['b_mercy','b_heal','b_bloom','b_cleanse','b_grace','b_light','b_wing','b_bless','b_awake'],
 // 5 어쌔신(10노드, 9번 은신은 파이터와 같은 추가 노드 규칙), 6 닌자(9노드). Docs/PLAN_CLASS_ROGUE.md 2.3~2.4절
 ['a_vital','a_twin','a_veil','a_venom','a_ambush','a_rend','a_rupture','a_assassinate','a_awake','a_stealth'],
 ['n_throw','n_kunai','n_clone','n_recall','n_far','n_volley','n_fuma','n_bomb','n_awake']];
const reject = (why: string): never => { throw new AppError(422, '전직 상태가 올바르지 않습니다.', 'INVALID_CAREER', {reason:why}); };
/** 9단계: 서버가 기록한 전직(character_career). granted가 null이면 행 없음(미전직). log는 거절하지 않고 기록만 한다 */
export interface ServerCareer { granted: {career:number;stage:number} | null; mode: 'log' | 'enforce' }
export function validateCareer(next: CareerState | undefined | null, prev: CareerState | undefined | null,
  character: {level:number;class:string}, oldPassives: string[], validTraining: (id:string)=>boolean, server?: ServerCareer): void {
  if (!next) { if(prev?.career) reject('MISSING_STATE'); return; }
  const c=next.career;
  if (server) {
    // 서버 진실(8.3): 서버가 부여하지 않은 전직·각성 값은 거절한다
    const g=server.granted; let why: string | null = null;
    if (!g) { if (c!==0) why='NOT_GRANTED'; }
    else if (c===0) why='MISSING_STATE';
    else if (c!==g.career) why='NOT_GRANTED';
    else if (next.questStage>g.stage) why='STAGE_NOT_GRANTED';
    else if (next.awakened && g.stage!==5) why='NOT_GRANTED';
    else if (next.questStage<g.stage) why='QUEST_REGRESSION';
    if (why) { if (server.mode==='enforce') reject(why); else logger.warn({reason:why,career:c},'career state not granted by server (log mode)'); }
  }
  if(c && (character.level<15 || baseClassOfCareer(c)!==character.class)) reject('LEVEL_OR_BASE_CLASS');
  if(prev?.career && c!==prev.career) reject('ALREADY_PROMOTED');
  if(!c && (next.nodes.length||next.questStage||next.awakened)) reject('BASE_CLASS_LOCK');
  if(next.awakened !== (c>0 && next.questStage===5)) reject('QUEST_REQUIRED');
  if(prev && next.questStage<prev.questStage) reject('QUEST_REGRESSION');
  const expected=prev?.training ?? [...new Set(oldPassives.filter(x=>x!=='start'&&validTraining(x)))];
  if(new Set(next.training).size!==next.training.length || next.training.length!==expected.length || next.training.some(x=>!expected.includes(x))) reject('TRAINING_CHANGED');
  const refunded=prev?.refunded ?? Math.min(character.level-1,expected.length);
  if(next.refunded!==refunded) reject('REFUND_CHANGED');
  checkNodes(next.nodes, c, character.level, next.refunded);
  // 스킬 스타일(Docs/PLAN_SKILL_STYLES.md): 저장된 스타일마다 같은 규칙(레벨·선행·예산)으로 본다
  for(const st of next.styles ?? []) checkNodes(st.nodes, c, character.level, next.refunded);
  // 챕터 3 특화: 쓰고 있는 것과 스타일마다 저장한 것 모두 같은 규칙
  checkSpecs(next.specs, c, character, next.awakened);
  for(const st of next.styles ?? []) checkSpecs(st.specs, c, character, next.awakened);
  if((next.activeStyle ?? 0) >= Math.max(1, next.styles?.length ?? 0) && next.styles?.length) reject('BAD_STYLE');
}

/** 전직 노드 배분 한 벌: 직업 목록 안, 레벨, 선행, 예산(레벨 - 11 + 환급) */
function checkNodes(list: { id: string; rank: number }[], c: number, level: number, refunded: number): void {
  const ids=careerIds[c]??[]; const nodes=new Map(list.map(x=>[x.id,x.rank]));
  if(nodes.size!==list.length) reject('DUPLICATE_NODE');
  if(!c && list.length) reject('BASE_CLASS_LOCK');
  let spent=0;
  for(const n of list){
    const i=ids.indexOf(n.id);if(i<0||i===8) reject('WRONG_CAREER_OR_AWAKENING');
    // 9번(파이터 검기 태세)은 트리 밖 추가 노드: 섬광보(2번)를 선행으로, Lv18부터
    // 9번 추가 노드: 파이터 검기 태세·어쌔신 은신(선행 2번, Lv18), 메이지 매직 배리어(선행 마력 순환 4번, Lv15)
    const arc=c===3, tier=i===9?(arc?1:2):i%4, need=MAIN_HUNTERS.includes(n.id)?18:tier<2?15:tier===2?18:22, before=i===9?(arc?4:2):i-1;
    if(level<need) reject('NODE_LEVEL');
    if(tier>0&&!nodes.has(ids[before] as string)) reject('PREREQUISITE');
    spent+=n.rank*(n.rank+1)/2;
  }
  if(c && spent>level-11+refunded)reject('OVER_BUDGET');
}

/** 스타일 3을 쓰거나 무언가 저장했는지(계정이 해금했어야 한다) */
export function usesStyle3(state: CareerState | null | undefined): boolean {
  if (!state) return false;
  if (state.activeStyle === 2) return true;
  const s3 = state.styles?.[2];
  return !!s3 && (s3.nodes.length > 0 || s3.passives.length > 0 || s3.gems.some((g) => g !== ''));
}
export function validCareerActive(id:string, state:CareerState|null|undefined, slot:number):boolean {
  if(!state?.career)return false;
  const list=careerIds[state.career]??[]; const index=list.indexOf(id);
  if(index===8)return slot===4&&state.awakened&&state.questStage===5;
  // 8 keys: career actives go on any key but T (slot 4, the awakening); slots 5-7 are A S D
  return slot>=0&&slot<8&&slot!==4&&index>=0&&index%4!==0&&state.nodes.some(x=>x.id===id&&x.rank>0);
}

/** 기본 스킬(클래스마다 5개). 미전직도 이것만은 특화할 수 있다 */
export const BASE_SKILLS: Record<string, string[]> = {
  warrior: ['crush', 'whirl', 'cry', 'charge', 'blades'],
  mage: ['lance', 'frostorb', 'nova', 'firefield', 'meteor'],
  // 도적(2026-10-23): 이동기 그림자 질주는 젬이 아니라 특화 대상이 아니다(Docs/ch3/SKILL_SPECIALIZATION.md 3.6절)
  rogue: ['stab', 'leap', 'backstab', 'raid', 'dance'],
};
/** 특화 포인트: Lv41부터 레벨당 1(최대 10). 레벨은 지금 상한(levelCap)까지만 센다 */
export const specPoints = (level: number, cap: number = levelCap()): number =>
  Math.max(0, Math.min(10, Math.min(level, cap) - CHAPTER2_LEVEL_CAP));

/** 특화 한 벌 검증: 스킬이 그 캐릭터 것인지, 중복 없음, 정의 존재, 포인트 합, 챕터 3 열림 */
function checkSpecs(list: { skill: string; side: 'a' | 'b' }[] | undefined, c: number,
  character: { level: number; class: string }, awakened: boolean): void {
  if (!list || list.length === 0) return;
  if (!chapter3Open()) throw new AppError(422, '아직 열리지 않은 콘텐츠입니다.', 'CHAPTER_LOCKED', { kind: 'spec' });
  const defs = getGameData().economy.skillSpecs;
  const base = BASE_SKILLS[character.class] ?? [];
  const ids = careerIds[c] ?? [];
  const seen = new Set<string>();
  let spent = 0;
  for (const x of list) {
    if (seen.has(x.skill)) reject('SPEC_DUPLICATE');
    seen.add(x.skill);
    const idx = ids.indexOf(x.skill);
    if (!base.includes(x.skill) && idx < 0) reject('SPEC_WRONG_SKILL');
    if (idx === 8 && !awakened) reject('SPEC_NOT_AWAKENED');
    const def = defs.get(`${x.skill}:${x.side}`);
    if (!def) reject('SPEC_UNKNOWN');
    spent += (def as { cost: number }).cost;
  }
  if (spent > specPoints(character.level)) reject('SPEC_OVER_BUDGET');
}

/** 확산형 특화(집중·확산 축의 b)가 하나라도 있으면 true: 다중 적중 상한(AOE_CAP)을 넓힌다 */
export function hasSpreadSpec(state: CareerState | null | undefined): boolean {
  const defs = getGameData().economy.skillSpecs;
  return (state?.specs ?? []).some((x) => defs.get(`${x.skill}:${x.side}`)?.axis === 'spread');
}
