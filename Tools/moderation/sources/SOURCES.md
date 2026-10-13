# 금칙어 원본 목록 출처

| 파일 | 출처 | 라이선스 | 받은 날 |
|---|---|---|---|
| ldnoobw_{en,ko,ja,zh,es,pt,de,fr,ru}.txt | https://github.com/LDNOOBW/List-of-Dirty-Naughty-Obscene-and-Otherwise-Bad-Words | CC BY 4.0 | 2026-10-13 |
| badwordsko_ko.txt | https://github.com/yoonheyjung/badwords-ko (src/badwords.ko.config.json) | MIT | 2026-10-14 |
| vnoffensive_vi.txt | https://github.com/blue-eyes-vn/vietnamese-offensive-words (vn_offensive_words.txt) | MIT | 2026-10-14 |
| indobadwords_id.txt | https://github.com/drizki/indonesian-badwords (src/dict.json) | MIT (package.json, LICENSE 파일 없음) | 2026-10-14 |

쓰지 않은 것: 인도네시아어 abusive.csv(okkyibrohim)는 CC BY-NC-SA(비상업 조건)라 제외했다.

## 검증 도구
- `node Tools/moderation/corpus_check.js`: 게임 번역문 전체(11개 언어, 약 5만 3천 줄) 오탐 실측. 0이어야 한다.
- `node Tools/moderation/detect_check.js`: 반드시 걸러야 하는 표현(`must_catch.json`, 우회형 포함) 탐지 실측. 놓침 0이어야 한다.
- `cd server && node ../Tools/moderation/names_check.js`: 게임 속 단어를 이름으로 넣었을 때의 거절과 필수 거절 이름.
- 같은 기준의 정식 테스트: `server/test/bannedWordsCorpus.test.ts`.
- 판단 근거(빈도 상위 2만 단어 대조)는 FrequencyWords(MIT, 검사용으로만 사용, 저장소에 넣지 않음).
