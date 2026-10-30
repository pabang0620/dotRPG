"""Static UI readability audit for dotRPG (no Unity, no window).

Walks Assets/Scripts/Runtime/UI/**/*.cs for WindowScreen.Button / Label / Cell and UIFactory.Text + Place pairs,
estimates the rendered width of their literal (L.T / L.F) strings in all 11 languages and flags:
  btn-shrink   button label must shrink below the 14 px rendered floor (16 layout x TextScale 0.88) to fit
  cell-over    one-line cell / OneLine label wider than its box (runs into the next column)
  label-over   wrapped label needs more lines than its box holds (spills below)
  oneline-over label in a one-line box (UIWordWrap never wraps it) wider than the box
  min-font     best-fit floor below 14 px rendered
  overlap      two sibling rects with the same parent and anchor intersect by more than 4 px
Widths: Hangul / CJK / kana 1.0 x size, other glyphs 0.55 x size, space 0.3 x size. {n} placeholders count as 4 digits.
Usage: python3 Tools/ui_static_audit.py [file-substring ...] > report.txt
"""
import glob
import json
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..")
UI = os.path.join(ROOT, "Assets", "Scripts", "Runtime", "UI")
LANG = os.path.join(ROOT, "Assets", "Resources", "Data", "Lang")
TEXT_SCALE, MIN_RENDER = 0.88, 14

tables = {}
for d in sorted(glob.glob(os.path.join(LANG, "*"))):
    if not os.path.isdir(d):
        continue
    t = {}
    for f in glob.glob(os.path.join(d, "*.json")):
        try:
            t.update(json.load(open(f, encoding="utf-8-sig")))
        except Exception:
            pass
    tables[os.path.basename(d)] = t


def glyph_w(ch):
    o = ord(ch)
    if 0xAC00 <= o <= 0xD7A3 or 0x3040 <= o <= 0x30FF or 0x4E00 <= o <= 0x9FFF or 0x3130 <= o <= 0x318F or 0xFF00 <= o <= 0xFFEF:
        return 1.0
    if ch == " ":
        return 0.3
    return 0.55


def clean(s):
    s = re.sub(r"<[^>]+>", "", s)
    s = re.sub(r"\{\d+(:[^}]*)?\}", "9999", s)
    return s.replace("\\n", "\n")


def width(s, size):
    return max((sum(glyph_w(c) for c in line) for line in clean(s).split("\n")), default=0) * size


def variants(ko):
    out = [("ko", ko)]
    for code, t in tables.items():
        v = t.get(ko)
        if v is None:
            for k, val in t.items():
                if k.endswith("|" + ko):
                    v = val
                    break
        if v:
            out.append((code, v))
    return out


def split_args(s):
    args, depth, cur, q = [], 0, "", None
    i = 0
    while i < len(s):
        c = s[i]
        if q:
            cur += c
            if c == "\\":
                cur += s[i + 1]; i += 2; continue
            if c == q:
                q = None
        elif c in "\"'":
            q = c; cur += c
        elif c in "([{":
            depth += 1; cur += c
        elif c in ")]}":
            depth -= 1; cur += c
        elif c == "," and depth == 0:
            args.append(cur.strip()); cur = ""
        else:
            cur += c
        i += 1
    if cur.strip():
        args.append(cur.strip())
    return args


def call_args(src, start):
    depth, i, q = 0, start, None
    while i < len(src):
        c = src[i]
        if q:
            if c == "\\":
                i += 2; continue
            if c == q:
                q = None
        elif c in "\"'":
            q = c
        elif c == "(":
            depth += 1
        elif c == ")":
            depth -= 1
            if depth == 0:
                return src[start + 1:i], i
        i += 1
    return None, start


def ev(expr, env):
    e = re.sub(r"(\d+(\.\d+)?)f\b", r"\1", expr.strip())
    e = e.replace("Mathf.Max", "max").replace("Mathf.Min", "min")
    try:
        return eval(e, {"__builtins__": {}, "max": max, "min": min}, env)
    except Exception:
        return None


def vec(expr, env):
    m = re.match(r"new Vector2\((.*)\)$", expr.strip(), re.S)
    if m:
        a = split_args(m.group(1))
        if len(a) == 2:
            x, y = ev(a[0], env), ev(a[1], env)
            if isinstance(x, (int, float)) and isinstance(y, (int, float)):
                return (float(x), float(y))
        return None
    if expr.strip() == "Vector2.zero":
        return (0.0, 0.0)
    v = env.get(expr.strip())
    return v if isinstance(v, tuple) else None


