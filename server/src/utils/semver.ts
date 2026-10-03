const SEMVER = /^(\d+)\.(\d+)\.(\d+)$/;

export function isSemver(v: string): boolean {
  return SEMVER.test(v);
}

/** a < b 이면 음수, 같으면 0, 크면 양수 */
export function compareSemver(a: string, b: string): number {
  const pa = SEMVER.exec(a);
  const pb = SEMVER.exec(b);
  if (!pa || !pb) throw new Error('semver 형식이 아닙니다');
  for (let i = 1; i <= 3; i++) {
    const d = Number(pa[i]) - Number(pb[i]);
    if (d !== 0) return d;
  }
  return 0;
}
