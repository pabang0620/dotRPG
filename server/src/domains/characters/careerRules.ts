import { z } from 'zod';
import { AppError } from '../../utils/AppError';
import { logger } from '../../utils/logger';

export const careerSchema = z.strictObject({
  schema: z.literal(1), career: z.number().int().min(0).max(4),
  nodes: z.array(z.strictObject({ id: z.string().max(64), rank: z.number().int().min(1).max(3) })).max(8),
  training: z.array(z.string().max(64)).max(200), refunded: z.number().int().min(0).max(39),
  questStage: z.number().int().min(0).max(5), awakened: z.boolean(),
});
export type CareerState = z.infer<typeof careerSchema>;
export const careerIds = [[], ['f_rhythm','f_cross','f_rush','f_flurry','f_edge','f_break','f_focus','f_execute','f_awake'],
 ['g_steel','g_guard','g_wall','g_oath','g_retal','g_taunt','g_bash','g_counter','g_awake'],
 ['m_elements','m_fire','m_ice','m_storm','m_flow','m_orbit','m_veil','m_rift','m_awake'],
 ['b_mercy','b_heal','b_bloom','b_cleanse','b_grace','b_light','b_wing','b_bless','b_awake']];
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
  if(c && (character.level<15 || ((c<=2?'warrior':'mage')!==character.class))) reject('LEVEL_OR_BASE_CLASS');
  if(prev?.career && c!==prev.career) reject('ALREADY_PROMOTED');
  if(!c && (next.nodes.length||next.questStage||next.awakened)) reject('BASE_CLASS_LOCK');
  if(next.awakened !== (c>0 && next.questStage===5)) reject('QUEST_REQUIRED');
  if(prev && next.questStage<prev.questStage) reject('QUEST_REGRESSION');
  const expected=prev?.training ?? [...new Set(oldPassives.filter(x=>x!=='start'&&validTraining(x)))];
  if(new Set(next.training).size!==next.training.length || next.training.length!==expected.length || next.training.some(x=>!expected.includes(x))) reject('TRAINING_CHANGED');
  const refunded=prev?.refunded ?? Math.min(character.level-1,expected.length);
  if(next.refunded!==refunded) reject('REFUND_CHANGED');
  const ids=careerIds[c]??[]; const nodes=new Map(next.nodes.map(x=>[x.id,x.rank]));
  if(nodes.size!==next.nodes.length) reject('DUPLICATE_NODE');
  let spent=0;
  for(const n of next.nodes){
    const i=ids.indexOf(n.id);if(i<0||i>=8) reject('WRONG_CAREER_OR_AWAKENING');
    const tier=i%4, level=tier<2?15:tier===2?18:22;
    if(character.level<level) reject('NODE_LEVEL');
    if(tier>0&&!nodes.has(ids[i-1] as string)) reject('PREREQUISITE');
    spent+=n.rank*(n.rank+1)/2;
  }
  if(c && spent>character.level-11+next.refunded)reject('OVER_BUDGET');
}
export function validCareerActive(id:string, state:CareerState|null|undefined, slot:number):boolean {
  if(!state?.career)return false;
  const list=careerIds[state.career]??[]; const index=list.indexOf(id);
  if(index===8)return slot===4&&state.awakened&&state.questStage===5;
  return slot<4&&index>=0&&index%4!==0&&state.nodes.some(x=>x.id===id&&x.rank>0);
}
