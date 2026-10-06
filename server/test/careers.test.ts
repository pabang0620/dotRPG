import { randomUUID } from 'node:crypto';
import request from 'supertest';
import fs from 'node:fs';
import path from 'node:path';
import { careerIds, validateCareer, validCareerActive, type CareerState } from '../src/domains/characters/careerRules';
import { getPool } from '../src/db/pool';
import { auth, buildApp, createChar, emptyState, randomName, registerAccount, resetDb, shutdown, ver } from './helpers';
const blank = (career=0):CareerState => ({schema:1,career,nodes:[],training:[],refunded:0,questStage:0,awakened:false});
const character=(level=15,cls='warrior')=>({level,class:cls});
const check=(s:CareerState,c=character(),prev:CareerState|null=null)=>validateCareer(s,prev,c,[],()=>false);
describe('career invariants',()=>{
 test('base level1 saves',()=>expect(()=>check(blank(),character(1))).not.toThrow());
 test.each([1,2,3,4])('career %i minimum and ancestry',c=>{
  expect(()=>check(blank(c),character(14,c<=2?'warrior':'mage'))).toThrow();
  expect(()=>check(blank(c),character(15,c<=2?'warrior':'mage'))).not.toThrow();
  expect(()=>check(blank(c),character(40,c<=2?'mage':'warrior'))).toThrow();
 });
 test('promotion cannot change or revert',()=>{expect(()=>check(blank(2),character(),blank(1))).toThrow();expect(()=>check(blank(),character(),blank(1))).toThrow();});
 test('awakening requires own completed quest',()=>{const s=blank(1);s.awakened=true;expect(()=>check(s)).toThrow();s.questStage=5;expect(()=>check(s)).not.toThrow();expect(validCareerActive('f_awake',s,4)).toBe(true);expect(validCareerActive('b_awake',s,4)).toBe(false);expect(validCareerActive('f_awake',s,0)).toBe(false);});
 test('base high level cannot hold awakening',()=>{const s=blank();s.questStage=5;s.awakened=true;expect(()=>check(s,character(40))).toThrow();});
 test('nodes enforce prerequisites, rank costs and own class',()=>{
  const s=blank(1);s.nodes=[{id:'f_cross',rank:1}];expect(()=>check(s)).toThrow();
  s.nodes=[{id:'f_rhythm',rank:1},{id:'f_cross',rank:1}];expect(()=>check(s)).not.toThrow();
  s.nodes[0]!.rank=3;expect(()=>check(s)).toThrow();
  s.nodes=[{id:'g_steel',rank:1}];expect(()=>check(s)).toThrow();
  s.nodes=[{id:'f_awake',rank:1}];expect(()=>check(s,character(40))).toThrow();
 });
 test('refund is immutable and migration may only use stored training',()=>{
  const s=blank();s.training=['old'];s.refunded=1;
  expect(()=>validateCareer(s,null,character(),['old'],id=>id==='old')).not.toThrow();
  expect(()=>validateCareer(s,s,character(),[],()=>false)).not.toThrow();
  expect(()=>validateCareer({...s,refunded:2},s,character(),[],()=>false)).toThrow();
 });
 test('each career has eight nodes and a distinct awakening',()=>{for(const ids of careerIds.slice(1))expect(ids.length).toBe(9);expect(new Set(careerIds.flat()).size).toBe(36);});
 test('server rules match the compiled client catalog',()=>{
  const catalog=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../data/careers.json'),'utf8')) as {skills:{id:string;career:number;index:number;level:number}[]};
  expect(catalog.skills.length).toBe(36);
  for(let c=1;c<=4;c++)expect(catalog.skills.filter(s=>s.career===c).sort((a,b)=>a.index-b.index).map(s=>s.id)).toEqual(careerIds[c]);
  for(const s of catalog.skills)expect(s.level).toBe(s.index===8?15:s.index%4<2?15:s.index%4===2?18:22);
 });
 test('renewal catalog exposes role mechanics while retaining saved node identities',()=>{
  const {skills}=JSON.parse(fs.readFileSync(path.resolve(__dirname,'../data/careers.json'),'utf8'));
  const skill=(id:string)=>skills.find((s:{id:string})=>s.id===id);
  expect(skill('f_awake')).toMatchObject({name:'천검귀일',range:5,hits:6});
  expect(skill('f_break')).toMatchObject({name:'파쇄 검기',range:7,effect:'break'});
  expect(skill('g_taunt').radius).toBe(5);
  expect(skill('g_bash').range).toBeCloseTo(2.2);
  expect(skill('m_storm')).toMatchObject({hits:5,radius:3});
  expect(skill('b_heal').radius).toBe(5);
  expect(skill('b_bloom')).toMatchObject({duration:6,hits:6});
  expect(skill('b_awake')).toMatchObject({duration:8,hits:8});
  const active=skills.filter((s:{kind:number})=>s.kind!==0);
  expect(new Set(active.map((s:{delivery:string})=>s.delivery)).size).toBe(28);
  expect(skill('f_cross')).toMatchObject({name:'십자참',delivery:'cross',mp:10,cooldown:3,hits:2});
  expect(skill('f_cross').range).toBeCloseTo(2.3,5);
  expect(skill('f_flurry')).toMatchObject({name:'난무',delivery:'flurry',mp:20,cooldown:9,hits:5});
  expect(skill('f_focus')).toMatchObject({name:'일섬',delivery:'iaido',range:6});
  expect(skill('f_rhythm').effect).toBe('swordflow');
  expect(skill('f_edge').effect).toBe('opening');
  expect(skill('g_awake')).toMatchObject({name:'천쇄방패',effect:'aegis',power:5,radius:5});
  expect(skill('g_wall').delivery).toBe('shieldthrow');
  expect(skill('m_fire').delivery).toBe('fire');
  expect(skill('b_heal').delivery).toBe('heal');
  expect(skill('b_light').power).toBeLessThan(skill('f_cross').power*skill('f_cross').hits);
  for(const s of skills){expect(Number.isFinite(s.power)).toBe(true);expect(s.cooldown).toBeGreaterThanOrEqual(0);}
 });
});
const app=buildApp();
beforeAll(resetDb);afterAll(shutdown);
test('HTTP save/load carries career, loadout and one-time refund; wrong class rejected',async()=>{
 const account=await registerAccount(app);const made=await createChar(app,account,randomName(),'warrior');const c=made.body.data.character;
 await getPool().query('UPDATE characters SET level=15 WHERE uuid=$1',[c.id]);
 // 9단계: 전직은 서버가 기록한다(C1). 서버가 부여하지 않은 값은 PUT state가 거절한다
 const promoted=await request(app).post(`/characters/${c.id}/career/promote`).set(auth(account)).send({request_id:randomUUID(),career:1});
 expect(promoted.status).toBe(200);
 const career=blank(1);career.nodes=[{id:'f_rhythm',rank:1},{id:'f_cross',rank:1}];
 const input={...emptyState(0),career,skill_gems:[{slot:0,active:'f_cross',supports:[null,null]}]};
 const saved=await request(app).put(`/characters/${c.id}/state`).set(auth(account)).set(ver()).send(input);
 expect(saved.status).toBe(200);
 const read=await request(app).get(`/characters/${c.id}`).set(auth(account)).set(ver());
 expect(read.body.data.character.state.career).toEqual(career);
 expect(read.body.data.character.state.skill_gems[0].active).toBe('f_cross');
 const bad=await request(app).put(`/characters/${c.id}/state`).set(auth(account)).set(ver()).send({...input,version:1,career:blank(3)});
 expect(bad.status).toBe(422);
});
