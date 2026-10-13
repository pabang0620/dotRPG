// 금칙어 우회 잡기용 한글 조합: 두벌식 자판으로 친 영문("tlqkf" -> "시발")과 풀어 쓴 자모("ㅅㅣㅂㅏㄹ" -> "시발")를 음절로 묶는다.

const KEY: Record<string, string> = {
  q: 'ㅂ', w: 'ㅈ', e: 'ㄷ', r: 'ㄱ', t: 'ㅅ', y: 'ㅛ', u: 'ㅕ', i: 'ㅑ', o: 'ㅐ', p: 'ㅔ',
  a: 'ㅁ', s: 'ㄴ', d: 'ㅇ', f: 'ㄹ', g: 'ㅎ', h: 'ㅗ', j: 'ㅓ', k: 'ㅏ', l: 'ㅣ',
  z: 'ㅋ', x: 'ㅌ', c: 'ㅊ', v: 'ㅍ', b: 'ㅠ', n: 'ㅜ', m: 'ㅡ',
  Q: 'ㅃ', W: 'ㅉ', E: 'ㄸ', R: 'ㄲ', T: 'ㅆ', O: 'ㅒ', P: 'ㅖ',
};

const INITIALS = 'ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ';
const VOWELS = 'ㅏㅐㅑㅒㅓㅔㅕㅖㅗㅘㅙㅚㅛㅜㅝㅞㅟㅠㅡㅢㅣ';
const FINALS = ['', 'ㄱ', 'ㄲ', 'ㄳ', 'ㄴ', 'ㄵ', 'ㄶ', 'ㄷ', 'ㄹ', 'ㄺ', 'ㄻ', 'ㄼ', 'ㄽ', 'ㄾ', 'ㄿ', 'ㅀ', 'ㅁ', 'ㅂ', 'ㅄ', 'ㅅ', 'ㅆ', 'ㅇ', 'ㅈ', 'ㅊ', 'ㅋ', 'ㅌ', 'ㅍ', 'ㅎ'];
const VOWEL_PAIRS: Record<string, string> = { ㅗㅏ: 'ㅘ', ㅗㅐ: 'ㅙ', ㅗㅣ: 'ㅚ', ㅜㅓ: 'ㅝ', ㅜㅔ: 'ㅞ', ㅜㅣ: 'ㅟ', ㅡㅣ: 'ㅢ' };
const FINAL_PAIRS: Record<string, string> = {
  ㄱㅅ: 'ㄳ', ㄴㅈ: 'ㄵ', ㄴㅎ: 'ㄶ', ㄹㄱ: 'ㄺ', ㄹㅁ: 'ㄻ', ㄹㅂ: 'ㄼ', ㄹㅅ: 'ㄽ', ㄹㅌ: 'ㄾ', ㄹㅍ: 'ㄿ', ㄹㅎ: 'ㅀ', ㅂㅅ: 'ㅄ',
};

const isVowel = (j: string | undefined): boolean => j !== undefined && VOWELS.includes(j);
const isInitial = (j: string | undefined): boolean => j !== undefined && INITIALS.includes(j);

/** 자모 배열을 음절로 묶는다. 묶이지 않는 자모(초성만 있는 "ㅅㅂ" 등)는 그대로 둔다. */
export function composeJamo(jamo: string[]): string {
  let out = '';
  let i = 0;
  while (i < jamo.length) {
    const c = jamo[i] as string;
    if (!isInitial(c) || !isVowel(jamo[i + 1])) {
      out += c;
      i++;
      continue;
    }
    let v = jamo[i + 1] as string;
    i += 2;
    const pairV = VOWEL_PAIRS[v + (jamo[i] ?? '')];
    if (pairV) {
      v = pairV;
      i++;
    }
    let fin = '';
    const f1 = jamo[i];
    if (f1 !== undefined && FINALS.includes(f1) && !isVowel(jamo[i + 1])) {
      fin = f1;
      i++;
      const pairF = FINAL_PAIRS[fin + (jamo[i] ?? '')];
      if (pairF && !isVowel(jamo[i + 1])) {
        fin = pairF;
        i++;
      }
    }
    out += String.fromCharCode(0xac00 + (INITIALS.indexOf(c) * 21 + VOWELS.indexOf(v)) * 28 + FINALS.indexOf(fin));
  }
  return out;
}

/** 두벌식으로 친 영문을 한글로 바꾼다("tlqkf" -> "시발"). 자판에 없는 글자는 그대로. */
export function fromKoreanKeyboard(latin: string): string {
  return composeJamo([...latin].map((ch) => KEY[ch] ?? KEY[ch.toLowerCase()] ?? ch));
}

/** 글 안의 풀어 쓴 자모 덩어리를 음절로 묶은 사본("ㅅㅣㅂㅏㄹ" -> "시발"). */
export function composeLooseJamo(text: string): string {
  return text.replace(/[ㄱ-ㅣ]{2,}/gu, (run) => composeJamo([...run]));
}
