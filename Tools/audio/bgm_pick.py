# -*- coding: utf-8 -*-
"""Gen2 BGM 후보(c1, c2 ...)를 모두 검증·루프 처리하고, 곡마다 가장 나은 후보를 게임에 넣는다.

순서: 검증 통과 > 대표 멜로디 반복 횟수 > 루프 일치도 > 이음매 > 박 흔들림.
사람이 듣고 고른 후보는 Tools/audio/bgm_overrides.json ({"<키>": "cN"})이 자동 선정보다 우선한다.
게임: Assets/Resources/Audio/<key>.ogg (뽑힌 후보), 비교용: AudioSource/Gen2/candidates/<key>_cN.ogg
보고: AudioSource/Gen2/report.json, report.txt
이음매 미리듣기: AudioSource/Gen2/seam_preview/<key>.wav (뽑힌 루프의 끝 8초 + 처음 8초, 이어지는 곳에서 끊김이 들리면 문제)
사용: <venv>/bin/python Tools/audio/bgm_pick.py [--apply]
"""
import json, shutil, subprocess, sys
import numpy as np
import soundfile as sf
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GEN = ROOT / "AudioSource" / "Gen2"
BPM = {"music_title": 92, "music_village": 72, "music_canyon": 104, "music_winter": 88, "music_forest": 120,
       "music_dgn_canyon": 128, "music_dgn_forest": 112, "music_dgn_winter": 132, "music_boss": 150, "music_raid": 140,
       "music_raid_enrage": 165, "music_clear": 112, "music_fail": 76}
BPB = {"music_winter": 3}


def score(r):
    if "loop_len_s" not in r: return (-1,)
    return (1 if r["verdict"] == "PASS" else 0, min(r["hook_returns"], 8), r["loop_bar_match"], -r["seam_level_db"], -r["beat_jitter_ms"])


def seam_preview(src, dst, sec=8.0):
    y, sr = sf.read(src, always_2d=True)
    n = min(int(sec * sr), len(y) // 2)
    sf.write(dst, np.concatenate([y[-n:], y[:n]]), sr, subtype='PCM_16')


def main():
    apply = "--apply" in sys.argv
    cand_dir = GEN / "candidates"; cand_dir.mkdir(exist_ok=True)
    report = {}
    ov_path = Path(__file__).with_name("bgm_overrides.json")
    overrides = json.loads(ov_path.read_text(encoding="utf-8")) if ov_path.exists() else {}
    for key, bpm in BPM.items():
        rows = []
        for wav in sorted(GEN.glob(f"c*/{key}_c*.wav")):
            tag = wav.stem.rsplit("_", 1)[1]
            out = GEN / f"out_{tag}"
            p = subprocess.run([sys.executable, str(Path(__file__).with_name("bgm_loop.py")), str(wav), str(out), key,
                                "--bpm", str(bpm), "--bpb", str(BPB.get(key, 4))], capture_output=True, text=True)
            try: r = json.loads(p.stdout.strip().splitlines()[-1])
            except Exception: r = {"key": key, "loop": "FAIL: analysis error " + p.stderr[-200:]}
            r["candidate"] = tag
            rows.append(r)
            if (out / f"{key}.ogg").exists(): shutil.copyfile(out / f"{key}.ogg", cand_dir / f"{key}_{tag}.ogg")
        if not rows: continue
        best = max(rows, key=score)
        forced = next((r for r in rows if r["candidate"] == overrides.get(key) and "loop_len_s" in r), None)
        if forced: best = forced
        report[key] = {"picked": best["candidate"], "candidates": rows}
        if apply and "loop_len_s" in best:
            shutil.copyfile(GEN / f"out_{best['candidate']}" / f"{key}.ogg", ROOT / "Assets" / "Resources" / "Audio" / f"{key}.ogg")
        if "loop_len_s" in best:
            (GEN / "seam_preview").mkdir(exist_ok=True)
            seam_preview(GEN / f"out_{best['candidate']}" / f"{key}.wav", GEN / "seam_preview" / f"{key}.wav")
    (GEN / "report.json").write_text(json.dumps(report, ensure_ascii=False, indent=1))
    lines = []
    for key, v in report.items():
        for r in v["candidates"]:
            mark = "*" if r["candidate"] == v["picked"] else " "
            lines.append(f"{mark} {key:18} {r['candidate']} bpm {r.get('bpm','-')}/{BPM[key]} jitter {r.get('beat_jitter_ms','-')}ms "
                         f"hook {r.get('hook_returns','-')} loop {r.get('loop_len_s','-')}s match {r.get('loop_bar_match','-')} "
                         f"seam {r.get('seam_level_db','-')}dB lufs {r.get('lufs','-')} | {r.get('verdict', r.get('loop'))}")
    (GEN / "report.txt").write_text("\n".join(lines) + "\n")
    print("\n".join(lines))


if __name__ == "__main__":
    main()
