// 확률형 아이템 정보 공개(게임산업법 제33조 제2항). 서버가 실제로 쓰는 확률표에서 바로 만든다:
// 외형 뽑기·장비 뽑기·외형 합성(starshopDefs.ts), 봉인된 상자·부스터·천장(sealed_box.json), 강화권 상자(items.json box_enh*),
// 장비 강화(enhance.json), 레이드 상점(raid_shop.json). 웹 페이지(GET /policies/:lang/probability)와 scripts/export-rates.ts가 같은 함수를 쓴다.
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { sealedTable, type SealedData } from '../../gamedata/sealedBox';
import { translate } from '../../utils/i18n';
import {
  BANNER_WEIGHT,
  GAUGE,
  GEAR_CEILING,
  GEAR_RARITY_NAME,
  GEAR_RARITY_PERMILLE,
  GEAR_RATE_SCALE,
  PULL_PRICE,
  RARITY_NAME,
  RATE_SCALE,
  RATES_VERSION,
  SYNTH,
  SYNTH_COUNT,
  TEN_COUNT,
  TEN_PRICE,
  type CosmeticBanner,
  type Rarity,
} from '../starshop/starshopDefs';
import { poolOf } from '../starshop/starshopService';
import type { PolicyLang } from './policyVersions';

const pct = (v: number): string => `${v.toFixed(3)}%`;

interface ItemRow {
  id: string;
  name?: string;
  use?: string;
  power?: number;
  chance?: number;
}

interface EnhanceLevel {
  from: number;
  successPercent: number;
  failure: string;
  pity: boolean;
}
interface EnhanceData {
  pityPerFailure: number;
  drop3Levels: number;
  ticketItem: string;
  steps: { id: string; levels: EnhanceLevel[] }[];
}
const GEAR_KIND_KO: Record<string, string> = { sword: '검', staff: '지팡이', plate: '전사 상의', robe: '마법사 상의', greaves: '전사 하의', skirt: '마법사 하의', ring: '반지', neck: '목걸이' };
const GEAR_KIND_EN: Record<string, string> = { sword: 'Swords', staff: 'Staves', plate: 'Warrior tops', robe: 'Mage tops', greaves: 'Warrior bottoms', skirt: 'Mage bottoms', ring: 'Rings', neck: 'Necklaces' };

const readJson = <T>(dir: string, file: string): T | null => {
  const p = path.join(dir, file);
  return fs.existsSync(p) ? (JSON.parse(fs.readFileSync(p, 'utf8')) as T) : null;
};

