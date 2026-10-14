// "확률 표시가 정확했다"는 증거: 공개 문서에 적힌 확률(이용자가 보는 값)을 파싱해, 실제 추첨 코드의 빈도와 비교한다.
// 칸마다 |관측-기대| <= 4.5 표준편차, 카이제곱 p > 1e-6, 확률 합 100%±0.01. 난수는 시드 고정(재현 가능).
import path from 'node:path';
import { getConfig, initConfig } from '../src/config/env';
import { buildRatesMarkdown } from '../src/domains/policies/ratesDisclosure';
import { BANNER_WEIGHT, GEAR_RARITY_NAME, RARITY_NAME, type CosmeticBanner, type Rarity } from '../src/domains/starshop/starshopDefs';
import { gearTable, pickGearTier, rollRarity } from '../src/domains/starshop/starshopService';
import { luckBoxHit, pickRow } from '../src/domains/sealedbox/sealedBoxService';
import { rollEnhance } from '../src/domains/enhance/enhanceService';
import { rollShopRarity } from '../src/domains/raidshop/raidShopService';
import { getGameData, initGameData } from '../src/gamedata/loader';
import { getSealedBox, sealedTable } from '../src/gamedata/sealedBox';
import { setRng, type Rng } from '../src/utils/rng';

const N = 300_000;

// ---------- 시드 고정 난수(sfc32) ----------
function seeded(seed: number): Rng {
  let a = 0x9e3779b9 ^ seed;
  let b = 0x243f6a88 + seed;
  let c = 0xb7e15162 ^ (seed * 7919);
  let d = 1;
  const next = (): number => {
    a >>>= 0; b >>>= 0; c >>>= 0; d >>>= 0;
    const t = (((a + b) | 0) + d) | 0;
    d = (d + 1) | 0;
    a = b ^ (b >>> 9);
    b = (c + (c << 3)) | 0;
    c = (c << 21) | (c >>> 11);
    c = (c + t) | 0;
    return (t >>> 0) / 4294967296;
  };
  for (let i = 0; i < 20; i++) next();
  return {
    int: (min, max) => (max <= min ? min : min + Math.floor(next() * (max - min))),
    unit: () => next(),
  };
}

// ---------- 카이제곱 ----------
function lgamma(x: number): number {
  const g = [76.18009172947146, -86.50532032941677, 24.01409824083091, -1.231739572450155, 0.1208650973866179e-2, -0.5395239384953e-5];
  let y = x;
  let tmp = x + 5.5;
  tmp -= (x + 0.5) * Math.log(tmp);
  let ser = 1.000000000190015;
  for (const c of g) ser += c / ++y;
  return -tmp + Math.log((2.5066282746310005 * ser) / x);
}
/** 상측 정규화 불완전 감마 Q(a, x) */
function gammaQ(a: number, x: number): number {
  if (x <= 0) return 1;
  if (x < a + 1) {
    let sum = 1 / a;
    let del = sum;
    for (let n = 1; n < 500; n++) {
      del *= x / (a + n);
      sum += del;
      if (Math.abs(del) < Math.abs(sum) * 1e-14) break;
    }
    return 1 - sum * Math.exp(-x + a * Math.log(x) - lgamma(a));
  }
  let b = x + 1 - a;
  let c = 1 / 1e-300;
  let d = 1 / b;
  let h = d;
  for (let i = 1; i < 500; i++) {
    const an = -i * (i - a);
    b += 2;
    d = an * d + b;
    if (Math.abs(d) < 1e-300) d = 1e-300;
    c = b + an / c;
    if (Math.abs(c) < 1e-300) c = 1e-300;
    d = 1 / d;
    const del = d * c;
    h *= del;
    if (Math.abs(del - 1) < 1e-14) break;
  }
  return Math.exp(-x + a * Math.log(x) - lgamma(a)) * h;
}

/** 칸별 관측 횟수를 기대 확률(%)과 비교한다. 기대 합이 100%±0.01이어야 한다 */
function expectFits(label: string, observed: Map<string, number>, expectedPct: Map<string, number>, n: number): void {
  const keys = new Set([...observed.keys(), ...expectedPct.keys()]);
  const total = [...expectedPct.values()].reduce((a, v) => a + v, 0);
  expect({ label, sumOk: Math.abs(total - 100) <= 0.01 }).toEqual({ label, sumOk: true });
  let chi = 0;
  let pooledObs = 0;
  let pooledExp = 0;
  let cells = 0;
  for (const k of keys) {
    const p = (expectedPct.get(k) ?? 0) / 100;
    const o = observed.get(k) ?? 0;
    const e = n * p;
    if (p === 0) {
      expect({ label, cell: k, observed: o }).toEqual({ label, cell: k, observed: 0 });
      continue;
    }
    const sd = Math.sqrt(n * p * (1 - p));
    const dev = sd === 0 ? (o === e ? 0 : Infinity) : Math.abs(o - e) / sd;
    expect({ label, cell: k, withinSd: dev <= 4.5, dev: Number(dev.toFixed(2)) }).toMatchObject({ withinSd: true });
    if (e < 5) {
      pooledObs += o;
      pooledExp += e;
    } else {
      chi += (o - e) ** 2 / e;
      cells++;
    }
  }
  if (pooledExp > 0) {
    chi += (pooledObs - pooledExp) ** 2 / pooledExp;
    cells++;
  }
  if (cells > 1) {
    const pValue = gammaQ((cells - 1) / 2, chi / 2);
    expect({ label, pOk: pValue > 1e-6, pValue }).toMatchObject({ pOk: true });
  }
}

