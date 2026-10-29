// 확률형 아이템 정보 공개(게임산업법 제33조 제2항). 서버가 실제로 쓰는 확률표에서 바로 만든다:
// 외형 뽑기·장비 뽑기·외형 합성(starshopDefs.ts), 봉인된 상자·부스터·천장·별조각 상자(sealed_box.json), 강화권 상자(items.json box_enh*),
// 장비 강화(enhance.json), 레이드 상점(raid_shop.json), 일일·주간 완료 상자(daily_quests.json chests). 웹 페이지(GET /policies/:lang/probability)와 scripts/export-rates.ts가 같은 함수를 쓴다.
import { createHash } from 'node:crypto';
import fs from 'node:fs';
import path from 'node:path';
import { sealedTable, starBoxExpected, type SealedData } from '../../gamedata/sealedBox';
import { UNIQUE_BONUS } from '../dungeons/dungeonRules';
import { translate } from '../../utils/i18n';
import {
  EXCHANGE_GOOD_BY_ID,
  HIDDEN_NAME,
  GEAR_CEILING,
  GEAR_RARITY_NAME,
  GEAR_RARITY_PERMILLE,
  GEAR_RATE_SCALE,
  PULL_PRICE,
  RARITY_NAME,
  RATE_SCALE,
  RATES_VERSION,
  STAR_COSMETICS,
  SYNTH,
  SYNTH_COUNT,
  TEN_COUNT,
  TEN_PRICE,
  type CosmeticBanner,
  type Rarity,
} from '../starshop/starshopDefs';
import { gaugeView, rateTable } from '../starshop/starshopService';
import type { PolicyLang } from './policyVersions';

const pct = (v: number): string => `${v.toFixed(3)}%`;

