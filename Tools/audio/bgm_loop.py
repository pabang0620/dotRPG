# -*- coding: utf-8 -*-
"""BGM 검증과 이음매 없는 루프 만들기 (Docs/AUDIO_TEST_CHECKLIST.md 의 자동 검증 부분).

생성곡(WAV)마다:
  1) 박: librosa beat_track(템포 힌트) -> 실제 BPM, 박 간격 흔들림(ms)
  2) 대표 멜로디 반복: 마디 단위 크로마 유사도. 첫 4마디(훅)와 비슷한 4마디 묶음이 몇 번 다시 나오는지,
     전체 마디가 곡 안 다른 마디와 얼마나 겹치는지(반복도)
  3) 루프: 같은 내용으로 시작하는 두 마디(S, E)를 찾아 [S, E)를 루프로 자르고, 시작 한 박을
     E 뒤 소리에서 S 소리로 등전력 크로스페이드해 반복 재생 때 끊김이 없게 한다
  4) 음량: -14 LUFS, 피크 -1 dBFS 이하, 클리핑·긴 무음 검사, 이음매의 음량 차이와 클릭
결과: <out>/<key>.wav(루프), <key>.ogg(페이드 없음), <key>.json(수치)

사용: python bgm_loop.py <in.wav> <out_dir> <key> --bpm 100 [--bpb 4]
(librosa, soundfile, pyloudnorm 필요)
"""
import argparse, json, subprocess, sys
from pathlib import Path
import numpy as np
import librosa, soundfile as sf, pyloudnorm as pyln

TARGET_LUFS, PEAK_DB = -14.0, -1.0