/** 확률표 공개 문서(Markdown). now는 문서에 적는 생성 시각 */
export function buildRatesMarkdown(dataDir: string, lang: PolicyLang, now: Date = new Date()): string {
  const ko = lang === 'ko';
  const t = (s: string): string => (ko ? s : translate('en', s));
  const items = new Map<string, ItemRow>();
  for (const it of readJson<{ items: ItemRow[] }>(dataDir, 'items.json')?.items ?? []) items.set(it.id, it);
  const itemName = (key: string): string => t(items.get(key)?.name ?? key);
  const rarity = (r: Rarity): string => (ko ? RARITY_NAME[r] : r[0]!.toUpperCase() + r.slice(1));
  const gearRarity = (r: string): string => (ko ? (GEAR_RARITY_NAME[r] ?? r) : r);
  const out: string[] = [];

  out.push(ko ? '# 확률형 아이템 정보 공개' : '# Probability Disclosure (Random Items)');
  out.push(
    ko
      ? `이 문서는 서버가 실제로 쓰는 확률표에서 자동으로 만듭니다. 생성 시각: ${now.toISOString()}. 확률은 소수점 셋째 자리까지 표시하며, 표시 과정의 반올림 때문에 합이 100.000%와 조금 다를 수 있습니다. 확률이 바뀌면 이 문서와 게임 안 확률표가 함께 바뀌며, 변경 공지 절차는 운영정책을 따릅니다.`
      : `Generated automatically from the rate tables the server actually uses. Generated at: ${now.toISOString()}. Probabilities are shown to three decimals; rounding may make a total differ slightly from 100.000%. When rates change, this page and the in-game rate tables change together; change notices follow the Operation Policy.`,
  );

  // ---- 외형 뽑기 ----
  out.push(ko ? `## 외형 뽑기 (확률표 버전 ${RATES_VERSION})` : `## Cosmetic Draws (rate table version ${RATES_VERSION})`);
  out.push(
    ko
      ? `가격: 1회 별조각 ${PULL_PRICE}개, ${TEN_COUNT}회 별조각 ${TEN_PRICE}개. ${TEN_COUNT}회 묶음의 마지막 1회(보너스)는 일반 등급이 나오지 않고 나머지 등급이 같은 비율로 커집니다(표의 보너스 열). 같은 등급 안에서는 아직 갖지 않은 외형이 같은 확률로 나오고, 그 등급을 모두 가졌으면 중복으로 나와 합성 재료가 됩니다. 아래 외형별 확률은 아무것도 갖지 않았을 때 기준입니다.`
      : `Price: 1 draw ${PULL_PRICE} Star Shards, ${TEN_COUNT} draws ${TEN_PRICE} Star Shards. The last draw of the ${TEN_COUNT}-pack (bonus) excludes the Common grade; the other grades grow in proportion (bonus columns). Within a grade, cosmetics you do not own yet share the grade's chance equally; once you own all of a grade, duplicates become synthesis materials. Per-item rates below assume you own nothing yet.`,
  );
  const banners: { id: CosmeticBanner; ko: string; en: string; cls?: string }[] = [
    { id: 'aura', ko: '오라 뽑기', en: 'Aura Draw' },
    { id: 'skin', ko: '스킨 뽑기 (전사)', en: 'Skin Draw (Warrior)', cls: 'warrior' },
    { id: 'skin', ko: '스킨 뽑기 (마법사)', en: 'Skin Draw (Mage)', cls: 'mage' },
  ];
  for (const b of banners) {
    out.push(`### ${ko ? b.ko : b.en}`);
    // 보너스 1회: 일반을 빼고 나머지 등급은 같은 비율(starshopService rollRarity)
    const notCommon = RATE_SCALE - BANNER_WEIGHT[b.id].common;
    out.push(
      ko
        ? '| 등급 | 등급 확률 | 보너스 1회 등급 확률 | 외형 | 외형별 확률 | 보너스 1회 외형별 확률 |'
        : '| Grade | Grade rate | Bonus-draw grade rate | Cosmetic | Per-item rate | Bonus-draw per-item rate |',
    );
    out.push('|---|---|---|---|---|---|');
    for (const r of ['unique', 'epic', 'rare', 'common'] as Rarity[]) {
      const w = BANNER_WEIGHT[b.id][r];
      if (w <= 0) continue;
      const tier = (w * 100) / RATE_SCALE;
      const bonus = r === 'common' || notCommon <= 0 ? 0 : (w * 100) / notCommon;
      const pool = poolOf(b.id, r, b.cls ?? 'warrior');
      const n = Math.max(1, pool.length);
      for (const c of pool) out.push(`| ${rarity(r)} | ${pct(tier)} | ${pct(bonus)} | ${t(c.name)} | ${pct(tier / n)} | ${pct(bonus / n)} |`);
    }
    const g = GAUGE[b.id];
    out.push(
      ko
        ? `선택 게이지: 1회마다 1칸, ${g.max}칸이 차면 ${rarity(g.rarity)} 등급 외형 하나를 직접 고릅니다.`
        : `Choice gauge: +1 per draw; at ${g.max} you pick one ${rarity(g.rarity)} cosmetic yourself.`,
    );
  }

  // ---- 외형 합성 ----
  out.push(ko ? '## 외형 합성' : '## Cosmetic Synthesis');
  out.push(ko ? `같은 등급 여분 ${SYNTH_COUNT}개를 넣습니다. 실패하면 넣은 것 중 1개가 돌아옵니다.` : `Uses ${SYNTH_COUNT} spares of the same grade. On failure, 1 of them is returned.`);
  out.push(ko ? '| 재료 등급 | 결과 등급 | 성공 확률 | 확정 |' : '| Material grade | Result grade | Success rate | Guaranteed |');
  out.push('|---|---|---|---|');
  for (const [from, s] of Object.entries(SYNTH) as [Rarity, (typeof SYNTH)['common']][]) {
    const pity = s.pity > 0 ? (ko ? `${s.pity}회 연속 실패 뒤 다음 합성 성공` : `success after ${s.pity} failures in a row`) : ko ? '없음' : 'none';
    out.push(`| ${rarity(from)} | ${rarity(s.to)} | ${pct(s.permille / 10)} | ${pity} |`);
  }

  // ---- 장비 뽑기 ----
  out.push(ko ? `## 장비 뽑기: 무기·방어구·장신구 (확률표 버전 ${RATES_VERSION})` : `## Gear Draws: Weapon, Armor, Accessory (rate table version ${RATES_VERSION})`);
  out.push(ko ? '| 등급 | 확률 |' : '| Grade | Rate |');
  out.push('|---|---|');
  for (const [r, w] of GEAR_RARITY_PERMILLE) out.push(`| ${gearRarity(r)} | ${pct((w * 100) / GEAR_RATE_SCALE)} |`);
  out.push(
    ko
      ? `${TEN_COUNT}회 묶음의 보너스 1회도 같은 확률입니다. 내 직업이 쓸 수 있는 +0 장비가 나옵니다. 그 뽑기에 없는 등급의 몫은 있는 등급 중 가장 낮은 등급이 가지며(레전더리가 없으면 유니크), 같은 등급 안에서는 장비마다 같은 확률입니다. 천장: 세 장비 뽑기를 합쳐 ${GEAR_CEILING.max}회마다 ${gearRarity('Unique')} 장비 하나를 고릅니다(그 단계에 유니크가 없으면 가장 높은 등급).`
      : `The bonus draw of the ${TEN_COUNT}-pack uses the same rates. Draws +0 gear your class can use. A grade missing from a draw passes its share to the lowest grade present (Unique when there is no Legendary); items within a grade share equally. Pity: every ${GEAR_CEILING.max} gear draws (all three combined) you pick one Unique item (the highest grade if that tier has no Unique).`,
  );

  // ---- 봉인된 상자 ----
  const sealed = readJson<SealedData>(dataDir, 'sealed_box.json');
  if (sealed) {
    out.push(ko ? `## 봉인된 상자 (확률표 버전 ${sealed.ratesVersion})` : `## Sealed Chest (rate table version ${sealed.ratesVersion})`);
    out.push(
      ko
        ? `가격: 1회 별조각 ${sealed.priceOne}개, ${sealed.elevenCount}회 별조각 ${sealed.priceEleven}개. ${sealed.boosterEvery}회마다 다음 1회는 부스터가 적용되어 희귀 이상 확률이 2배(일반 등급은 같은 비율로 줄어 합 100%), 지급 수량도 2배입니다.`
        : `Price: 1 open ${sealed.priceOne} Star Shards, ${sealed.elevenCount} opens ${sealed.priceEleven} Star Shards. Every ${sealed.boosterEvery} opens the next one is boosted: Rare-or-better chances double (common rows shrink proportionally so the total stays 100%) and amounts double.`,
    );
    out.push(ko ? '| 보상 | 확률 | 부스터 확률 |' : '| Reward | Rate | Boosted rate |');
    out.push('|---|---|---|');
    const boosted = sealedTable(sealed, true);
    sealed.rows.forEach((row, i) => {
      const what = row.rewards.map((r) => `${itemName(r.item_key)} x${r.count}`).join(', ');
      out.push(`| ${what} | ${pct(row.rate)} | ${pct(boosted[i]!.rate)} |`);
    });
    out.push(
      ko
        ? `천장: ${sealed.ceiling.every}회 열 때마다 ${itemName(sealed.ceiling.reward.item_key)} ${sealed.ceiling.reward.count}개와 교환할 수 있습니다.`
        : `Pity: every ${sealed.ceiling.every} opens you can claim ${itemName(sealed.ceiling.reward.item_key)} x${sealed.ceiling.reward.count}.`,
    );
  }

  // ---- 강화권 상자 ----
  const luck = [...items.values()].filter((i) => i.use === 'LuckBox');
  if (luck.length) {
    const fb = sealed?.luckBoxFallback;
    out.push(ko ? '## 강화권 상자' : '## Enhancement Ticket Boxes');
    out.push(ko ? '| 상자 | 성공 시 | 성공 확률 | 실패 시 |' : '| Box | On success | Success rate | On failure |');
    out.push('|---|---|---|---|');
    for (const b of luck) {
      const fail = fb ? `${itemName(fb.item_key)} x${fb.count}` : '-';
      out.push(`| ${itemName(b.id)} | +${b.power} ${t('강화권')} | ${pct(b.chance ?? 0)} | ${fail} |`);
    }
  }

  // ---- 장비 강화 ----
  const enh = readJson<EnhanceData>(dataDir, 'enhance.json');
  if (enh) {
    out.push(ko ? '## 장비 강화' : '## Gear Enhancement');
    out.push(
      ko
        ? `강화 비용(골드와 재료)은 시도할 때 소모되며 결과와 관계없이 돌아오지 않습니다. 천장 표시가 있는 단계는 실패할 때마다 그 장비의 다음 시도 성공 확률이 ${enh.pityPerFailure}%p씩 올라가고(최대 100%), 성공하면 초기화됩니다. 실패 시 "파괴" 단계에서 가방에 ${itemName(enh.ticketItem)}이 있으면 1개를 써서 파괴 대신 +0으로 남깁니다(시작 장비 제외).`
        : `Enhancement costs (gold and materials) are consumed on each attempt regardless of the result. On steps marked with pity, each failure raises that item's next success chance by ${enh.pityPerFailure}%p (up to 100%), reset on success. On "Destroyed" steps, if you carry ${itemName(enh.ticketItem)}, one is used and the item stays at +0 instead (starter gear excluded).`,
    );
    const failText = (f: string): string => {
      if (f === 'Keep') return ko ? '단계 유지' : 'Level kept';
      if (f === 'Drop3') return ko ? `${enh.drop3Levels}단계 하락` : `Drops ${enh.drop3Levels} levels`;
      return ko ? '파괴' : 'Destroyed';
    };
    const kind = (id: string): string => id.replace(/^eq_/, '').replace(/_.*/, '');
    const groups = new Map<string, { kinds: Set<string>; levels: EnhanceLevel[] }>();
    for (const st of enh.steps) {
      const sig = st.levels.map((l) => `${l.from}:${l.successPercent}:${l.failure}:${l.pity}`).join('|');
      const g = groups.get(sig) ?? { kinds: new Set<string>(), levels: st.levels };
      g.kinds.add(kind(st.id));
      groups.set(sig, g);
    }
    for (const g of groups.values()) {
      const names = [...g.kinds].map((k) => (ko ? (GEAR_KIND_KO[k] ?? k) : (GEAR_KIND_EN[k] ?? k))).join(', ');
      out.push(`### ${names}`);
      out.push(ko ? '| 단계 | 성공 확률 | 실패 시 | 천장 |' : '| Step | Success rate | On failure | Pity |');
      out.push('|---|---|---|---|');
      for (const l of g.levels) {
        out.push(`| +${l.from} → +${l.from + 1} | ${pct(l.successPercent)} | ${failText(l.failure)} | ${l.pity ? (ko ? '있음' : 'Yes') : '-'} |`);
      }
    }
  }

  // ---- 레이드 상점 ----
  const raid = readJson<{ ratesVersion: string; products: { id: string; price: number; materialItem: string; category: string; rarityWeights: Record<string, number> }[] }>(dataDir, 'raid_shop.json');
  if (raid) {
    out.push(ko ? `## 레이드 상점 무기 상자 (확률표 버전 ${raid.ratesVersion}, 유료 재화 아님)` : `## Raid Shop Weapon Boxes (rate table version ${raid.ratesVersion}, not paid currency)`);
    out.push(ko ? '| 상품 | 가격 | 등급 확률 |' : '| Product | Price | Grade rates |');
    out.push('|---|---|---|');
    for (const p of raid.products) {
      const total = Object.values(p.rarityWeights).reduce((a, w) => a + w, 0) || 1;
      const rates = Object.entries(p.rarityWeights).map(([r, w]) => `${gearRarity(r)} ${pct((w * 100) / total)}`).join(', ');
      out.push(`| ${p.id} | ${itemName(p.materialItem)} x${p.price} | ${rates} |`);
    }
  }
  return out.join('\n\n').replace(/\n\n(\|)/g, '\n$1');
}


// ---- 공개 기록(이용약관 제14조의2 3항): 생성 시각을 뺀 한국어판 전문의 sha256 지문 ----
const FIXED_NOW = new Date(0);
const STAMP_RE = /생성 시각: 1970-01-01T00:00:00\.000Z\. /;

export interface RatesPublicationDoc {
  fingerprint: string;
  versions: Record<string, string>;
  content: string;
}

/** 지금 공개 중인 확률 문서(한국어판, 생성 시각 제외)와 지문, 각 표의 버전 */
export function buildRatesPublication(dataDir: string): RatesPublicationDoc {
  const content = buildRatesMarkdown(dataDir, 'ko', FIXED_NOW).replace(STAMP_RE, '');
  const sealed = readJson<{ ratesVersion: string }>(dataDir, 'sealed_box.json');
  const raid = readJson<{ ratesVersion: string }>(dataDir, 'raid_shop.json');
  const versions: Record<string, string> = { cosmetic: RATES_VERSION };
  if (sealed) versions.sealed = sealed.ratesVersion;
  if (raid) versions.raid = raid.ratesVersion;
  return { fingerprint: createHash('sha256').update(content, 'utf8').digest('hex'), versions, content };
}