const tally = (n: number, draw: () => string): Map<string, number> => {
  const m = new Map<string, number>();
  for (let i = 0; i < n; i++) {
    const k = draw();
    m.set(k, (m.get(k) ?? 0) + 1);
  }
  return m;
};

// ---------- 공개 문서 파싱 ----------
const pctNum = (s: string): number => Number(s.replace('%', '').trim());
const cells = (line: string): string[] => line.split('|').slice(1, -1).map((c) => c.trim());
/** "## 제목" 구역의 줄들(다음 "## " 전까지) */
function section(md: string, titleStart: string): string[] {
  const lines = md.split('\n');
  const start = lines.findIndex((l) => l.startsWith(`## ${titleStart}`));
  if (start < 0) throw new Error(`공개 문서에 구역이 없습니다: ${titleStart}`);
  const end = lines.findIndex((l, i) => i > start && l.startsWith('## '));
  return lines.slice(start + 1, end < 0 ? undefined : end);
}
const tableRows = (lines: string[]): string[][] => lines.filter((l) => l.startsWith('|') && !l.startsWith('|---')).map(cells).slice(1);
/** "### 소제목" 아래 표들 */
function subSections(lines: string[]): { title: string; rows: string[][] }[] {
  const out: { title: string; rows: string[][] }[] = [];
  let cur: { title: string; lines: string[] } | null = null;
  const all: { title: string; lines: string[] }[] = [];
  for (const l of lines) {
    if (l.startsWith('### ')) {
      cur = { title: l.slice(4).trim(), lines: [] };
      all.push(cur);
    } else cur?.lines.push(l);
  }
  for (const s of all) out.push({ title: s.title, rows: tableRows(s.lines) });
  return out;
}

const RARITY_BY_NAME = new Map<string, Rarity>((Object.entries(RARITY_NAME) as [Rarity, string][]).map(([r, n]) => [n, r]));
const GEAR_BY_NAME = new Map<string, string>(Object.entries(GEAR_RARITY_NAME).map(([r, n]) => [n, r]));

let md = '';
beforeAll(() => {
  initConfig({ ...process.env });
  initGameData(getConfig().gameDataDir);
  md = buildRatesMarkdown(path.resolve(getConfig().gameDataDir), 'ko', new Date(0));
});
afterEach(() => setRng(null));

