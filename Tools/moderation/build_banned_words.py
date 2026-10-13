#!/usr/bin/env python3
"""Builds server/data/banned_words.json (chat, party messages and character names) from curated and public lists.

  python3 Tools/moderation/build_banned_words.py

Inputs (Tools/moderation/):
  manual.json           hand-written lists: words (substring), words_whole (whole word), names_only (substring, names only)
  sources/*.txt         public lists, one entry per line (see sources/SOURCES.md for origin and licence)
  drop.txt              entries never used (common words that the public lists contain), one per line, # comments

How each public list is used:
  - Korean, Japanese, Chinese (no spaces between words): substring ("words"). One-character entries are dropped
    (a single hanzi such as 性 also sits in 属性 "attribute").
  - Latin and Cyrillic scripts: whole word ("words_whole"), so "ass" never hits "class".
  - Korean entries of two or more syllables also get their Korean-keyboard spelling ("시발" -> "tlqkf", typed with the
    IME off), added as substring when it is at least 5 letters long (shorter ones collide with real words).
  - manual.json "allow": real words that contain a banned entry ("시발점"); the server blanks them before matching.
After building, the server test test/bannedWordsCorpus.test.ts must pass: no line of the game's own text in any
language may be masked, and no in-game short name may be refused.
"""
import json
import os
import re

HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.dirname(os.path.dirname(HERE))
OUT = os.path.join(ROOT, "server", "data", "banned_words.json")
SOURCES = os.path.join(HERE, "sources")

CJK = re.compile("[\u1100-\u11ff\u3040-\u30ff\u3400-\u9fff\uac00-\ud7a3\u3131-\u318e\U00020000-\U0002ffff]")  # conjoining jamo too: NFKC turns ㅁㅊ into U+1106 U+110E
NOTE = ("운영 금칙어(게임 데이터 아님). Tools/moderation/build_banned_words.py가 만든다: 직접 고치지 말고 manual.json·drop.txt를 "
        "고친 뒤 다시 만든다. words는 부분 일치(띄어 쓰지 않는 말과 어디에 붙어도 욕인 말), words_whole은 단어 단위(띄어 쓰는 "
        "언어), names_only는 이름에만 쓰는 부분 일치. 수정하면 서버가 다시 읽는다.")


def read_lines(path):
    if not os.path.exists(path):
        return []
    out = []
    for line in open(path, encoding="utf-8"):
        line = line.split("#", 1)[0].strip() if not line.lstrip().startswith("#") else ""
        if line:
            out.append(line)
    return out


def norm(w):
    import unicodedata
    return unicodedata.normalize("NFKC", w).lower().strip()


INITIALS = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ"
VOWELS = ["ㅏ", "ㅐ", "ㅑ", "ㅒ", "ㅓ", "ㅔ", "ㅕ", "ㅖ", "ㅗ", "ㅗㅏ", "ㅗㅐ", "ㅗㅣ", "ㅛ", "ㅜ", "ㅜㅓ", "ㅜㅔ", "ㅜㅣ", "ㅠ", "ㅡ", "ㅡㅣ", "ㅣ"]
FINALS = ["", "ㄱ", "ㄲ", "ㄱㅅ", "ㄴ", "ㄴㅈ", "ㄴㅎ", "ㄷ", "ㄹ", "ㄹㄱ", "ㄹㅁ", "ㄹㅂ", "ㄹㅅ", "ㄹㅌ", "ㄹㅍ", "ㄹㅎ", "ㅁ", "ㅂ", "ㅂㅅ", "ㅅ",
          "ㅆ", "ㅇ", "ㅈ", "ㅊ", "ㅋ", "ㅌ", "ㅍ", "ㅎ"]
KEYS = dict(zip("ㅂㅈㄷㄱㅅㅛㅕㅑㅐㅔㅁㄴㅇㄹㅎㅗㅓㅏㅣㅋㅌㅊㅍㅠㅜㅡㅃㅉㄸㄲㅆㅒㅖ", "qwertyuiopasdfghjklzxcvbnmQWERTOP"))


def korean_keyboard(word):
    """"시발" -> "tlqkf": the keys pressed on a Korean 2-set keyboard with the IME switched off. None if not all Hangul."""
    keys = []
    for ch in word:
        code = ord(ch) - 0xAC00
        if not 0 <= code < 11172:
            return None
        jamo = INITIALS[code // 588] + VOWELS[(code % 588) // 28] + FINALS[code % 28]
        keys.append("".join(KEYS[j] for j in jamo))
    return "".join(keys)


def main():
    manual = json.load(open(os.path.join(HERE, "manual.json"), encoding="utf-8"))
    drop = {norm(w) for w in read_lines(os.path.join(HERE, "drop.txt"))}
    sub, whole, names_only = [], [], []

    def add(bucket, w, curated=False):
        # drop.txt only filters the public lists: a curated entry is a decision already made.
        n = norm(w)
        if n and (curated or n not in drop) and n not in bucket:
            bucket.append(n)

    for w in manual.get("words", []):
        add(sub, w, True)
    for w in manual.get("words_whole", []):
        add(whole, w, True)
    for w in manual.get("names_only", []):
        add(names_only, w, True)

    for f in sorted(os.listdir(SOURCES)):
        if not f.endswith(".txt"):
            continue
        for w in read_lines(os.path.join(SOURCES, f)):
            n = norm(w)
            if not n or len(n) > 30:
                continue
            if CJK.search(n):
                if len(n) >= 2:
                    add(sub, n)
            elif re.fullmatch(r"[\w' .-]+", n) and len(n) >= 3:
                add(whole, n)

    # Korean-keyboard spellings of Korean entries (case kept: shifted keys are double consonants).
    for w in list(sub):
        typed = korean_keyboard(w) if len(w) >= 2 else None
        if typed and len(typed) >= 5:
            low = typed.lower()
            if low not in drop and low not in sub:
                sub.append(low)

    allow = []
    for w in manual.get("allow", []):
        n = norm(w)
        if n and n not in allow:
            allow.append(n)

    whole = [w for w in whole if w not in sub]
    names_only = [w for w in names_only if w not in sub]
    # strong: the curated substring list only. Spaced-out bypasses ("시 발", "f u c k") are checked against these,
    # never against the large public lists (Korean has many one-syllable words: "새 키 입력" must not read as "새키").
    strong = sorted({norm(w) for w in manual.get("words", [])} & set(sub))
    data = {"note": NOTE, "words": sorted(sub), "words_whole": sorted(whole), "names_only": sorted(names_only), "allow": sorted(allow),
            "strong": strong}
    with open(OUT, "w", encoding="utf-8") as fh:
        fh.write(json.dumps(data, ensure_ascii=False, indent=2) + "\n")
    print(f"words {len(sub)}, words_whole {len(whole)}, names_only {len(names_only)}, allow {len(allow)}, dropped {len(drop)} -> {OUT}")


if __name__ == "__main__":
    main()