def bar_chroma(y, sr, beats, bpb):
    chroma = librosa.feature.chroma_cqt(y=y, sr=sr, hop_length=512)
    bf = librosa.time_to_frames(beats, sr=sr, hop_length=512)
    sync = librosa.util.sync(chroma, bf, aggregate=np.median)  # one column per beat segment
    n = sync.shape[1] // bpb
    bars = np.stack([sync[:, i * bpb:(i + 1) * bpb].reshape(-1) for i in range(n)]) if n else np.zeros((0, 12 * bpb))
    norm = np.linalg.norm(bars, axis=1, keepdims=True) + 1e-9
    return bars / norm


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('inp'); ap.add_argument('out'); ap.add_argument('key')
    ap.add_argument('--bpm', type=float, required=True); ap.add_argument('--bpb', type=int, default=4)
    a = ap.parse_args()
    out = Path(a.out); out.mkdir(parents=True, exist_ok=True)
    audio, sr0 = sf.read(a.inp, always_2d=True)
    audio = audio.astype(np.float32)
    mono = librosa.to_mono(audio.T)
    sr = 22050
    y = librosa.resample(mono, orig_sr=sr0, target_sr=sr)
    dur = len(mono) / sr0

    # 1) beats
    tempo, beat_frames = librosa.beat.beat_track(y=y, sr=sr, start_bpm=a.bpm, tightness=400, units='frames')
    beats = librosa.frames_to_time(beat_frames, sr=sr)
    ibi = np.diff(beats)
    med = float(np.median(ibi)) if len(ibi) else 0.0
    bpm = 60.0 / med if med else 0.0
    # allow half/double detection
    for f in (2.0, 0.5):
        if abs(bpm * f - a.bpm) < abs(bpm - a.bpm): bpm *= f
    jitter_ms = float(np.std(ibi - med) * 1000) if len(ibi) else 999.0

    # downbeat phase: strongest low-band onset phase
    S = np.abs(librosa.stft(y, n_fft=2048, hop_length=512))
    freqs = librosa.fft_frequencies(sr=sr, n_fft=2048)
    low = S[freqs < 150].sum(axis=0)
    lowf = np.maximum(0, np.diff(low, prepend=low[:1]))
    bf = beat_frames[beat_frames < len(lowf)]
    phase = int(np.argmax([lowf[bf[p::a.bpb]].mean() if len(bf[p::a.bpb]) else 0 for p in range(a.bpb)]))
    bars_t = beats[phase::a.bpb]

    # 2) hook / repetition
    bars = bar_chroma(y, sr, beats[phase:], a.bpb)
    nb = len(bars)
    rep = 0.0; hook_hits = 0
    if nb >= 8:
        sim = bars @ bars.T
        for i in range(nb):
            sim[i, max(0, i - 1):i + 2] = -1
        rep = float(np.mean(sim.max(axis=1)))
        # the hook = the 4-bar phrase in the first 8 bars that comes back most often
        def returns(h0):
            hook = bars[h0:h0 + 4].reshape(-1); hook = hook / (np.linalg.norm(hook) + 1e-9)
            n, i = 0, h0 + 4
            while i + 4 <= nb:
                seg = bars[i:i + 4].reshape(-1); seg = seg / (np.linalg.norm(seg) + 1e-9)
                if float(seg @ hook) > 0.85: n += 1; i += 4
                else: i += 1
            return n
        hook_hits = max(returns(h0) for h0 in range(0, min(8, nb - 8) + 1))

    # 3) loop points: bars S (first 30%) and E (later) whose next two bars sound the same; [S, E) is the loop
    loop = None
    # a generated file sometimes holds two takes with a gap between them: a loop must not span a silence over 1s
    fr = int(0.1 * sr0)
    quiet = np.array([20 * np.log10(np.sqrt(np.mean(mono[i:i + fr] ** 2)) + 1e-9) < -50 for i in range(0, len(mono) - fr, fr)])
    gaps, run = [], 0  # (start, end) of each silence over 1s
    for i, q in enumerate(list(quiet) + [False]):
        if q: run += 1; continue
        if run > 10: gaps.append(((i - run) * 0.1, i * 0.1))
        run = 0
    min_len = 45.0 if gaps else max(45.0, 0.5 * dur)
    if nb >= 8:
        best = (-1.0, None, None)
        bpm_safe = max(bpm, 1)
        for s_idx in range(0, max(1, int(nb * (0.6 if gaps else 0.3)))):
            if bars_t[s_idx] < 0.3: continue
            for e_idx in range(s_idx + 8, nb - 1):
                length = bars_t[e_idx] - bars_t[s_idx]
                if length < min_len or bars_t[e_idx] + 60.0 / bpm_safe > dur - 0.2: continue
                if any(g0 < bars_t[e_idx] + 60.0 / bpm_safe and g1 > bars_t[s_idx] - 0.2 for g0, g1 in gaps): continue
                sc = (bars[e_idx] @ bars[s_idx] + bars[e_idx + 1] @ bars[s_idx + 1]) / 2
                sc += 0.03 * (length / dur)  # prefer longer loops a little
                if sc > best[0]: best = (sc, s_idx, e_idx)
        if best[1] is not None:
            loop = (best[1], best[2], float(best[0]))

    rec = {"key": a.key, "duration_s": round(dur, 2), "bpm_hint": a.bpm, "bpm": round(bpm, 1),
           "bpm_error_pct": round(abs(bpm - a.bpm) / a.bpm * 100, 1), "beat_jitter_ms": round(jitter_ms, 1),
           "bars": nb, "repetition": round(rep, 3), "hook_returns": hook_hits}
    if loop is None:
        rec["loop"] = "FAIL: no matching bars"
        print(json.dumps(rec, ensure_ascii=False)); (out / f"{a.key}.json").write_text(json.dumps(rec, ensure_ascii=False, indent=1)); return 1

    s_idx, e_idx, match = loop
    S0 = int(round(bars_t[s_idx] * sr0)); E0 = int(round(bars_t[e_idx] * sr0))
    xf = int(round(60.0 / max(bpm, 1) * sr0))  # one beat
    xf = min(xf, len(audio) - E0, S0 if S0 > 0 else xf)
    body = audio[S0:E0].copy()
    tail = audio[E0:E0 + xf]
    if len(tail) == xf and xf > 0:
        t = np.linspace(0, np.pi / 2, xf, dtype=np.float32)[:, None]
        body[:xf] = body[:xf] * np.sin(t) + tail * np.cos(t)  # starts as the sound after E, becomes S
    # 4) loudness and peak
    meter = pyln.Meter(sr0)
    lufs = meter.integrated_loudness(body)
    body = pyln.normalize.loudness(body, lufs, TARGET_LUFS).astype(np.float32)
    peak = float(np.max(np.abs(body)))
    lim = 10 ** (PEAK_DB / 20)
    if peak > lim:  # soft-knee limiting of the few peaks over -1 dBFS
        over = np.abs(body) > lim * 0.9
        body[over] = np.sign(body[over]) * (lim * 0.9 + (np.abs(body[over]) - lim * 0.9) / (1 + (np.abs(body[over]) - lim * 0.9) / (lim * 0.1)))
    peak_db = float(20 * np.log10(np.max(np.abs(body)) + 1e-12))
    clipped = int(np.sum(np.abs(body) >= 0.999))
    # seam: level jump and sample step at the loop point (end -> start)
    w = int(0.05 * sr0)
    rms = lambda x: float(np.sqrt(np.mean(x ** 2)) + 1e-9)
    seam_raw = abs(20 * np.log10(rms(body[-w:]) / rms(body[:w])))
    # a downbeat is naturally louder than the end of the bar before it: judge the seam against the same jump at the
    # loop's own bar lines (only the excess over that is a seam problem)
    inner = []
    for t in bars_t[s_idx + 1:e_idx]:
        k = int(round(t * sr0)) - S0
        if w <= k < len(body) - w: inner.append(abs(20 * np.log10(rms(body[k - w:k]) / rms(body[k:k + w]))))
    seam_db = max(0.0, seam_raw - (float(np.median(inner)) if inner else 0.0))
    step = float(np.max(np.abs(body[0] - body[-1])))
    local = float(np.median(np.abs(np.diff(body[:w], axis=0))) + 1e-6)
    click_ratio = step / local
    # silence inside
    frame = int(0.1 * sr0)
    env = np.array([rms(body[i:i + frame]) for i in range(0, len(body) - frame, frame)])
    sil = 0; run = 0
    for v in env:
        run = run + 1 if 20 * np.log10(v) < -50 else 0; sil = max(sil, run)

    rec.update({"loop_start_s": round(bars_t[s_idx], 2), "loop_end_s": round(bars_t[e_idx], 2),
                "loop_len_s": round((E0 - S0) / sr0, 2), "loop_bar_match": round(match, 3),
                "lufs": round(meter.integrated_loudness(body), 1), "peak_dbfs": round(peak_db, 2), "clipped_samples": clipped,
                "seam_level_db": round(seam_db, 2), "seam_raw_db": round(seam_raw, 2), "seam_click_ratio": round(click_ratio, 1), "longest_silence_s": round(sil * 0.1, 1)})
    # verdict
    fails = []
    if rec["bpm_error_pct"] > 6: fails.append("tempo off")
    if rec["beat_jitter_ms"] > 25: fails.append("unsteady beat")
    if rec["hook_returns"] < 2: fails.append("hook rarely returns")
    if rec["loop_bar_match"] < 0.8: fails.append("weak loop match")
    if rec["seam_level_db"] > 3: fails.append("seam level jump")
    if rec["seam_click_ratio"] > 40: fails.append("seam click")
    if clipped: fails.append("clipping")
    if rec["longest_silence_s"] > 1.0: fails.append("silence gap")
    rec["verdict"] = "PASS" if not fails else "CHECK: " + ", ".join(fails)
    sf.write(out / f"{a.key}.wav", body, sr0, subtype='PCM_16')
    subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", str(out / f"{a.key}.wav"), "-c:a", "libvorbis", "-q:a", "6", str(out / f"{a.key}.ogg")], check=True)
    (out / f"{a.key}.json").write_text(json.dumps(rec, ensure_ascii=False, indent=1))
    print(json.dumps(rec, ensure_ascii=False))
    return 0


if __name__ == "__main__":
    sys.exit(main())
