// Google Flow image generation in this session's own browser (a copy of the logged-in profile, never the shared MCP one).
// Usage: FLOW_PROFILE=<profile copy> node Tools/art/flow_image_gen.mjs <jobs.json> <out dir> [first] [count]
// jobs.json = [{ "name": "gear_sword_1_10", "prompt": "...", "ref": "/abs/path/ref.png" }]
//   optional per job: "refs": [several reference paths, attached in order], "aspect": "1:1" | "4:3" | "16:9" ...,
//   "out": exact output path (default <out dir>/gen_<name>.jpg)
// Env: FLOW_PROJECT (project URL), FLOW_MODEL (model label, e.g. "Nano Banana Pro"; default: leave the model as it is),
//   FLOW_HEADLESS=1 (no window), CHROME_PATH, GAP_SEC (default 20), SHOTS (failure screenshots dir)
// Each job: clear the prompt box, attach the references (uploaded once, then picked by name), type the prompt, generate,
// wait for the new tile and save it. Jobs whose file exists are skipped. A "image could not be generated" tile is retried
// (up to 3 tries per job). On the unusual-activity / usage-limit message the run stops with exit code 2.
// Pace: one job at a time with a gap (GAP_SEC) so Flow does not flag rapid submissions.
import { chromium } from '/home/lee/project/nyang-bakja/node_modules/@playwright/test/index.mjs';
import { readFileSync, existsSync, writeFileSync, mkdirSync } from 'node:fs';
import path from 'node:path';

const PROFILE = process.env.FLOW_PROFILE;
if (!PROFILE) throw new Error('FLOW_PROFILE(로그인된 프로필 복사본 경로)을 지정하세요');
const PROJECT = process.env.FLOW_PROJECT ?? 'https://flow.google.com/project/148fc0d6-31d9-4538-bf3d-1a266fd94249';
const GAP = Number(process.env.GAP_SEC ?? 20) * 1000;
const SHOTS = process.env.SHOTS ?? '/tmp';
const MODEL = process.env.FLOW_MODEL ?? '';
const HEADLESS = process.env.FLOW_HEADLESS === '1';
const [, , jobsPath, outDir, firstArg, countArg] = process.argv;
const all = JSON.parse(readFileSync(jobsPath, 'utf8'));
const first = Number(firstArg ?? 0);
const jobs = all.slice(first, countArg ? first + Number(countArg) : undefined);
const log = (...a) => console.log(new Date().toISOString().slice(11, 19), ...a);
const BLOCK = /비정상적인 활동|사용량 한도|unusual activity|usage limit/g;
const FAIL = /정책을 위반|이미지를 생성할 수 없습니다/g;
const count = (s, re) => (s.match(re) || []).length;

const ctx = await chromium.launchPersistentContext(PROFILE, {
  headless: HEADLESS,
  ...(HEADLESS ? { userAgent: 'Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/151.0.0.0 Safari/537.36' } : {}),
  executablePath: process.env.CHROME_PATH ?? '/home/lee/.cache/ms-playwright/chromium-1232/chrome-linux64/chrome',
  viewport: { width: 1500, height: 950 },
  // Flow checks reCAPTCHA: do not announce an automated browser
  ignoreDefaultArgs: ['--enable-automation'],
  args: ['--disable-blink-features=AutomationControlled', '--disable-dev-shm-usage'],
});
await ctx.addInitScript(() => { Object.defineProperty(navigator, 'webdriver', { get: () => undefined }); });
const page = ctx.pages()[0] ?? (await ctx.newPage());
const shot = (n) => page.screenshot({ path: `${SHOTS}/flowgen-${n}.png` }).catch(() => {});
const uploaded = new Set();
const bodyText = () => page.locator('body').innerText().catch(() => '');

async function tiles() {
  return page.evaluate(() => [...document.querySelectorAll('img')]
    .filter((i) => i.alt === '사용자 이미지를 표시하는 타일')
    .map((i) => ({ src: i.src, key: i.src.split('?')[0], w: i.naturalWidth, label: i.closest('[role=group]')?.getAttribute('aria-label') ?? '' })));
}

let settingsKey = null;
async function settings(aspect) {
  const key = `${aspect ?? ''}|${MODEL}`;
  if (key === settingsKey) return;
  await page.getByRole('button', { name: '설정 트리거' }).click();
  await page.waitForTimeout(1200);
  if (aspect) await page.getByRole('radio', { name: aspect, exact: true }).click().catch(() => log('비율 선택 실패', aspect));
  // One image per generation (a profile copy may keep x2; two at once trips the limit)
  await page.getByRole('radio', { name: 'x1', exact: true }).click()
    .catch(() => page.getByRole('button', { name: 'x1', exact: true }).click())
    .catch(() => page.getByText('x1', { exact: true }).click()).catch(() => {});
  await page.waitForTimeout(500);
  if (MODEL && !(await page.getByText(MODEL).first().isVisible().catch(() => false))) {
    await page.getByText('arrow_drop_down').first().click();
    await page.waitForTimeout(1000);
    await page.getByText(MODEL).last().click();
    await page.waitForTimeout(800);
  }
  await page.keyboard.press('Escape');
  await page.waitForTimeout(500);
  settingsKey = key;
}