type DailyItems = { item_key: string; count: number }[];
interface DailyChests {
  ratesVersion: string;
  fragment: { item: string; per: number; into: string };
  day: { streak: number; name: string; rewards: DailyItems; bonusRolls: number; maxLevel: DailyItems }[];
  week: { name: string; rewards: DailyItems; bonusRolls: number; maxLevel: DailyItems; skinGauge?: number };
  bonus: { id: string; weight: number; rewards: DailyItems }[];
  matBox: { item: string; reward: string; bands: { min: number; max: number; weight: number }[] };
}
const STAR_NAME = new Map(STAR_COSMETICS.map((c) => [c.id, c.name] as const));

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
  const slot = (r: Rarity, hidden: boolean): string => (hidden ? `${rarity(r)} ${ko ? HIDDEN_NAME : 'Hidden'}` : rarity(r));
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
      ? `가격: 1회 별조각 ${PULL_PRICE}개, ${TEN_COUNT}회 별조각 ${TEN_PRICE}개. ${TEN_COUNT}회 묶음의 마지막 1회(보너스)는 일반 보통 칸만 제외하며 일반 히든은 나올 수 있습니다. 남은 칸은 같은 비율로 커집니다(표의 보너스 열). 같은 등급·히든 여부의 칸 안에서는 아직 갖지 않은 외형이 같은 확률로 나오고, 그 칸을 모두 가졌으면 중복으로 나와 합성 재료가 됩니다. 아래 외형별 확률은 아무것도 갖지 않았을 때 기준입니다.`
      : `Price: 1 draw ${PULL_PRICE} Star Shards, ${TEN_COUNT} draws ${TEN_PRICE} Star Shards. The last draw of the ${TEN_COUNT}-pack (bonus) excludes only the non-Hidden Common slot; Common Hidden can still appear. The remaining slots grow in proportion (bonus columns). Within a slot of the same grade and Hidden status, cosmetics you do not own yet share that slot's chance equally; once you own all of a slot, duplicates become synthesis materials. Per-item rates below assume you own nothing yet.`,
  );
  out.push(
    ko
      ? `등급마다 "히든"이 있습니다(같은 등급 안의 특별 디자인, 공격력 보너스가 한 단계 높음). 등급 확률은 그 등급 히든을 포함한 합이며, 히든 1종의 확률은 같은 등급 보통 1종의 1/5입니다(전설 히든은 0.006% 고정). 아직 히든이나 전설 외형이 없는 직업은 그 몫이 같은 등급 보통(등급 전체가 없으면 한 등급 아래 보통)으로 옮겨 가며, 아래 표는 옮긴 뒤의 실제 확률입니다.`
      : `Every grade has "Hidden" cosmetics (special designs within the grade, one step higher attack bonus). A grade's rate includes its Hidden ones; one Hidden cosmetic has 1/5 the chance of one regular cosmetic of the same grade (Legend Hidden is fixed at 0.006%). For a class that has no Hidden or Legend cosmetic yet, that share moves to the regular ones of the same grade (or of the grade below when the whole grade is empty); the tables below show the actual rates after that move.`,
  );
  const banners: { id: CosmeticBanner; ko: string; en: string; cls: string }[] = [
    { id: 'aura', ko: '오라 뽑기', en: 'Aura Draw', cls: 'warrior' },
    { id: 'skin', ko: '스킨 뽑기 (전사)', en: 'Skin Draw (Warrior)', cls: 'warrior' },
    { id: 'skin', ko: '스킨 뽑기 (마법사)', en: 'Skin Draw (Mage)', cls: 'mage' },
    { id: 'skin', ko: '스킨 뽑기 (도적)', en: 'Skin Draw (Rogue)', cls: 'rogue' },
  ];
  for (const b of banners) {
    out.push(`### ${ko ? b.ko : b.en}`);
    // 보너스 1회: 일반(보통)을 빼고 나머지 칸은 같은 비율(starshopService rollSlot)
    out.push(
      ko
        ? '| 등급 | 등급 확률 | 보너스 1회 등급 확률 | 외형 | 외형별 확률 | 보너스 1회 외형별 확률 |'
        : '| Grade | Grade rate | Bonus-draw grade rate | Cosmetic | Per-item rate | Bonus-draw per-item rate |',
    );
    out.push('|---|---|---|---|---|---|');
    const table = rateTable(b.id, b.cls);
    for (const row of table) {
      for (const it of row.items) {
        const name = STAR_NAME.get(it.id) ?? it.id;
        out.push(`| ${slot(row.rarity, row.hidden)} | ${pct(row.rate)} | ${pct(row.bonus_rate)} | ${t(name)} | ${pct(it.rate)} | ${pct(it.bonus_rate)} |`);
      }
    }
    const gauges = b.id === 'aura' ? (['aura'] as const) : (['skin', 'hidden'] as const);
    for (const g of gauges) {
      const view = gaugeView(g, b.cls, 0);
      if (view.targets.length === 0) continue;
      const list = view.targets.map((x) => (ko ? `${slot(x.rarity, x.hidden)} ${x.cost}칸` : `${slot(x.rarity, x.hidden)} ${x.cost}`)).join(', ');
      const label = g === 'hidden' ? (ko ? '히든 게이지' : 'Hidden gauge') : ko ? '선택 게이지' : 'Choice gauge';
      out.push(
        ko
          ? `${label}: 1회마다 1칸 찹니다. 칸이 차면 그 목표의 외형 하나를 직접 고르고 그만큼 칸이 줄며, 남은 칸은 이어집니다(${list}). 뽑기에서 자연히 나와도 칸은 줄지 않습니다.`
          : `${label}: +1 per draw. When it reaches a target you pick one cosmetic of that target yourself and that many points are spent; the rest carries over (${list}). Natural drops never reduce it.`,
      );
    }
  }

  // ---- 외형 합성 ----
  out.push(ko ? '## 외형 합성' : '## Cosmetic Synthesis');
  out.push(
    ko
      ? `같은 등급 여분 ${SYNTH_COUNT}개를 넣습니다. 실패하면 넣은 것 중 1개가 돌아옵니다. 히든 여분은 재료로 쓰지 않으며, 결과에도 히든은 나오지 않습니다. 유니크 → 전설은 내 직업 스킨만 대상입니다.`
      : `Uses ${SYNTH_COUNT} spares of the same grade. On failure, 1 of them is returned. Hidden spares are not used as materials and results are never Hidden. Unique to Legend uses skins of your class only.`,
  );
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
  const gearGauge = EXCHANGE_GOOD_BY_ID.get('gear_gauge10') as { count: number; price: number; weekly: number };
  out.push(
    ko
      ? `${TEN_COUNT}회 묶음의 보너스 1회도 같은 확률입니다. 내 직업이 쓸 수 있는 +0 장비가 나옵니다. 그 뽑기에 없는 등급의 몫은 있는 등급 중 가장 낮은 등급이 가지며(레전더리가 없으면 유니크), 같은 등급 안에서는 장비마다 같은 확률입니다. 천장: 세 장비 뽑기를 합쳐 ${GEAR_CEILING.max}회마다 ${gearRarity('Unique')} 장비 하나를 고릅니다(그 단계에 유니크가 없으면 가장 높은 등급). 별빛 교환소의 "장비 뽑기 천장 +${gearGauge.count}칸"(별의 결정 ${gearGauge.price}개, 주 ${gearGauge.weekly}회)은 이 천장 칸만 채우며 확률은 바꾸지 않습니다.`
      : `The bonus draw of the ${TEN_COUNT}-pack uses the same rates. Draws +0 gear your class can use. A grade missing from a draw passes its share to the lowest grade present (Unique when there is no Legendary); items within a grade share equally. Pity: every ${GEAR_CEILING.max} gear draws (all three combined) you pick one Unique item (the highest grade if that tier has no Unique). The Starlight Exchange item "Gear pity +${gearGauge.count}" (${gearGauge.price} Star Crystals, ${gearGauge.weekly} per week) only fills this pity count and does not change any rate.`,
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

  // ---- 별조각 상자(sealed-2): 봉인된 상자 안의 상자도 열었을 때의 확률을 따로 공개한다 ----
  const starBox = sealed?.starBox;
  if (sealed && starBox) {
    const box = itemName(starBox.item_key);
    const ev = starBoxExpected(sealed).toFixed(1);
    out.push(ko ? `## 별조각 상자 (확률표 버전 ${sealed.ratesVersion})` : `## Star Shard Box (rate table version ${sealed.ratesVersion})`);
    out.push(
      ko
        ? `봉인된 상자에서 나오는 ${box}를 가방에서 열면 아래 확률로 별조각이 나옵니다. 나온 별조각은 무료 별조각이며 환불 대상이 아닙니다. 상자 1개의 기대값은 별조각 ${ev}개입니다.`
        : `Opening a ${box} (from the Sealed Chest) in the bag gives Star Shards by the rates below. They are free Star Shards and are not refundable. Expected value per box: ${ev} Star Shards.`,
    );
    out.push(ko ? '| 별조각 | 확률 |' : '| Star Shards | Rate |');
    out.push('|---|---|');
    for (const a of starBox.amounts) out.push(`| ${a.stars} | ${pct(a.rate)} |`);
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
  // ---- 요일 던전 유니크 보너스(0043) ----
  out.push(ko ? '## 요일 던전 유니크 보너스 (유료 재화 아님)' : '## Weekday Dungeon Unique Bonus (not paid currency)');
  out.push(
    ko
      ? `요일 던전을 클리어할 때마다 ${UNIQUE_BONUS.chancePct}% 확률로 운 카드 한 장이 그 난이도 단계의 유니크 장비가 됩니다. 유니크 없이 ${UNIQUE_BONUS.pityRuns - 1}판이 지나면 ${UNIQUE_BONUS.pityRuns}판째에는 반드시 나오고, 나오면 다시 처음부터 셉니다. 유니크가 나올 때는 그 단계에서 아직 유니크를 가지지 않은 부위(무기·상의·하의·목걸이·반지 중 내 직업이 쓸 수 있는 것)를 같은 확률로 먼저 고르고, 모두 가졌으면 전체에서 같은 확률로 고릅니다. 같은 부위 안에서는 장비마다 같은 확률입니다. 일반 던전·레이드·소탕에는 적용하지 않습니다.`
      : `Each weekday dungeon clear has a ${UNIQUE_BONUS.chancePct}% chance to turn one luck card into a Unique piece of that difficulty's gear tier. After ${UNIQUE_BONUS.pityRuns - 1} clears without one, the ${UNIQUE_BONUS.pityRuns}th clear always gives one, and the count starts over. The slot is picked evenly among the slots (weapon, top, bottom, necklace, ring usable by your class) where you have no Unique of that tier yet, or evenly among all of them if you have every slot; within a slot each piece is equally likely. Normal dungeons, raids and sweeps do not have this bonus.`,
  );
  // ---- 일일·주간 완료 상자(0055) ----
  const daily = readJson<{ chests?: DailyChests }>(dataDir, 'daily_quests.json')?.chests;
  if (daily) {
    const list = (rs: { item_key: string; count: number }[]): string => rs.map((r) => `${itemName(r.item_key)} x${r.count}`).join(', ');
    out.push(ko ? `## 일일·주간 완료 상자 (확률표 버전 ${daily.ratesVersion}, 유료 재화 아님)` : `## Daily and Weekly Completion Chests (rate table version ${daily.ratesVersion}, not paid currency)`);
    out.push(
      ko
        ? `일일 의뢰를 그날 모두 끝내면 일일 상자, 주간 의뢰를 모두 끝내면 주간 상자를 한 번 엽니다. 일일 상자는 연속으로 연 날 수에 따라 단계가 오르고 ${daily.day.length}일째마다 가장 높은 단계가 되며, 하루라도 못 열면 1단계부터 다시 셉니다(어제 의뢰는 오늘까지 끝낼 수 있습니다). 고정 내용은 확정 지급이고, 보너스 굴림은 아래 표를 굴립니다. 만렙이면 만렙 추가분을 더 줍니다. ${itemName(daily.fragment.item)} ${daily.fragment.per}개가 모이면 자동으로 ${itemName(daily.fragment.into)} 1개가 됩니다.`
        : `Finishing all of a day's daily jobs opens one daily chest; finishing all weekly jobs opens one weekly chest. The daily chest steps up with consecutive days opened and reaches the top step every ${daily.day.length} days; missing a day starts again from step 1 (yesterday's jobs can still be finished today). Fixed contents are guaranteed; bonus rolls use the table below. At max level the max-level extras are added. Every ${daily.fragment.per} ${itemName(daily.fragment.item)} turn into 1 ${itemName(daily.fragment.into)} automatically.`,
    );
    out.push(ko ? '| 상자 | 고정 내용 | 만렙 추가 | 보너스 굴림 |' : '| Chest | Fixed contents | Max-level extras | Bonus rolls |');
    out.push('|---|---|---|---|');
    for (const st of daily.day) out.push(`| ${ko ? `${st.streak}일째 ${st.name}` : `Day ${st.streak} ${t(st.name)}`} | ${list(st.rewards)} | ${list(st.maxLevel) || '-'} | ${st.bonusRolls} |`);
    const wk = daily.week;
    const gauge = (wk.skinGauge ?? 0) > 0 ? (ko ? `, 스킨 선택 게이지 +${wk.skinGauge}칸(계정당 주 1회)` : `, Skin choice gauge +${wk.skinGauge} (once per account per week)`) : '';
    out.push(`| ${t(wk.name)} | ${list(wk.rewards)}${gauge} | ${list(wk.maxLevel) || '-'} | ${wk.bonusRolls} |`);
    const total = daily.bonus.reduce((a, b) => a + b.weight, 0) || 1;
    out.push(ko ? '### 보너스 굴림 (1회)' : '### Bonus Roll (each)');
    out.push(ko ? '| 보상 | 확률 |' : '| Reward | Rate |');
    out.push('|---|---|');
    for (const b of daily.bonus) out.push(`| ${list(b.rewards)} | ${pct((b.weight * 100) / total)} |`);
    const mb = daily.matBox;
    const mTotal = mb.bands.reduce((a, b) => a + b.weight, 0) || 1;
    out.push(`### ${itemName(mb.item)}`);
    out.push(
      ko
        ? `열면 ${itemName(mb.reward)}이 나옵니다. 수량 구간을 아래 확률로 고르고, 구간 안에서는 모든 수량이 같은 확률입니다(예: 1~5개 구간이면 1·2·3·4·5개가 각각 구간 확률의 1/5).`
        : `Opens into ${itemName(mb.reward)}. A quantity range is picked by the rates below; every amount inside the range is equally likely (e.g. in the 1-5 range, each of 1-5 is 1/5 of the range rate).`,
    );
    out.push(ko ? '| 수량 | 구간 확률 | 수량 1개당 확률 |' : '| Amount | Range rate | Rate per amount |');
    out.push('|---|---|---|');
    for (const b of mb.bands) {
      const r = (b.weight * 100) / mTotal;
      out.push(`| ${b.min === b.max ? b.min : `${b.min}~${b.max}`} | ${pct(r)} | ${pct(r / (b.max - b.min + 1))} |`);
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
  const daily = readJson<{ chests?: { ratesVersion: string } }>(dataDir, 'daily_quests.json')?.chests;
  if (daily) versions.daily = daily.ratesVersion;
  return { fingerprint: createHash('sha256').update(content, 'utf8').digest('hex'), versions, content };
}
