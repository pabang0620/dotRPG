#!/usr/bin/env python3
"""English support tooling (Docs/PLAN_I18N.md).

  python3 Tools/i18n/i18n.py check [lang]           # progress report (default: every language folder), exit 1 while anything is left
  python3 Tools/i18n/i18n.py extract <lang> [out]    # untranslated Korean -> JSON (default Temp/i18n/missing_<lang>.json)
  python3 Tools/i18n/i18n.py unwrapped [dir]         # list Korean literals not yet wrapped in L.T / L.F / L.N
  python3 Tools/i18n/i18n.py sync-server             # write server/data/lang.<lang>.json for every language

Keys are the Korean source sentences. Sources: every Korean string literal in Assets/Scripts (except DevCapture*,
Editor, logs, [Tooltip], lines ending in // i18n-ignore) and the display fields of Resources/Data/*.json.
Interpolated $"..." strings with Korean are not keys: they must first become L.F("...{0}...", x).
Server: Korean literals in server/src (not admin, db, ops) and display fields of server/data/*.json are keys too.
Their English goes to server.json (client shows mail titles, daily quests, names from the server) and is copied to
server/data/lang.<lang>.json, which the server uses to answer X-Lang requests. Template holes ${x} become {0}, {1}.
"""
import json
import os
import re
import sys
from collections import defaultdict

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SCRIPTS = os.path.join(ROOT, "Assets", "Scripts")
DATA = os.path.join(ROOT, "Assets", "Resources", "Data")
LANG_ROOT = os.path.join(DATA, "Lang")
GLOSSARY = os.path.join(ROOT, "Docs", "I18N_GLOSSARY.md")
SERVER = os.path.join(ROOT, "server")
SERVER_SRC = os.path.join(SERVER, "src")
SERVER_SKIP = ("admin", "db", "ops")
SERVER_DATA_FIELDS = {"name", "description", "synergy", "label", "giver", "title", "text"}
SERVER_DATA_FILES = ["careers.json", "chat.json", "daily_quests.json", "dungeons.json", "items.json",
                     "monsters.json", "star_products.json"]
SERVER_DATA = os.path.join(SERVER, "data")

HANGUL = re.compile(r"[가-힣]")
DATA_FIELDS = {"text", "speaker", "title", "summary", "prompt"}
DATA_FILES = ["StoryDialogues.json", "Quests.json", "Cutscenes.json", "Dialogues.json", "StoryWorld.json", "chapter3.json"]
WRAPPED = re.compile(r"\bL\.(T|F|N)\(\s*$")
IGNORED_LINE = re.compile(r"Debug\.Log|\[Tooltip\(|\[Header\(")
RAW_OK = "// i18n-ignore"  # kept raw on purpose (translated where shown): still a table key, not counted as unwrapped


# ---------------- C# string literals ----------------

def lex_strings(src):
    """Yield (start, end, kind, value) for each string literal. kind: 'plain', 'verbatim', 'interp'."""
    i, n = 0, len(src)
    while i < n:
        c = src[i]
        if src.startswith("//", i):
            j = src.find("\n", i)
            i = n if j < 0 else j
            continue
        if src.startswith("/*", i):
            j = src.find("*/", i + 2)
            i = n if j < 0 else j + 2
            continue
        if c == "'":
            j = i + 1
            while j < n and src[j] != "'":
                j += 2 if src[j] == "\\" else 1
            i = j + 1
            continue
        m = re.match(r'(\$@|@\$|\$|@)?"', src[i:i + 3])
        if m and (c == '"' or c in "$@"):
            prefix = m.group(1) or ""
            end, value = read_string(src, i + len(prefix) + 1, "@" in prefix, "$" in prefix)
            kind = "interp" if "$" in prefix else ("verbatim" if "@" in prefix else "plain")
            yield i, end, kind, value
            i = end
            continue
        i += 1


def read_string(src, i, verbatim, interp):
    """Read from just after the opening quote; return (index after closing quote, decoded text)."""
    out, n = [], len(src)
    while i < n:
        c = src[i]
        if interp and c == "{":
            if src.startswith("{{", i):
                out.append("{{"); i += 2; continue
            depth, j = 1, i + 1
            while j < n and depth:
                if src[j] == '"' or src.startswith('$"', j) or src.startswith('@"', j):
                    k = j + (1 if src[j] == '"' else 2)
                    j, _ = read_string(src, k, src[j] == "@", src[j] == "$")
                    continue
                depth += {"{": 1, "}": -1}.get(src[j], 0)
                j += 1
            out.append(src[i:j]); i = j; continue
        if verbatim:
            if c == '"':
                if src.startswith('""', i):
                    out.append('"'); i += 2; continue
                return i + 1, "".join(out)
            out.append(c); i += 1; continue
        if c == "\\" and i + 1 < n:
            e = src[i + 1]
            if e == "u":
                out.append(chr(int(src[i + 2:i + 6], 16))); i += 6; continue
            out.append({"n": "\n", "t": "\t", "r": "\r", "0": "\0"}.get(e, e)); i += 2; continue
        if c == '"':
            return i + 1, "".join(out)
        out.append(c); i += 1
    return n, "".join(out)