async function attachRef(ref) {
  const name = path.basename(ref, path.extname(ref));
  await page.getByRole('button', { name: '프롬프트 상자에 소재 추가' }).click();
  await page.waitForTimeout(1500);
  const search = page.locator('input[placeholder*="애셋 검색"], input[aria-label*="애셋 검색"]').first();
  if (!uploaded.has(ref)) {
    await search.fill(name).catch(() => {});
    await page.waitForTimeout(2000);
    const found = await page.locator('[role=option]', { hasText: name }).count();
    if (found === 0) {
      const [chooser] = await Promise.all([
        page.waitForEvent('filechooser', { timeout: 15000 }),
        page.getByRole('button', { name: '미디어 업로드' }).click().catch(() => page.getByText('업로드', { exact: false }).last().click()),
      ]);
      await chooser.setFiles(ref);
      log('uploaded', name);
      await page.waitForTimeout(8000);
    }
    uploaded.add(ref);
  }
  if (await search.count()) {
    await search.fill(name);
    await page.waitForTimeout(2500);
    const opt = page.locator('[role=option]', { hasText: name }).first();
    if (await opt.count()) await opt.click();
    await page.waitForTimeout(500);
    const add = page.getByRole('button', { name: /프롬프트에 추가/ });
    if (await add.count()) await add.first().click();
  }
  await page.waitForTimeout(1000);
  await page.keyboard.press('Escape').catch(() => {});
  await page.waitForTimeout(400);
}

async function save(src, out) {
  const b64 = await page.evaluate(async (s) => {
    const r = await fetch(s);
    const u = new Uint8Array(await r.arrayBuffer());
    let bin = '';
    for (let i = 0; i < u.length; i += 32768) bin += String.fromCharCode.apply(null, u.subarray(i, i + 32768));
    return btoa(bin);
  }, src);
  mkdirSync(path.dirname(out), { recursive: true });
  writeFileSync(out, Buffer.from(b64, 'base64'));
}

await page.goto(PROJECT, { waitUntil: 'domcontentloaded' });
await page.waitForTimeout(8000);
if (!page.url().includes('/project/')) {
  await shot('login');
  throw new Error('Flow 프로젝트가 열리지 않았습니다(로그인 확인): ' + page.url());
}

let n = 0;
let lastSubmit = 0;
for (const job of jobs) {
  const out = job.out ?? `${outDir}/gen_${job.name}.jpg`;
  if (existsSync(out)) { log('skip(있음)', job.name); continue; }
  const refs = job.refs ?? (job.ref ? [job.ref] : []);
  for (let tries = 1; tries <= 3; tries++) {
    const gapLeft = GAP - (Date.now() - lastSubmit);
    if (gapLeft > 0) await page.waitForTimeout(gapLeft);
    log('==', job.name, tries > 1 ? `(${tries})` : '');
    try {
      const clear = page.getByRole('button', { name: '프롬프트 지우기' });
      if (await clear.count()) { await clear.first().click(); await page.waitForTimeout(800); }
      await settings(job.aspect);
      for (const r of refs) await attachRef(r);
      const box = page.locator('[contenteditable=true]').first();
      await box.click();
      await page.keyboard.insertText(job.prompt);
      await page.waitForTimeout(4000); // a just-uploaded reference may appear late in the tile list
      const before = new Set((await tiles()).map((t) => t.key));
      const b0 = await bodyText();
      const blockBefore = count(b0, BLOCK);
      const failBefore = count(b0, FAIL);
      await page.getByRole('button', { name: '생성 시작' }).click();
      lastSubmit = Date.now();
      let got = null;
      for (let k = 0; k < 75 && !got; k++) {
        await page.waitForTimeout(4000);
        const body = await bodyText();
        if (count(body, BLOCK) > blockBefore) { log('차단 문구 감지. 중단', job.name); await shot('block'); await ctx.close(); process.exit(2); }
        if (count(body, FAIL) > failBefore) throw new Error('생성 실패 타일');
        got = (await tiles()).find((t) => !before.has(t.key) && t.w >= 800 && !/\.(png|jpe?g)$/i.test(t.label));
      }
      if (!got) throw new Error('시간 초과');
      await save(got.src, out);
      log('저장', out);
      break;
    } catch (e) {
      await shot(job.name);
      log('실패', job.name, String(e.message).split('\n')[0].slice(0, 120));
      await page.keyboard.press('Escape').catch(() => {});
      if (/locator\.click|시간 초과/.test(String(e.message))) {
        await page.goto(PROJECT, { waitUntil: 'domcontentloaded' }).catch(() => {});
        await page.waitForTimeout(8000);
        settingsKey = null;
        // A profile copy can be signed out when the original profile rotates its session: stop with exit code 3
        // so the caller can refresh the copy and run again (finished files are skipped).
        if (!page.url().includes('/project/')) { log('로그아웃됨. 중단', page.url()); await ctx.close(); process.exit(3); }
      }
    }
  }
  n++;
}
await ctx.close();
log('끝');
