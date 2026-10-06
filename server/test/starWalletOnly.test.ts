// 구조 테스트(설계 4절, 18절 B-7): INSERT INTO star_ledger / UPDATE star_wallets 는 starshop/starWallet.ts 한 파일에만 있어야 한다.
// 새 파일이 별조각 지급·소비 SQL을 쓰면 이 테스트가 깨진다. 테스트 디렉터리는 대상이 아니다(DB 방어선 시험이 직접 SQL을 쓴다).
import fs from 'node:fs';
import path from 'node:path';

const ROOT = path.resolve(__dirname, '..');

function walk(dir: string, out: string[] = []): string[] {
  for (const e of fs.readdirSync(dir, { withFileTypes: true })) {
    if (e.name === 'node_modules' || e.name === 'dist') continue;
    const p = path.join(dir, e.name);
    if (e.isDirectory()) walk(p, out);
    else if (/\.(ts|mjs|js)$/.test(e.name)) out.push(p);
  }
  return out;
}

const files = [...walk(path.join(ROOT, 'src')), ...walk(path.join(ROOT, 'scripts'))];
const holders = (re: RegExp): string[] =>
  files.filter((f) => re.test(fs.readFileSync(f, 'utf8').replace(/\s+/g, ' '))).map((f) => path.relative(ROOT, f).replace(/\\/g, '/'));

describe('별조각 지급·소비 SQL 단일화', () => {
  it('INSERT INTO star_ledger 는 starWallet.ts 한 파일에만 있다', () => {
    expect(holders(/INSERT INTO star_ledger/i)).toEqual(['src/domains/starshop/starWallet.ts']);
  });

  it('UPDATE star_wallets 는 starWallet.ts 한 파일에만 있다', () => {
    expect(holders(/UPDATE star_wallets/i)).toEqual(['src/domains/starshop/starWallet.ts']);
  });

  it('범용 changeBalance 는 없다(임의 사유·임의 부호로 잔액을 바꾸는 함수 삭제)', () => {
    expect(holders(/\bchangeBalance\b/)).toEqual([]);
  });

  it('원장·로트·배분 표를 쓰는 SQL(INSERT/UPDATE/DELETE)은 결제 모듈과 starWallet 밖에 없다', () => {
    const allowed = new Set([
      'src/domains/starshop/starWallet.ts',
    ]);
    const bad = holders(/(INSERT INTO|UPDATE|DELETE FROM) (star_paid_lots|star_spend_allocs)\b/i).filter((f) => !allowed.has(f));
    expect(bad).toEqual([]);
  });
});