def literal_strings(expr):
    """Korean source strings of a label expression: L.T("..") / L.F("..") / plain literal."""
    out = re.findall(r'L\.[TF]\(\s*"((?:[^"\\]|\\.)*)"', expr)
    if not out:
        m = re.match(r'^(\$?)"((?:[^"\\]|\\.)*)"$', expr.strip())
        if m and m.group(2):
            out = [re.sub(r"\{[^}]*\}", "{0}", m.group(2)) if m.group(1) else m.group(2)]
    return out


def longest(strings, size):
    best = (0, "", "")
    for ko in strings:
        for code, s in variants(ko):
            w = width(s, size)
            if w > best[0]:
                best = (w, code, s)
    return best


def render(size):
    return max(MIN_RENDER if size >= 16 else 14, round(size * TEXT_SCALE))


def file_env(src):
    env = {"UIFactory": None}
    for m in re.finditer(r"const (?:float|int) ([^;]+);", src):
        for part in split_args(m.group(1)):
            if "=" in part:
                k, v = part.split("=", 1)
                val = ev(v, env)
                if val is not None:
                    env[k.strip()] = val
    for m in re.finditer(r"var (\w+) = (new Vector2\([^;]*\));", src):
        v = vec(m.group(2), env)
        if v:
            env[m.group(1)] = v
    env.setdefault("FrameRim", 12)
    return env


def field_texts(src):
    """name -> format strings assigned later as `x.name.text = L.F(...)` (dynamic labels)."""
    out = {}
    for m in re.finditer(r"(\w+)\.text\s*=\s*([^;]+);", src):
        s = literal_strings(m.group(2))
        if s:
            out.setdefault(m.group(1), []).extend(s)
    return out


