// 캐릭터 이름 예약어·금칙어(10절). 운영자가 server/data/reserved_names.json과 banned_words.json을 수정하면 서버가 다시 읽는다.
// 기존 캐릭터는 소급 개명하지 않는다(정합성 점검 I-names가 수만 센다).
import { getConfig } from '../../config/env';
import { getReservedNames } from '../../gamedata/antiAbuseData';
import { AppError } from '../../utils/AppError';
import { containsBannedWord, hasObfuscatedBannedWord } from '../../utils/bannedWords';

/** 혼동 문자 접기: 0->o, 1->i, l->i, |->i, 3->e, 4->a, 5->s, 7->t, 8->b, $->s, @->a */
const FOLD: Record<string, string> = { '0': 'o', '1': 'i', l: 'i', '|': 'i', '3': 'e', '4': 'a', '5': 's', '7': 't', '8': 'b', $: 's', '@': 'a' };

/** NFKC -> 소문자 -> 혼동 문자 접기 -> 같은 문자 연속 3개 이상은 2개로 축약 */
export function skeletonOf(name: string): string {
  const folded = [...name.normalize('NFKC').toLowerCase()].map((ch) => FOLD[ch] ?? ch).join('');
  return folded.replace(/(.)\1{2,}/gu, '$1$1');
}

/** 숫자를 모두 지운 사본(접기 전): "gm01", "admin7" 같은 번호 붙이기를 잡는다 */
const digitless = (name: string): string => name.normalize('NFKC').toLowerCase().replace(/\p{N}/gu, '').replace(/(.)\1{2,}/gu, '$1$1');

/** 예약어에 걸리는가('RESERVED'), 욕설 금칙어에 걸리는가('BANNED'), 통과면 null. allow 목록의 정확한 이름은 예약어 판정을 통과한다 */
export function forbiddenReason(name: string): 'RESERVED' | 'BANNED' | null {
  const list = getReservedNames();
  const lower = name.normalize('NFKC').toLowerCase();
  if (!list.allow.some((a) => a.normalize('NFKC').toLowerCase() === lower)) {
    const candidates = [skeletonOf(name), digitless(name)];
    for (const c of candidates) {
      if (list.substring.some((w) => w.length > 0 && c.includes(w.normalize('NFKC').toLowerCase()))) return 'RESERVED';
      // 짧은 영문 토큰은 이름이 같거나 토큰으로 시작/끝날 때만(Pigmy 같은 중간 포함은 통과)
      if (list.token.some((w) => w.length > 0 && (c === w || c.startsWith(w) || c.endsWith(w)))) return 'RESERVED';
    }
  }
  const stripped = digitless(name);
  if (containsBannedWord(name) || containsBannedWord(stripped) || containsBannedWord(skeletonOf(name)) || hasObfuscatedBannedWord(name)) return 'BANNED';
  return null;
}

/** createCharacter가 부른다. NAME_RESERVED_MODE=off면 건너뛴다 */
export function checkReservedName(name: string): void {
  if (getConfig().aa.nameMode === 'off') return;
  const reason = forbiddenReason(name);
  if (reason) throw new AppError(422, '사용할 수 없는 이름입니다.', 'NAME_FORBIDDEN', { reason });
}