def cs_files():
    for d, _, files in os.walk(SCRIPTS):
        if os.sep + "Editor" in d:
            continue
        for f in files:
            if f.endswith(".cs") and not f.startswith("DevCapture"):
                yield os.path.join(d, f)


def cs_literals():
    """Yield (path, line, kind, value, wrapped, in_names) for Korean literals in scope."""
    for path in cs_files():
        src = open(path, encoding="utf-8-sig").read()
        lines = src.split("\n")
        names_lines = set()
        inside = False
        for no, text in enumerate(lines, 1):
            if "i18n-names-begin" in text: inside = True
            if inside: names_lines.add(no)
            if "i18n-names-end" in text: inside = False
        for start, _end, kind, value in lex_strings(src):
            if not HANGUL.search(value):
                continue
            line = src.count("\n", 0, start) + 1
            if IGNORED_LINE.search(lines[line - 1]):
                continue
            wrapped = bool(WRAPPED.search(src[max(0, start - 40):start])) or RAW_OK in lines[line - 1]
            yield path, line, kind, value, wrapped, line in names_lines


# ---------------- data ----------------

def data_strings():
    for f in DATA_FILES:
        p = os.path.join(DATA, f)
        if not os.path.exists(p):
            continue
        def walk(o, key=None):
            if isinstance(o, dict):
                # cutscene "choice": the answers are the "id" field, one key each
                if o.get("op") == "choice" and isinstance(o.get("id"), str):
                    for a in o["id"].split("|"):
                        if HANGUL.search(a): yield a
                for k, v in o.items(): yield from walk(v, k)
            elif isinstance(o, list):
                for v in o: yield from walk(v, key)
            elif isinstance(o, str) and key in DATA_FIELDS and HANGUL.search(o):
                yield o
        for s in walk(json.load(open(p, encoding="utf-8"))):
            yield f, s


# ---------------- server ----------------

TS_TOKEN = re.compile(r"//[^\n]*|/\*.*?\*/|'(?:\\.|[^'\\\n])*'|\"(?:\\.|[^\"\\\n])*\"|`(?:\\.|\$\{[^}]*\}|[^`\\])*`", re.S)


def ts_unescape(body):
    return re.sub(r"\\(.)", lambda m: {"n": "\n", "t": "\t"}.get(m.group(1), m.group(1)), body)


def server_strings():
    """Yield (label, key) for Korean server literals; template holes become {0}, {1}."""
    for d, dirs, files in os.walk(SERVER_SRC):
        rel = os.path.relpath(d, SERVER_SRC)
        if rel.split(os.sep)[0] in SERVER_SKIP:
            continue
        for f in files:
            if not f.endswith(".ts"):
                continue
            src = open(os.path.join(d, f), encoding="utf-8").read()
            for m in TS_TOKEN.finditer(src):
                tok = m.group(0)
                if tok.startswith("/") or not HANGUL.search(tok):
                    continue
                body = tok[1:-1]
                # Messages shown to players are single-line; a multi-line "template" is a backtick inside code or a regex
                # that the simple tokenizer mistook for a string (seen in policyRenderer.ts).
                if tok[0] == "`" and "\n" in body:
                    continue
                if tok[0] == "`":
                    n = iter(range(100))
                    body = re.sub(r"\$\{[^}]*\}", lambda _m: "{%d}" % next(n), body)
                line = src[:m.start()].split("\n")[-1] + src[m.start():].split("\n")[0]
                if "logger." in line or "console." in line:
                    continue
                yield "server/" + os.path.relpath(os.path.join(d, f), SERVER), ts_unescape(body)
    for f in SERVER_DATA_FILES:
        p = os.path.join(SERVER, "data", f)
        if not os.path.exists(p):
            continue
        def walk(o, key=None):
            if isinstance(o, dict):
                # cutscene "choice": the answers are the "id" field, one key each
                if o.get("op") == "choice" and isinstance(o.get("id"), str):
                    for a in o["id"].split("|"):
                        if HANGUL.search(a): yield a
                for k, v in o.items(): yield from walk(v, k)
            elif isinstance(o, list):
                for v in o: yield from walk(v, key)
            elif isinstance(o, str) and key in SERVER_DATA_FIELDS and HANGUL.search(o):
                yield o
        for s in walk(json.load(open(p, encoding="utf-8"))):
            yield "server/data/" + f, s


def languages():
    return sorted(d for d in os.listdir(LANG_ROOT) if os.path.isdir(os.path.join(LANG_ROOT, d))) if os.path.isdir(LANG_ROOT) else []


def load_table(lang):
    table, folder = {}, os.path.join(LANG_ROOT, lang)
    if os.path.isdir(folder):
        for f in sorted(os.listdir(folder)):
            if f.endswith(".json"):
                table.update(json.load(open(os.path.join(folder, f), encoding="utf-8")))
    return table


