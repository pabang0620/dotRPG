// 정책 문서와 동의가 필요한 버전. 문서 내용이 바뀌면(이용자에게 불리하거나 중요한 변경) 버전을 올려 다시 동의를 받는다.

export type ConsentDocId = 'terms' | 'privacy' | 'age14';

export interface RequiredConsent {
  id: ConsentDocId;
  version: string;
}

/** 게임을 이용하려면 모두 동의해야 한다. age14는 "만 14세 이상입니다" 확인 */
export const REQUIRED: readonly RequiredConsent[] = [
  { id: 'terms', version: '2026-10-14' },
  { id: 'privacy', version: '2026-10-14' },
  { id: 'age14', version: '1' },
];

/** 공개 정책 문서(GET /policies/:lang/:doc). probability는 서버 확률표로 만든다 */
export const DOCS = ['terms', 'privacy', 'operation', 'paid', 'youth', 'account-deletion', 'probability'] as const;
export type PolicyDoc = (typeof DOCS)[number];

export const LANGUAGES = ['ko', 'en'] as const;
export type PolicyLang = (typeof LANGUAGES)[number];

export const isPolicyDoc = (d: string): d is PolicyDoc => (DOCS as readonly string[]).includes(d);

/** 목록에 없는 언어: 한국어(ko*)면 ko, 그 밖은 en */
export function resolveLang(lang: string): PolicyLang {
  if ((LANGUAGES as readonly string[]).includes(lang)) return lang as PolicyLang;
  return lang.toLowerCase().startsWith('ko') ? 'ko' : 'en';
}