def audit(path):
    src = open(path, encoding="utf-8-sig").read()
    env = file_env(src)
    dyn = field_texts(src)
    issues, rects = [], []

    def line_of(i):
        return src.count("\n", 0, i) + 1

    for m in re.finditer(r"\b(Button|Label|Cell)\(", src):
        kind = m.group(1)
        if src[max(0, m.start() - 7):m.start()].endswith("static "):
            continue
        body, end = call_args(src, m.end() - 1)
        if body is None:
            continue
        a = split_args(body)
        ln = line_of(m.start())
        assign = re.search(r"([\w\.\[\]]+)\s*=\s*$", src[max(0, m.start() - 80):m.start()])
        target = assign.group(1).split(".")[-1] if assign else ""
        tail = src[end:end + 120]
        if kind == "Button" and len(a) >= 10:
            size = vec(a[7], env); font = ev(a[9], env)
            if not size or not isinstance(font, (int, float)):
                continue
            strs = literal_strings(a[2]) or dyn.get(target, [])
            inset = 6 if size[0] < 70 else 10
            aw, ah = size[0] - 2 * inset, size[1] - 6
            f = render(max(16, font))
            w, code, s = longest(strs, f)
            lines = max(1, int(ah // (f * 1.15)))
            if w > aw * (1 if lines == 1 else lines * 0.85):
                need = f * aw * (1 if lines == 1 else lines * 0.85) / w
                if need < MIN_RENDER:
                    issues.append((ln, "btn-shrink", f"{a[1]} {size[0]:.0f}x{size[1]:.0f} needs {need:.1f}px [{code}] {clean(s)[:40]}"))
            anchor, pos = vec(a[4], env), vec(a[6], env)
            pivot = vec(a[5], env)
            if anchor and pos and pivot:
                rects.append((a[0], a[4], anchor, pivot, pos, size, ln, a[1]))
        elif kind == "Label" and len(a) >= 8:
            size = vec(a[7], env); font = ev(a[3], env)
            if not size or not isinstance(font, (int, float)):
                continue
            f = render(font)
            strs = literal_strings(a[2]) or dyn.get(target, [])
            oneline = "OneLine(" in src[max(0, m.start() - 20):m.start()] or re.match(r"\s*;?\s*UIFactory\.OneLine\(" + re.escape(target), tail or "") is not None
            shrink = "shrink: true" in src[end:end + 200] and target and ("OneLine(" + target) in src[end:end + 200].replace("w.", "").replace("v.", "")
            if strs and not shrink:
                w, code, s = longest(strs, f)
                one_box = size[1] < f * 2.2 and "\\n" not in "".join(strs)
                if (oneline or one_box) and w > size[0] + 4:
                    issues.append((ln, "oneline-over", f"{a[1]} box {size[0]:.0f}x{size[1]:.0f} text {w:.0f}px [{code}] {clean(s)[:40]}"))
                elif not one_box:
                    nlines = sum(max(1, -(-int(width(l, f)) // max(1, int(size[0])))) for l in clean(s).split("\n"))
                    if nlines * f * 1.2 * 1.15 > size[1] + f * 0.5:
                        issues.append((ln, "label-over", f"{a[1]} box {size[0]:.0f}x{size[1]:.0f} needs {nlines} lines [{code}] {clean(s)[:40]}"))
            anchor, pos, pivot = vec(a[4], env), vec(a[6], env), vec(a[5], env)
            if anchor and pos and pivot:
                rects.append((a[0], a[4], anchor, pivot, pos, size, ln, a[1]))
        elif kind == "Cell" and len(a) >= 4:
            w_box = ev(a[3], env); font = ev(a[4], env) if len(a) > 4 else 18
            if not isinstance(w_box, (int, float)) or not isinstance(font, (int, float)):
                continue
            strs = dyn.get(target, [])
            if strs:
                f = render(font)
                w, code, s = longest(strs, f)
                if w > w_box + 4:
                    issues.append((ln, "cell-over", f"{a[1]} col {w_box:.0f} text {w:.0f}px [{code}] {clean(s)[:40]}"))

    for m in re.finditer(r"(\w+(?:\.\w+)?)\s*=\s*UIFactory\.Text\(", src):
        body, end = call_args(src, m.end() - 1)
        if body is None:
            continue
        a = split_args(body)
        if len(a) < 4:
            continue
        var = m.group(1)
        font = ev(a[3], env)
        pm = re.search(r"UIFactory\.Place\(" + re.escape(var) + r"\.rectTransform,", src[end:end + 600])
        if not pm or not isinstance(font, (int, float)):
            continue
        pbody, _ = call_args(src, end + pm.end() - 1 - len(var + ".rectTransform,") - 0 + 0 if False else end + pm.start() + len("UIFactory.Place"))
        pa = split_args(pbody or "")
        if len(pa) < 5:
            continue
        size = vec(pa[4], env)
        if not size:
            continue
        f = render(font)
        strs = literal_strings(a[2]) or dyn.get(var.split(".")[-1], [])
        best = "resizeTextForBestFit = true" in src[end:end + 400] and var in src[end:end + 400]
        if strs and not best:
            w, code, st = longest(strs, f)
            if size[1] < f * 2.2 and "\\n" not in "".join(strs) and w > size[0] + 4:
                issues.append((line_of(m.start()), "oneline-over", f"{a[1]} box {size[0]:.0f}x{size[1]:.0f} text {w:.0f}px [{code}] {clean(st)[:40]}"))
        anchor, pos, pivot = vec(pa[1], env), vec(pa[3], env), vec(pa[2], env)
        if anchor and pos and pivot:
            rects.append((a[0], pa[1], anchor, pivot, pos, size, line_of(m.start()), a[1]))

    for m in re.finditer(r"resizeTextMinSize\s*=\s*(\d+)\s*;", src):
        if int(m.group(1)) < MIN_RENDER:
            issues.append((line_of(m.start()), "min-font", f"best-fit floor {m.group(1)}"))

    for i in range(len(rects)):
        for j in range(i + 1, len(rects)):
            p1, an1, a1, v1, o1, s1, l1, n1 = rects[i]
            p2, an2, a2, v2, o2, s2, l2, n2 = rects[j]
            if p1 != p2 or a1 != a2 or abs(l1 - l2) > 60:
                continue
            x1, y1 = o1[0] - v1[0] * s1[0], o1[1] - v1[1] * s1[1]
            x2, y2 = o2[0] - v2[0] * s2[0], o2[1] - v2[1] * s2[1]
            ox = min(x1 + s1[0], x2 + s2[0]) - max(x1, x2)
            oy = min(y1 + s1[1], y2 + s2[1]) - max(y1, y2)
            if ox > 4 and oy > 4:
                issues.append((l2, "overlap", f"{n1}(L{l1}) x {n2} by {ox:.0f}x{oy:.0f} in {p1}"))
    return sorted(issues)


def main():
    filt = sys.argv[1:]
    files = sorted(glob.glob(os.path.join(UI, "**", "*.cs"), recursive=True))
    total = 0
    for f in files:
        name = os.path.relpath(f, UI)
        if filt and not any(x in name for x in filt):
            continue
        iss = audit(f)
        if not iss:
            continue
        print(f"== {name} ({len(iss)})")
        for ln, kind, msg in iss:
            print(f"  L{ln} {kind}: {msg}")
        total += len(iss)
    print(f"TOTAL {total}")


if __name__ == "__main__":
    main()