def wanted_keys():
    """Korean key -> set of source labels."""
    keys = defaultdict(set)
    for path, _line, kind, value, _w, _n in cs_literals():
        if kind != "interp":
            keys[value].add(os.path.relpath(path, SCRIPTS))
    for f, s in data_strings():
        keys[s].add("Data/" + f)
    for label, s in server_strings():
        keys[s].add(label)
    return keys


def cmd_sync_server():
    keys = sorted({k for _l, k in server_strings()})
    for lang in languages():
        table = load_table(lang)
        out = {k: table[k] for k in keys if table.get(k)}
        path = os.path.join(SERVER_DATA, f"lang.{lang}.json")
        json.dump(out, open(path, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
        print(f"{lang}: {len(out)} server messages -> {path}")


# ---------------- checks ----------------

PLACEHOLDER = re.compile(r"\{(?:\d+(?:[,:][^}]*)?|[A-Za-z_\uac00-\ud7a3]+)\}")  # {0}, {0:N0}, {name}, {초}
TAG = re.compile(r"</?[a-z]+(?:=[^>]*)?>")


def bad_shape(ko, en):
    if sorted(PLACEHOLDER.findall(ko)) != sorted(PLACEHOLDER.findall(en)):
        return "placeholders"
    if sorted(TAG.findall(ko)) != sorted(TAG.findall(en)):
        return "tags"
    return None


def glossary():
    terms = []
    if os.path.exists(GLOSSARY):
        for row in open(GLOSSARY, encoding="utf-8"):
            cells = [c.strip() for c in row.strip().strip("|").split("|")]
            if len(cells) >= 2 and HANGUL.search(cells[0]) and len(cells[0]) >= 2 and cells[1]:
                terms.append((cells[0], cells[1]))
    return terms


def cmd_check(only=None):
    unwrapped = interp = 0
    for _p, _l, kind, _v, wrapped, in_names in cs_literals():
        if kind == "interp" and not wrapped: interp += 1
        elif not wrapped and not in_names: unwrapped += 1
    keys = wanted_keys()
    print(f"Korean $\"...\" interpolations left: {interp}")
    print(f"Korean literals not wrapped in L.T/L.F/L.N: {unwrapped}")
    failed = bool(interp or unwrapped)
    for lang in ([only] if only else languages()):
        table = load_table(lang)
        missing = [k for k in keys if not table.get(k)]
        shape = [(k, why) for k, v in table.items() if v and (why := bad_shape(k, v))]
        print(f"[{lang}] untranslated {len(missing)} / {len(keys)}, placeholder or tag mismatches {len(shape)}")
        for k, why in shape[:10]: print(f"  [{why}] {k}")
        if lang == "en":  # the glossary is English
            terms = glossary()
            off = [(k, ko, en) for k, v in table.items() if v for ko, en in terms if ko in k and en.lower() not in v.lower()]
            print(f"[en] glossary misses (hints, often substring noise): {len(off)}")
        failed = failed or bool(missing or shape)
    return 1 if failed else 0


def cmd_extract(lang, out):
    keys = wanted_keys()
    table = load_table(lang)
    missing = {k: "" for k in sorted(keys, key=lambda k: (sorted(keys[k])[0], k)) if not table.get(k)}
    groups = defaultdict(dict)
    for k in missing:
        groups[sorted(keys[k])[0]][k] = ""
    os.makedirs(os.path.dirname(out), exist_ok=True)
    json.dump(groups, open(out, "w", encoding="utf-8"), ensure_ascii=False, indent=1)
    print(f"{len(missing)} untranslated keys in {len(groups)} sources -> {out}")


def cmd_unwrapped(sub):
    base = os.path.join(SCRIPTS, sub) if sub else SCRIPTS
    counts = defaultdict(int)
    for path, line, kind, value, wrapped, in_names in cs_literals():
        if not path.startswith(base) or wrapped or in_names:
            continue
        counts[os.path.relpath(path, SCRIPTS)] += 1
        print(f"{os.path.relpath(path, ROOT)}:{line} [{kind}] {value[:80]}")
    print(f"-- {sum(counts.values())} in {len(counts)} files")


if __name__ == "__main__":
    cmd = sys.argv[1] if len(sys.argv) > 1 else "check"
    if cmd == "check":
        sys.exit(cmd_check(sys.argv[2] if len(sys.argv) > 2 else None))
    if cmd == "extract":
        lang = sys.argv[2] if len(sys.argv) > 2 else "en"
        cmd_extract(lang, sys.argv[3] if len(sys.argv) > 3 else os.path.join(ROOT, "Temp", "i18n", f"missing_{lang}.json"))
    elif cmd == "sync-server":
        cmd_sync_server()
    elif cmd == "unwrapped":
        cmd_unwrapped(sys.argv[2] if len(sys.argv) > 2 else "")
    else:
        print(__doc__)