describe('공개 확률 문서 vs 실제 추첨 빈도', () => {
  it('외형 뽑기: 오라·스킨의 등급 확률과 보너스 1회 등급 확률', () => {
    const subs = subSections(section(md, '외형 뽑기'));
    expect(subs.length).toBeGreaterThanOrEqual(3);
    subs.forEach((s, idx) => {
      const banner: CosmeticBanner = s.title.startsWith('오라') ? 'aura' : 'skin';
      const grade = new Map<string, number>();
      const bonus = new Map<string, number>();
      for (const r of s.rows) {
        const rar = RARITY_BY_NAME.get(r[0] as string) as Rarity;
        grade.set(rar, pctNum(r[1] as string));
        bonus.set(rar, pctNum(r[2] as string));
      }
      // 문서에 없는 등급은 기대 0%: 서버 가중치에서 0이 아닌 등급이 문서에 빠지면 여기서 실패한다
      for (const [r, w] of Object.entries(BANNER_WEIGHT[banner])) if (w > 0) expect(grade.has(r)).toBe(true);
      setRng(seeded(100 + idx));
      expectFits(`${s.title} 등급`, tally(N, () => rollRarity(banner)), grade, N);
      setRng(seeded(200 + idx));
      expectFits(`${s.title} 보너스`, tally(N, () => rollRarity(banner, true)), bonus, N);
    });
  });

  it('장비 뽑기: 문서의 등급 확률과 직업·단계·뽑기별 실제 등급 빈도(없는 등급 몫 이전 규칙 포함)', () => {
    const doc = new Map<string, number>();
    for (const r of tableRows(section(md, '장비 뽑기'))) doc.set(GEAR_BY_NAME.get(r[0] as string) as string, pctNum(r[1] as string));
    expect(Math.abs([...doc.values()].reduce((a, v) => a + v, 0) - 100)).toBeLessThanOrEqual(0.01);
    const tiers = [...new Set(getGameData().economy.shop.equipmentList.map((e) => e.levelTier))].sort((a, b) => a - b);
    let seed = 300;
    let checked = 0;
    for (const banner of ['weapon', 'armor', 'accessory'] as const) {
      for (const cls of ['warrior', 'mage']) {
        for (const tier of tiers) {
          const table = gearTable(banner, cls, tier);
          if (table.length === 0) continue;
          // 문서 문장대로: 그 뽑기에 없는 등급의 몫은 있는 등급 중 가장 낮은 등급이 가진다(레전더리가 없으면 유니크)
          const present = ['Uncommon', 'Rare', 'Epic', 'Unique', 'Legendary'].filter((r) => table.some((t) => t.rarity === r.toLowerCase()));
          const lowest = present[0] as string;
          const legendToUnique = !present.includes('Legendary') && present.includes('Unique');
          const expected = new Map<string, number>();
          for (const r of present) expected.set(r.toLowerCase(), doc.get(r) ?? 0);
          for (const [r, v] of doc) {
            if (present.includes(r)) continue;
            const to = legendToUnique && r === 'Legendary' ? 'Unique' : lowest;
            expected.set(to.toLowerCase(), (expected.get(to.toLowerCase()) ?? 0) + v);
          }
          setRng(seeded(++seed));
          expectFits(`장비 ${banner}/${cls}/${tier}`, tally(N, () => pickGearTier(table).rarity), expected, N);
          checked++;
        }
      }
    }
    expect(checked).toBeGreaterThanOrEqual(6);
  });

  it('봉인된 상자: 일반 표와 부스터 표의 칸별 확률', () => {
    const rows = tableRows(section(md, '봉인된 상자'));
    const sealed = getSealedBox();
    expect(rows.length).toBe(sealed.rows.length);
    for (const boosted of [false, true]) {
      const table = sealedTable(sealed, boosted);
      const expected = new Map<string, number>();
      rows.forEach((r, i) => expected.set((sealed.rows[i] as { id: string }).id, pctNum(r[boosted ? 2 : 1] as string)));
      setRng(seeded(boosted ? 401 : 400));
      expectFits(`봉인된 상자 ${boosted ? '부스터' : '일반'}`, tally(N, () => pickRow(table).row.id), expected, N);
    }
  });

  it('강화 성공 판정: 문서의 모든 단계 성공 확률 (roll < successPercent, rng.int(0, rollRange))', () => {
    const data = JSON.parse(require('node:fs').readFileSync(path.join(getConfig().gameDataDir, 'enhance.json'), 'utf8')) as {
      rollRange: number;
      steps: { id: string; levels: { from: number; successPercent: number }[] }[];
    };
    const docRows = subSections(section(md, '장비 강화')).flatMap((s) => s.rows);
    expect(docRows.length).toBeGreaterThan(0);
    // 데이터의 (단계, 확률)이 모두 문서에 표시되어 있어야 한다
    const shown = new Set(docRows.map((r) => `${(r[0] as string).replace(/^\+(\d+) .*/, '$1')}:${pctNum(r[1] as string)}`));
    for (const st of data.steps) for (const l of st.levels) expect(shown.has(`${l.from}:${l.successPercent}`)).toBe(true);
    const rates = [...new Set(docRows.map((r) => pctNum(r[1] as string)))];
    let seed = 500;
    for (const rate of rates) {
      setRng(seeded(++seed));
      const m = tally(N, () => (rollEnhance(data.rollRange, rate).success ? 'success' : 'fail'));
      expectFits(`강화 ${rate}%`, m, new Map([['success', rate], ['fail', 100 - rate]]), N);
    }
  });

  it('강화권 상자: 문서의 성공 확률과 luckBoxHit(chance)', () => {
    const docRows = tableRows(section(md, '강화권 상자'));
    const luck = [...getGameData().economy.items.entries()].filter(([, v]) => v.use === 'LuckBox');
    expect(luck.length).toBe(docRows.length);
    let seed = 600;
    for (const [id, info] of luck) {
      const row = docRows.find((r) => r[0] === info.name) as string[];
      expect(row).toBeDefined();
      const rate = pctNum(row[2] as string);
      setRng(seeded(++seed));
      expectFits(`강화권 상자 ${id}`, tally(N, () => (luckBoxHit(info.chance) ? 'hit' : 'miss')), new Map([['hit', rate], ['miss', 100 - rate]]), N);
    }
  });

  it('레이드 상점 상자: 문서의 등급 확률과 rollShopRarity', () => {
    const docRows = tableRows(section(md, '레이드 상점'));
    const products = [...(getGameData().raidShop?.products.values() ?? [])];
    expect(products.length).toBe(docRows.length);
    let seed = 700;
    for (const p of products) {
      const row = docRows.find((r) => r[0] === p.id) as string[];
      expect(row).toBeDefined();
      const expected = new Map<string, number>();
      for (const part of (row[2] as string).split(', ')) {
        const m = /^(\S+) ([0-9.]+)%$/.exec(part) as RegExpExecArray;
        expected.set(GEAR_BY_NAME.get(m[1] as string) as string, Number(m[2]));
      }
      setRng(seeded(++seed));
      const rng = seeded(seed);
      expectFits(`레이드 ${p.id}`, tally(N, () => rollShopRarity(p, rng)), expected, N);
    }
  });
});
