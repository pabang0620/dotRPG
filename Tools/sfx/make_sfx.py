#!/usr/bin/env python3
"""Offline sound design for dotRPG SFX (numpy + scipy + ffmpeg CLI).

Renders every gameplay SFX key as a 48 kHz stereo WAV master into AudioSource/Sfx/<key>.wav,
loudness-normalizes it per category, peak-limits it, verifies it (report.txt), encodes it to
Assets/Resources/Audio/<key>.ogg (libvorbis q6) and writes a matching Unity .meta with
Normalize off (Unity's default normalize would erase the category loudness balance).

AudioManager.PlaySfx(key) loads Resources/Audio/<key> before falling back to SfxSynth, so no
code change is needed. music_* files are never touched.

Usage:  python3 Tools/sfx/make_sfx.py [--only key1,key2] [--no-ogg] [--replace]
"""
import argparse
import hashlib
import os
import subprocess
import sys

import numpy as np
from scipy import signal
from scipy.io import wavfile
from scipy.ndimage import maximum_filter1d, uniform_filter1d

SR = 48000
ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
WAV_DIR = os.path.join(ROOT, "AudioSource", "Sfx")
OGG_DIR = os.path.join(ROOT, "Assets", "Resources", "Audio")

# ---------------------------------------------------------------------------------------------
# Category loudness targets. Metric: max 100 ms K-weighted loudness (BS.1770 K filter, LUFS
# scale). ITU integrated loudness needs 400 ms blocks, which most of these sounds are shorter
# than, so the short-window max is the comparable figure across all files.
# ---------------------------------------------------------------------------------------------
CATEGORIES = {
    "combat": -12.0,
    "skill": -13.0,
    "world": -15.5,
    "ui": -21.5,
    "jingle": -15.5,
}
CAT_RANGE = 2.5      # per-key offset must stay within +-this of the category target
TOLERANCE = 1.0      # measured vs per-key target
PEAK_CEIL_DB = -1.5  # sample peak ceiling for the master (ogg decode is checked against -1.0)
ORDER = ["combat", "skill", "world", "ui", "jingle"]
DUR_RANGE = {"combat": (0.06, 0.30), "skill": (0.2, 0.9), "world": (0.1, 1.6), "ui": (0.03, 0.3), "jingle": (0.8, 2.5)}
DUR_KEY = {"blip": (0.03, 0.08), "select": (0.03, 0.08)}

REGISTRY = {}


def sfx(category, offset=0.0, max_len=None):
    def deco(fn):
        REGISTRY[fn.__name__] = dict(fn=fn, cat=category, offset=offset, max_len=max_len)
        return fn
    return deco


# ---------------------------------------------------------------------------------------------
# DSP toolkit
# ---------------------------------------------------------------------------------------------
def n_of(d):
    return max(1, int(round(d * SR)))


def tvec(d):
    return np.arange(n_of(d)) / SR


def rng(seed):
    return np.random.default_rng(seed)


def noise(d, seed=0):
    return rng(seed).standard_normal(n_of(d))


def env_exp(d, tau, attack=0.001):
    t = tvec(d)
    e = np.exp(-t / tau)
    if attack > 0:
        a = np.clip(t / attack, 0, 1)
        e *= 0.5 - 0.5 * np.cos(np.pi * a)
    return e


def env_ahd(d, attack, hold, decay_tau):
    t = tvec(d)
    e = np.where(t < attack, 0.5 - 0.5 * np.cos(np.pi * t / max(attack, 1e-6)), 1.0)
    rel = np.clip(t - attack - hold, 0, None)
    return e * np.exp(-rel / decay_tau)


def env_swell(d, peak_at, rise_pow=2.0, fall_tau=0.05):
    """Rises (power curve) to 1 at peak_at, then decays exponentially."""
    t = tvec(d)
    up = np.clip(t / peak_at, 0, 1) ** rise_pow
    down = np.exp(-np.clip(t - peak_at, 0, None) / fall_tau)
    return np.where(t < peak_at, up, down)


def sweep(f0, f1, d, curve="exp", k=1.0):
    t = np.linspace(0, 1, n_of(d))
    if curve == "exp":
        return f0 * (f1 / f0) ** (t ** k)
    return f0 + (f1 - f0) * t ** k


def phase(freq, d=None):
    if np.isscalar(freq):
        freq = np.full(n_of(d), float(freq))
    return 2 * np.pi * np.cumsum(freq) / SR


def sine(freq, d=None, ph0=0.0):
    return np.sin(phase(freq, d) + ph0)


def saw(freq, d=None, top=14000.0):
    """Band-limited additive sawtooth (handles sweeps)."""
    if np.isscalar(freq):
        freq = np.full(n_of(d), float(freq))
    ph = phase(freq)
    out = np.zeros_like(ph)
    kmax = int(top / max(np.min(freq), 20)) + 1
    for k in range(1, min(kmax, 200)):
        mask = (k * freq) < top
        if not mask.any():
            break
        out += mask * np.sin(k * ph) / k
    return out * 0.6


def square(freq, d=None, top=14000.0):
    if np.isscalar(freq):
        freq = np.full(n_of(d), float(freq))
    ph = phase(freq)
    out = np.zeros_like(ph)
    for k in range(1, 200, 2):
        mask = (k * freq) < top
        if not mask.any():
            break
        out += mask * np.sin(k * ph) / k
    return out * 0.8


def fm(fc, ratio, index, d):
    """Simple 2-op FM. index may be an array (envelope)."""
    if np.isscalar(fc):
        fc = np.full(n_of(d), float(fc))
    pm = phase(fc * ratio)
    return np.sin(phase(fc) + index * np.sin(pm))


def _sos(kind, f, order=2):
    nyq = SR / 2
    if kind == "band":
        lo, hi = f
        lo = max(lo, 10) / nyq
        hi = min(hi, nyq * 0.98) / nyq
        return signal.butter(order, [lo, hi], btype="bandpass", output="sos")
    f = min(max(f, 10), nyq * 0.98) / nyq
    return signal.butter(order, f, btype=kind, output="sos")


def lp(x, f, order=2):
    return signal.sosfilt(_sos("low", f, order), x)


def hp(x, f, order=2):
    return signal.sosfilt(_sos("high", f, order), x)


def bp(x, lo, hi, order=2):
    return signal.sosfilt(_sos("band", (lo, hi), order), x)


def peak(x, f, q):
    b, a = signal.iirpeak(min(f, SR / 2 * 0.95), q, fs=SR)
    return signal.lfilter(b, a, x)


def sweep_filter(x, f_curve, kind="band", width=1.0, block=64):
    """Time-varying Butterworth filter, coefficient update every block (state carried).
    kind band: f_curve is the center, width is the band width in octaves."""
    out = np.zeros_like(x)
    zi = None
    for s in range(0, len(x), block):
        fc = float(f_curve[min(s, len(f_curve) - 1)])
        if kind == "band":
            sos = _sos("band", (fc * 2 ** (-width / 2), fc * 2 ** (width / 2)))
        else:
            sos = _sos(kind, fc)
        if zi is None:
            zi = np.zeros((sos.shape[0], 2))
        out[s:s + block], zi = signal.sosfilt(sos, x[s:s + block], zi=zi)
    return out


def sat(x, drive=2.0):
    return np.tanh(drive * x) / np.tanh(drive)


def modal(freqs, amps, taus, d, jitter=0.0, seed=0):
    r = rng(seed)
    t = tvec(d)
    out = np.zeros_like(t)
    for f, a, tau in zip(freqs, amps, taus):
        ff = f * (1 + jitter * r.uniform(-1, 1))
        out += a * np.sin(2 * np.pi * ff * t + r.uniform(0, 2 * np.pi)) * np.exp(-t / tau)
    return out


def mallet(f, d=0.5, bright=1.0, tau=0.25):
    """Marimba / glock style note: fundamental + 4x and 10x partials with faster decay."""
    t = tvec(d)
    x = np.sin(2 * np.pi * f * t) * np.exp(-t / tau)
    x += 0.35 * bright * np.sin(2 * np.pi * f * 3.93 * t) * np.exp(-t / (tau * 0.3))
    x += 0.12 * bright * np.sin(2 * np.pi * f * 9.2 * t) * np.exp(-t / (tau * 0.12))
    x *= env_exp(d, 10, attack=0.0015)
    return x


def bell(f, d=1.2, tau=0.6, bright=1.0):
    """Small bright bell (inharmonic partials)."""
    ratios = [1.0, 2.0, 2.76, 3.0, 4.07, 5.43, 6.8]
    amps = [1.0, 0.55, 0.4 * bright, 0.25, 0.22 * bright, 0.12 * bright, 0.07 * bright]
    taus = [tau, tau * 0.7, tau * 0.5, tau * 0.6, tau * 0.35, tau * 0.25, tau * 0.18]
    return modal([f * r for r in ratios], amps, taus, d) * env_exp(d, 100, attack=0.001)


def brass(f, d, attack=0.03, tau=0.4, cutoff=(600, 3500)):
    x = saw(f, d)
    ft = sweep(cutoff[0], cutoff[1], n_of(min(attack * 3, d)) / SR)
    fcurve = np.concatenate([ft, cutoff[1] * np.exp(-tvec(d - len(ft) / SR) / (tau * 2)) + cutoff[0]])[:len(x)]
    fcurve = np.pad(fcurve, (0, len(x) - len(fcurve)), mode="edge")
    x = sweep_filter(x, fcurve, kind="low")
    return x * env_ahd(d, attack, 0.05, tau)


def mix_len(*arrs):
    return max(a.shape[-1] for a in arrs)


def stereo(x, pan=0.0, width=0.0, delay_ms=0.0):
    """Mono -> stereo. pan -1..1 constant power; delay_ms (0..1) = amount of decorrelated width."""
    th = (pan + 1) * np.pi / 4
    l, r = np.cos(th) * x, np.sin(th) * x
    if delay_ms > 0:
        # Mono-safe widening: blend in an allpass-decorrelated copy (no Haas comb filtering).
        amt = min(1.0, delay_ms) * 0.5
        ap = x
        for c in (0.62, -0.47, 0.33, -0.71):
            ap = signal.lfilter([c, 1.0], [1.0, c], ap)
        l = l * np.sqrt(1 - amt ** 2) + amt * np.cos(th) * ap
        r = r * np.sqrt(1 - amt ** 2) - amt * np.sin(th) * ap * 0.6
    out = np.stack([l, r]) * np.sqrt(2)
    if width > 0:
        m = (out[0] + out[1]) / 2
        s = (out[0] - out[1]) / 2 * (1 + 0.5 * width)
        out = np.stack([m + s, m - s])
    return out


def st_noise(d, seed, corr=0.5):
    """Partly decorrelated stereo noise."""
    a = noise(d, seed)
    b = noise(d, seed + 1000)
    return np.stack([a, corr * a + np.sqrt(1 - corr ** 2) * b])


def pan_curve(x, p0, p1):
    p = np.linspace(p0, p1, len(x))
    th = (p + 1) * np.pi / 4
    return np.stack([np.cos(th) * x, np.sin(th) * x]) * np.sqrt(2)


def at(total, t0, layer):
    """Place a stereo or mono layer at time t0 in a stereo buffer of length total (seconds)."""
    if layer.ndim == 1:
        layer = stereo(layer)
    out = np.zeros((2, n_of(total)))
    s = n_of(t0) if t0 > 0 else 0
    e = min(out.shape[1], s + layer.shape[1])
    out[:, s:e] += layer[:, :e - s]
    return out


def mix(total, *placed):
    """placed: (t0, gain, layer) tuples."""
    out = np.zeros((2, n_of(total)))
    for t0, g, layer in placed:
        out += g * at(total, t0, layer)
    return out


def reverb(x, decay=0.6, wet=0.2, predelay=0.008, damp=6000.0, size_hp=200.0, seed=7):
    """Convolution with a synthetic stereo IR (decaying decorrelated noise, darker over time)."""
    if x.ndim == 1:
        x = stereo(x)
    L = n_of(decay * 1.2 + predelay)
    t = np.arange(L) / SR
    irs = []
    for ch in range(2):
        nz = rng(seed + ch).standard_normal(L)
        e = np.exp(-6.9 * np.clip(t - predelay, 0, None) / decay) * (t >= predelay)
        early = np.zeros(L)
        r = rng(seed + 10 + ch)
        for _ in range(8):
            k = int(r.uniform(predelay, predelay + 0.04) * SR)
            if k < L:
                early[k] += r.uniform(0.2, 0.6) * r.choice([-1, 1])
        ir = nz * e
        # Darken the tail: crossfade between bright and lowpassed copies.
        dark = lp(ir, damp * 0.35)
        mixw = np.clip(t / decay, 0, 1)
        ir = (1 - mixw) * lp(ir, damp) + mixw * dark
        ir = hp(ir, size_hp)
        ir += early
        ir /= np.sqrt(np.sum(ir ** 2)) + 1e-12
        irs.append(ir)
    out_len = x.shape[1] + L - 1
    wet_sig = np.zeros((2, out_len))
    for ch in range(2):
        wet_sig[ch] = signal.fftconvolve(x[ch], irs[ch])
    dry = np.pad(x, ((0, 0), (0, out_len - x.shape[1])))
    return dry * (1 - wet * 0.5) + wet_sig * wet


def crackle(d, density, seed, f_lo=1500, f_hi=6000, decay_pow=1.0):
    """Sparse random clicks (fire crackle, debris), density in events/sec, fading with time."""
    r = rng(seed)
    n = n_of(d)
    x = np.zeros(n)
    t = 0.0
    while t < d:
        t += r.exponential(1 / density)
        if t >= d:
            break
        i = int(t * SR)
        amp = r.uniform(0.3, 1.0) * (1 - t / d) ** decay_pow
        ln = int(r.uniform(0.0005, 0.003) * SR)
        burst = r.standard_normal(ln) * np.exp(-np.arange(ln) / (ln / 3))
        e = min(n, i + ln)
        x[i:e] += amp * burst[:e - i]
    return bp(x, f_lo, f_hi)


def grains(d, count, seed, f_range=(400, 3000), g_len=(0.004, 0.025), t_pow=1.8):
    """Granular debris: short resonant noise grains, more at the start."""
    r = rng(seed)
    n = n_of(d)
    x = np.zeros(n)
    for _ in range(count):
        t0 = d * r.uniform(0, 1) ** t_pow
        gl = r.uniform(*g_len)
        g = r.standard_normal(n_of(gl)) * env_exp(gl, gl / 4, attack=0.0005)
        fc = r.uniform(*f_range)
        g = peak(g, fc, r.uniform(3, 9))
        amp = r.uniform(0.3, 1.0) * (1 - t0 / d) ** 0.7
        i = int(t0 * SR)
        e = min(n, i + len(g))
        x[i:e] += amp * g[:e - i]
    return x


def midi(m):
    return 440.0 * 2 ** ((m - 69) / 12)


def thump(f0=160, f1=45, d=0.25, tau=0.07, drive=1.5):
    """Kick-like body: fast pitch-dropping sine."""
    f = f1 + (f0 - f1) * np.exp(-tvec(d) / (tau * 0.4))
    return sat(sine(f) * env_exp(d, tau, attack=0.0008), drive)


def whoosh(d, f0, f1, peak_at, seed, width=1.2, curve_k=1.0, fall=None):
    nz = noise(d, seed)
    fc = sweep(f0, f1, d, k=curve_k)
    x = sweep_filter(nz, fc, kind="band", width=width)
    return x * env_swell(d, peak_at, 2.0, fall or (d - peak_at) / 3)


def sparkle(d, count, seed, notes, start=0.0, spread=None, tau=0.12):
    """Random high sine pings (twinkles) chosen from notes (Hz)."""
    r = rng(seed)
    spread = spread if spread is not None else d - start
    out = np.zeros((2, n_of(d)))
    for _ in range(count):
        t0 = start + r.uniform(0, 1) * spread
        f = r.choice(notes) * (1 + r.uniform(-0.003, 0.003))
        ln = min(tau * 4, d - t0)
        if ln <= 0.01:
            continue
        p = sine(f, ln) * env_exp(ln, tau * r.uniform(0.6, 1.3), attack=0.002)
        p += 0.3 * sine(f * 2.01, ln) * env_exp(ln, tau * 0.4, attack=0.002)
        out += r.uniform(0.3, 1.0) * at(d, t0, stereo(p, r.uniform(-0.8, 0.8)))
    return out


def arp(notes, step, d, voice, gains=None, pans=None):
    out = np.zeros((2, n_of(d)))
    for i, m in enumerate(notes):
        t0 = i * step
        if t0 >= d:
            break
        g = gains[i] if gains else 1.0
        p = pans[i] if pans else 0.0
        out += g * at(d, t0, stereo(voice(midi(m), d - t0), p))
    return out


# ---------------------------------------------------------------------------------------------
# Sounds: combat
# ---------------------------------------------------------------------------------------------
@sfx("combat", max_len=0.3)
def hit():
    d = 0.24
    body = thump(190, 70, d, tau=0.045, drive=2.5)
    mid = bp(noise(d, 1), 500, 1800) * env_exp(d, 0.03, attack=0.0005)
    crack = hp(noise(d, 2), 2500) * env_exp(d, 0.008, attack=0.0002)
    click = sine(sweep(4200, 1500, 0.006), 0.006) * np.hanning(n_of(0.006))
    x = mix(d, (0, 0.9, body), (0, 0.55, sat(mid * 2.0, 1.5)), (0, 0.6, stereo(crack, 0, 0.3)),
            (0, 0.25, click))
    return reverb(x, decay=0.18, wet=0.12, damp=5000)


@sfx("combat", offset=-2.5, max_len=0.3)
def swing():
    d = 0.2
    w = whoosh(d, 700, 2600, 0.06, 11, width=1.0, curve_k=0.7, fall=0.04)
    air = hp(noise(d, 12), 5000) * env_swell(d, 0.05, 2, 0.03)
    x = pan_curve(w, -0.35, 0.35) + 0.25 * stereo(air, 0, 0.4)
    low = lp(noise(d, 13), 400) * env_swell(d, 0.06, 2, 0.04)
    return x + 0.5 * stereo(low)


@sfx("combat", offset=-2.0, max_len=0.3)
def enemy_attack():
    d = 0.18
    w = whoosh(d, 350, 1400, 0.07, 21, width=1.2, curve_k=0.8, fall=0.035)
    growl = lp(saw(sweep(110, 80, d)), 900) * env_ahd(d, 0.01, 0.04, 0.04)
    return pan_curve(w, 0.3, -0.3) + 0.35 * stereo(growl)


@sfx("combat", offset=-1.5, max_len=0.3)
def enemy_windup():
    # Boss tell: a rising, tremolo-pulsing warning tone with a noise riser under it.
    d = 0.3
    f = sweep(380, 920, d, k=1.3)
    trem = 0.65 + 0.35 * np.sin(2 * np.pi * sweep(14, 30, d, curve="lin"))
    tone = fm(f, 2.0, 1.5 * np.linspace(0.3, 1.0, n_of(d)), d) * trem
    tone = lp(tone, 4000) * env_ahd(d, 0.03, 0.2, 0.03)
    rise = sweep_filter(noise(d, 31), sweep(500, 3500, d), "band", 1.0) * np.linspace(0, 1, n_of(d)) ** 2
    x = stereo(tone, 0, 0.0) + 0.5 * stereo(rise, 0, 0.6, delay_ms=0.4)
    return reverb(x, 0.25, 0.15)


@sfx("combat", max_len=0.3)
def hurt():
    # Player takes damage: a dull body hit plus a short cute descending "oof" squeak.
    d = 0.26
    body = thump(140, 55, d, tau=0.05, drive=3)
    crunch = bp(noise(d, 41), 300, 2500) * env_exp(d, 0.025, attack=0.0005)
    oof = lp(square(sweep(520, 230, 0.16, k=0.6), 0.16), 2200) * env_ahd(0.16, 0.005, 0.04, 0.05)
    x = mix(d, (0, 0.8, body), (0, 0.5, sat(crunch * 2, 2)), (0.01, 0.35, oof))
    return reverb(x, 0.2, 0.1)


@sfx("combat", max_len=0.3)
def enemy_die():
    # Kill pop: thump + bright upward "pop" + a soft poof of smoke that darkens, tiny sparkles.
    d = 0.5
    body = thump(220, 50, d, tau=0.06, drive=2.5)
    pop = sine(sweep(500, 1900, 0.05, k=0.5), 0.05) * env_exp(0.05, 0.02, attack=0.001)
    poof_n = noise(d, 51)
    poof = sweep_filter(poof_n, sweep(3500, 400, d, k=0.6), "low") * env_swell(d, 0.03, 1.5, 0.12)
    crack = hp(noise(0.02, 52), 3000) * env_exp(0.02, 0.005)
    sp = sparkle(d, 5, 53, [midi(m) for m in (88, 91, 95, 100)], start=0.04, spread=0.12, tau=0.06)
    x = mix(d, (0, 0.85, body), (0, 0.55, pop), (0, 0.55, stereo(poof, 0, 0.6, 0.3)),
            (0, 0.5, crack), (0, 0.18, sp))
    return reverb(x, 0.3, 0.15)


# ---------------------------------------------------------------------------------------------
# Sounds: skills
# ---------------------------------------------------------------------------------------------
@sfx("skill", offset=-2.0, max_len=0.45)
def magic():
    # Basic ranged attack (played constantly): soft bright zap with a little sparkle.
    d = 0.3
    f = sweep(480, 1500, 0.18, k=0.6)
    z = fm(f, 1.5, 2.0 * np.exp(-tvec(0.18) / 0.05), 0.18) * env_ahd(0.18, 0.004, 0.03, 0.05)
    air = whoosh(0.2, 1500, 5000, 0.04, 61, width=1.5)
    sp = sparkle(d, 4, 62, [midi(m) for m in (84, 88, 91, 96)], start=0.03, spread=0.1, tau=0.05)
    x = mix(d, (0, 0.6, stereo(z, 0, 0.2, 0.3)), (0, 0.35, stereo(air, 0, 0.6, 0.5)), (0, 0.22, sp))
    return reverb(x, 0.35, 0.2)


@sfx("skill", max_len=0.5)
def c_slash():
    # Sharp blade: metallic "shing" ring over a fast hiss and a low cut.
    d = 0.32
    hiss = sweep_filter(noise(d, 71), sweep(7000, 1800, d, k=0.5), "band", 1.2) * env_exp(d, 0.05, attack=0.002)
    ring = modal([3150, 4720, 6180, 7900], [1, 0.6, 0.4, 0.2], [0.12, 0.08, 0.06, 0.04], d, seed=72)
    ring *= env_exp(d, 10, attack=0.003)
    low = whoosh(d, 300, 900, 0.03, 73, width=1.0, fall=0.04)
    x = mix(d, (0, 0.8, pan_curve(hiss, -0.5, 0.5)), (0, 0.3, stereo(ring, 0, 0.5, 0.2)), (0, 0.5, low),
            (0, 0.35, thump(260, 90, 0.1, 0.02, 2)))
    return reverb(x, 0.3, 0.16, damp=8000)


@sfx("skill", offset=1.0, max_len=0.7)
def c_heavy():
    # Heavy slam: big saturated low boom, crunch, debris, roomy tail.
    d = 0.6
    body = thump(150, 38, d, tau=0.12, drive=3.5)
    crunch = lp(noise(d, 81), 2500) * env_exp(d, 0.04, attack=0.0005)
    crack = hp(noise(d, 82), 2000) * env_exp(d, 0.01, attack=0.0002)
    deb = grains(d, 25, 83, (500, 3500), t_pow=2.2)
    x = mix(d, (0, 1.0, body), (0, 0.7, sat(crunch * 2.5, 2.5)), (0, 0.4, stereo(crack, 0, 0.5)),
            (0.02, 0.3, stereo(deb, 0, 0.8, 0.4)))
    return reverb(x, 0.6, 0.18, damp=3500)


@sfx("skill", offset=-1.0, max_len=0.5)
def c_dash():
    d = 0.34
    w = whoosh(d, 400, 4200, 0.12, 91, width=1.0, curve_k=0.8, fall=0.06)
    air = sweep_filter(noise(d, 92), sweep(2000, 9000, d), "high") * env_swell(d, 0.1, 2, 0.05)
    low = lp(noise(d, 93), 300) * env_swell(d, 0.1, 2, 0.06)
    x = pan_curve(w, -0.8, 0.8) + 0.3 * pan_curve(air, -0.6, 0.6) + 0.6 * stereo(low)
    return reverb(x, 0.25, 0.12)


@sfx("skill", max_len=0.8)
def c_shield():
    # Shield bash: metal plate clang (inharmonic modes) over a dull low hit.
    d = 0.7
    base = 410
    ratios = [1.0, 1.59, 2.14, 2.3, 2.65, 2.92, 3.5, 4.15]
    taus = [0.35, 0.28, 0.22, 0.25, 0.15, 0.12, 0.1, 0.07]
    amps = [1, 0.8, 0.6, 0.55, 0.4, 0.35, 0.25, 0.15]
    clang = modal([base * r for r in ratios], amps, taus, d, jitter=0.004, seed=101)
    clang *= env_exp(d, 10, attack=0.001)
    beat = 1 + 0.15 * np.sin(2 * np.pi * 6.5 * tvec(d))
    strike = bp(noise(d, 102), 1500, 7000) * env_exp(d, 0.008, attack=0.0002)
    x = mix(d, (0, 1.0, stereo(clang * beat, 0, 0.4, 0.4)), (0, 0.5, thump(170, 70, d, 0.05, 2.5)),
            (0, 0.5, strike))
    return reverb(sat(x, 1.3), 0.6, 0.2, damp=7000)


@sfx("skill", max_len=0.9)
def c_arcane():
    # Arcane: airy upward whoosh, detuned FM shimmer and twinkles.
    d = 0.7
    w = whoosh(d, 300, 2400, 0.22, 111, width=1.4, curve_k=0.8, fall=0.12)
    f = sweep(330, 990, 0.45, k=0.7)
    a1 = fm(f, 3.01, 1.8, 0.45)
    a2 = fm(f * 1.006, 2.0, 1.2, 0.45)
    pad = (stereo(a1, -0.3) + stereo(a2, 0.3)) * env_ahd(0.45, 0.05, 0.12, 0.1)
    sp = sparkle(d, 12, 112, [midi(m) for m in (86, 89, 93, 96, 98, 101)], start=0.12, spread=0.4, tau=0.1)
    x = mix(d, (0, 0.5, stereo(w, 0, 0.6, 0.4)), (0, 0.3, pad), (0, 0.3, sp),
            (0, 0.3, thump(120, 50, 0.3, 0.06, 1.5)))
    return reverb(x, 0.6, 0.3, damp=8000)


@sfx("skill", max_len=0.9)
def c_fire():
    # Fire burst: whoomp ignition, roaring noise body, crackles.
    d = 0.75
    boom = thump(110, 38, d, tau=0.13, drive=3)
    roar_n = noise(d, 121)
    roar_fc = np.concatenate([sweep(400, 2400, 0.08), 1900 * np.exp(-tvec(d - 0.08) / 0.25) + 500])
    roar = sweep_filter(roar_n, roar_fc, "low")
    roar = sat(roar * env_swell(d, 0.06, 1.5, 0.16) * 2.5, 2)
    flutter = 1 + 0.35 * lp(noise(d, 122), 30) * 6
    cr = crackle(d, 90, 123, 1800, 7000, decay_pow=1.2)
    ign = whoosh(0.12, 600, 3000, 0.05, 124, width=1.3)
    x = mix(d, (0, 0.9, boom), (0, 0.55, stereo(roar * flutter, 0, 0.6, 0.6)), (0.02, 0.55, stereo(cr, 0, 1.0, 0.7)),
            (0, 0.5, ign))
    return reverb(x, 0.45, 0.14, damp=5000)


@sfx("skill", max_len=0.9)
def c_ice():
    # Ice: hard crack, then glassy shards with a shimmering high tail.
    d = 0.75
    crack = hp(noise(0.03, 131), 3000) * env_exp(0.03, 0.006, attack=0.0002)
    snap = thump(400, 160, 0.1, 0.015, 2)
    r = rng(132)
    shards = np.zeros((2, n_of(d)))
    for i in range(14):
        t0 = 0.002 + 0.25 * r.uniform(0, 1) ** 2
        f = r.uniform(2400, 7200)
        ln = d - t0
        s = modal([f, f * 1.47, f * 2.09], [1, 0.4, 0.2], [r.uniform(0.04, 0.18), 0.05, 0.03], ln, seed=133 + i)
        shards += r.uniform(0.3, 1.0) * at(d, t0, stereo(s * env_exp(ln, 10, 0.0005), r.uniform(-0.9, 0.9)))
    shimmer = hp(noise(d, 134), 6000) * env_swell(d, 0.05, 1.0, 0.18) * (0.6 + 0.4 * np.sin(2 * np.pi * 23 * tvec(d)))
    x = mix(d, (0, 0.8, stereo(crack, 0, 0.5)), (0, 0.5, snap), (0, 0.18, shards), (0, 0.2, stereo(shimmer, 0, 0.8, 0.5)))
    return reverb(x, 0.8, 0.28, damp=11000, size_hp=600)


@sfx("skill", offset=1.0, max_len=0.9)
def c_thunder():
    # Thunder: crackling electric zap, a hard crack, and a short rolling rumble.
    d = 0.85
    r = rng(141)
    zd = 0.18
    jitter = 120 + 70 * lp(r.standard_normal(n_of(zd)), 60) * 8
    buzz = saw(np.clip(jitter, 60, 300), top=9000)
    gate = (lp(r.standard_normal(n_of(zd)), 80) > -0.02).astype(float)
    gate = uniform_filter1d(gate, 48)
    zap = hp(buzz, 300) * gate * env_ahd(zd, 0.002, 0.08, 0.05)
    elec = bp(noise(zd, 142), 2000, 9000) * gate * env_ahd(zd, 0.001, 0.1, 0.04)
    crack = sat(hp(noise(0.06, 143), 800) * env_exp(0.06, 0.012, 0.0002) * 3, 3)
    rumble_n = lp(noise(d, 144), 220, order=4)
    rumble = rumble_n * env_swell(d, 0.08, 1.0, 0.25) * (1 + 0.5 * lp(noise(d, 145), 8) * 20)
    boom = thump(90, 35, d, 0.15, 3)
    x = mix(d, (0, 0.5, stereo(zap, 0, 0.5, 0.3)), (0, 0.45, stereo(elec, 0, 0.9, 0.5)),
            (0.03, 0.9, stereo(crack, 0, 0.4)), (0.03, 1.4, stereo(rumble, 0, 0.5, 1.0)), (0.03, 0.7, boom))
    return reverb(x, 0.7, 0.18, damp=4000)


@sfx("skill", offset=-1.0, max_len=0.9)
def c_holy():
    # Holy: two bright bells a major sixth apart, airy shimmer and a soft glow swell.
    d = 1.0
    b1 = bell(midi(79), d, tau=0.55)
    b2 = bell(midi(83), d - 0.03, tau=0.45, bright=0.8)
    b3 = bell(midi(86), d - 0.06, tau=0.4, bright=0.6)
    glow = (sine(midi(67), d) + 0.5 * sine(midi(74) * 1.003, d)) * env_ahd(d, 0.08, 0.1, 0.3)
    air = hp(noise(d, 151), 7000) * env_swell(d, 0.1, 1.0, 0.3) * (0.6 + 0.4 * np.sin(2 * np.pi * 11 * tvec(d)))
    sp = sparkle(d, 10, 152, [midi(m) for m in (91, 95, 98, 103)], start=0.05, spread=0.5, tau=0.1)
    x = mix(d, (0, 0.45, stereo(b1, -0.2)), (0.03, 0.35, stereo(b2, 0.25)), (0.06, 0.25, stereo(b3, 0)),
            (0, 0.18, stereo(glow, 0, 0.4, 0.5)), (0, 0.12, stereo(air, 0, 0.8, 0.5)), (0, 0.15, sp))
    return reverb(x, 1.0, 0.3, damp=9000, size_hp=300)


@sfx("skill", offset=-2.0, max_len=0.9)
def heal():
    # Heal: soft rising major arpeggio of glassy mallets with an airy shimmer.
    d = 0.85
    voice = lambda f, dd: mallet(f, dd, bright=0.6, tau=0.35) + 0.3 * sine(f * 2, dd) * env_exp(dd, 0.15, 0.003)
    x = arp([72, 76, 79, 84], 0.065, d, voice, gains=[0.8, 0.85, 0.9, 1.0], pans=[-0.4, -0.1, 0.2, 0.4])
    air = whoosh(d, 2000, 7000, 0.25, 161, width=1.5, fall=0.2)
    sp = sparkle(d, 8, 162, [midi(m) for m in (91, 96, 100)], start=0.2, spread=0.35, tau=0.1)
    x = x * 0.5 + 0.12 * stereo(air, 0, 0.8, 0.5) + 0.15 * sp
    return reverb(x, 0.8, 0.3, damp=8000)


# ---------------------------------------------------------------------------------------------
# Sounds: world / gathering / building
# ---------------------------------------------------------------------------------------------
@sfx("world", max_len=0.35)
def chop():
    # Axe into wood: hollow woody resonances + bite click + low knock.
    d = 0.26
    exc = noise(d, 171) * env_exp(d, 0.006, attack=0.0003)
    wood = peak(exc, 320, 12) * 1.2 + peak(exc, 760, 14) * 0.8 + peak(exc, 1450, 16) * 0.5 + peak(exc, 2600, 10) * 0.3
    bite = hp(noise(0.015, 172), 2500) * env_exp(0.015, 0.003, 0.0002)
    knock = thump(160, 90, d, 0.03, 1.8)
    chips = grains(d, 6, 173, (1500, 4000), (0.003, 0.01), 1.5)
    x = mix(d, (0, 1.0, sat(wood * 3, 1.5)), (0, 0.5, bite), (0, 0.6, knock), (0.01, 0.2, stereo(chips, 0, 0.8, 0.4)))
    return reverb(x, 0.25, 0.12, damp=4000)


@sfx("world", max_len=0.4)
def mine():
    # Pickaxe on rock: bright metal clink + stone crunch + a few pebbles.
    d = 0.32
    clink = modal([2350, 3480, 5150, 6900], [1, 0.7, 0.45, 0.25], [0.09, 0.06, 0.04, 0.025], d, seed=181)
    clink *= env_exp(d, 10, 0.0005)
    crunch = bp(noise(d, 182), 800, 4500) * env_exp(d, 0.012, 0.0002)
    peb = grains(d, 10, 183, (1200, 5000), (0.003, 0.012), 1.6)
    knock = thump(240, 120, 0.08, 0.015, 1.5)
    x = mix(d, (0, 0.45, stereo(clink, 0, 0.3, 0.2)), (0, 0.8, sat(crunch * 2, 2)), (0.015, 0.3, stereo(peb, 0, 0.9, 0.4)),
            (0, 0.4, knock))
    return reverb(x, 0.3, 0.15, damp=6000)


@sfx("world", max_len=1.6)
def tree_fall():
    # Trunk creak (stick-slip pulses through wood resonances), leaf rush, then a heavy thud.
    d = 1.4
    cd = 0.75
    rate = 30 + 25 * np.sin(np.linspace(0, 2.6, n_of(cd))) + 10 * lp(noise(cd, 191), 5) * 10
    ph = np.cumsum(np.clip(rate, 8, 120)) / SR
    pulses = np.diff(np.floor(ph), prepend=0).astype(float)
    pulses = signal.lfilter([1], [1, -0.6], pulses)
    creak = peak(pulses, 420, 8) + 0.8 * peak(pulses, 950, 10) + 0.4 * peak(pulses, 1800, 9)
    creak *= env_ahd(cd, 0.08, 0.5, 0.1)
    rush = sweep_filter(noise(1.0, 192), sweep(800, 5000, 1.0, k=1.5), "band", 1.6) * env_swell(1.0, 0.85, 2.2, 0.08)
    thud = thump(85, 35, 0.55, 0.12, 3)
    thud_n = lp(noise(0.55, 193), 600) * env_exp(0.55, 0.06, 0.001)
    leaves = hp(noise(0.5, 194), 3000) * env_exp(0.5, 0.12, 0.003)
    x = mix(d, (0, 0.6, stereo(creak, 0.2, 0.3)), (0.15, 0.25, pan_curve(rush, 0.5, -0.4)),
            (0.88, 1.0, thud), (0.88, 0.6, stereo(thud_n, 0, 0.4)), (0.88, 0.2, stereo(leaves, 0, 1.0, 0.6)))
    return reverb(x, 0.6, 0.15, damp=4000)


@sfx("world", max_len=0.8)
def rock_break():
    # Rock crumbles: crack + crumble grains + low thump.
    d = 0.6
    crack = sat(bp(noise(0.04, 201), 600, 6000) * env_exp(0.04, 0.008, 0.0002) * 3, 2.5)
    crumble = grains(d, 60, 202, (300, 3000), (0.005, 0.03), 1.4)
    rumble = lp(noise(d, 203), 350) * env_exp(d, 0.12, 0.002)
    x = mix(d, (0, 0.8, stereo(crack, 0, 0.4)), (0.01, 0.55, stereo(crumble, 0, 0.9, 0.6)),
            (0, 0.6, stereo(rumble, 0, 0.3)), (0, 0.8, thump(140, 50, 0.3, 0.05, 2.5)))
    return reverb(x, 0.35, 0.15, damp=3500)


@sfx("world", offset=-1.0, max_len=0.35)
def pickup():
    # Item / coin pickup: bright two-note bling with a metallic edge.
    d = 0.28
    def coin(f, dd):
        return (fm(f, 3.5, 0.8 * np.exp(-tvec(dd) / 0.02), dd) * env_exp(dd, 0.08, 0.001)
                + 0.3 * sine(f * 2.0, dd) * env_exp(dd, 0.05, 0.001))
    x = mix(d, (0, 0.45, stereo(coin(midi(88), 0.07), -0.15)), (0.055, 0.55, stereo(coin(midi(95), d - 0.055), 0.15)))
    return reverb(x, 0.3, 0.18, damp=10000)


@sfx("world", offset=-1.0, max_len=0.35)
def pluck():
    # Pull a crop: a round upward "pop", leafy rustle and a soft soil thump.
    d = 0.26
    pop = sine(sweep(260, 820, 0.07, k=0.6), 0.07) * env_ahd(0.07, 0.002, 0.02, 0.02)
    rustle = bp(noise(0.12, 211), 2000, 7000) * env_swell(0.12, 0.02, 1, 0.03)
    soil = lp(noise(0.1, 212), 700) * env_exp(0.1, 0.02, 0.001)
    x = mix(d, (0.01, 0.7, pop), (0, 0.3, stereo(rustle, 0, 0.8, 0.4)), (0, 0.5, soil), (0, 0.3, thump(120, 70, 0.08, 0.02, 1.2)))
    return reverb(x, 0.25, 0.12)


@sfx("world", offset=-1.0, max_len=0.6)
def deliver():
    # Material delivered to a construction site: three warm wooden mallet notes up.
    d = 0.55
    voice = lambda f, dd: mallet(f, dd, bright=0.7, tau=0.18)
    x = arp([67, 72, 76], 0.075, d, voice, gains=[0.8, 0.9, 1.0], pans=[-0.3, 0, 0.3])
    return reverb(x * 0.6, 0.4, 0.2)


@sfx("world", max_len=0.35)
def hammer():
    # Hammer on a nail / plank: metal ping + wood knock.
    d = 0.26
    ping = modal([2650, 4100, 6200], [1, 0.5, 0.3], [0.06, 0.04, 0.025], d, seed=221) * env_exp(d, 10, 0.0005)
    exc = noise(d, 222) * env_exp(d, 0.005, 0.0002)
    wood = peak(exc, 520, 10) + 0.6 * peak(exc, 1200, 12)
    x = mix(d, (0, 0.4, stereo(ping, 0, 0.3)), (0, 0.9, sat(wood * 2.5, 1.5)), (0, 0.5, thump(200, 110, 0.07, 0.015, 1.5)))
    return reverb(x, 0.3, 0.15, damp=5000)


@sfx("world", max_len=1.5)
def door_open():
    # Heavy dungeon door: latch clunk, long creak with a pitch glide, stone-floor thud at the end.
    d = 1.3
    latch = thump(300, 120, 0.06, 0.012, 2) + 0.5 * bp(noise(0.06, 231), 1500, 5000) * env_exp(0.06, 0.006)
    cd = 0.95
    rate = 70 + 60 * np.sin(np.linspace(-0.3, 2.4, n_of(cd))) + 4 * lp(noise(cd, 232), 6) * 10
    ph = np.cumsum(np.clip(rate, 15, 200)) / SR
    pulses = np.diff(np.floor(ph), prepend=0).astype(float)
    pulses = signal.lfilter([1], [1, -0.5], pulses)
    creak = peak(pulses, 380, 9) + 0.9 * peak(pulses, 870, 11) + 0.5 * peak(pulses, 1650, 10) + 0.25 * peak(pulses, 2900, 8)
    creak *= env_ahd(cd, 0.06, 0.75, 0.08)
    air = lp(noise(cd, 233), 900) * env_ahd(cd, 0.2, 0.5, 0.15)
    thud = thump(95, 45, 0.3, 0.07, 2.5)
    x = mix(d, (0, 0.6, latch), (0.06, 0.65, stereo(creak, -0.1, 0.3)), (0.06, 0.25, stereo(air, 0, 0.6, 0.5)),
            (1.0, 0.85, thud))
    return reverb(x, 0.9, 0.22, damp=3500)


# ---------------------------------------------------------------------------------------------
# Sounds: UI
# ---------------------------------------------------------------------------------------------
@sfx("ui", offset=-2.0, max_len=0.08)
def blip():
    # Dialogue text blip: tiny round tone.
    d = 0.045
    x = sine(920, d) * env_exp(d, 0.012, 0.001) + 0.2 * sine(1840, d) * env_exp(d, 0.006, 0.001)
    return stereo(x)


@sfx("ui", max_len=0.09)
def select():
    # Cursor move / button hover: soft wooden tick.
    d = 0.06
    exc = noise(d, 241) * env_exp(d, 0.002, 0.0002)
    tick = peak(exc, 1900, 12) * 1.5 + peak(exc, 3800, 10) * 0.5
    tone = sine(1250, d) * env_exp(d, 0.012, 0.001)
    return stereo(tick * 0.6 + tone * 0.6)


@sfx("ui", offset=1.0, max_len=0.25)
def confirm():
    # Bright two-note up (glassy mallet).
    d = 0.24
    voice = lambda f, dd: mallet(f, dd, bright=0.8, tau=0.07)
    x = arp([84, 91], 0.06, d, voice, gains=[0.8, 1.0], pans=[-0.1, 0.1])
    return reverb(x, 0.2, 0.12, damp=9000)


@sfx("ui", max_len=0.25)
def cancel():
    # Two-note down, rounder and duller than confirm.
    d = 0.22
    voice = lambda f, dd: lp(mallet(f, dd, bright=0.3, tau=0.06), 3000)
    x = arp([76, 69], 0.065, d, voice, gains=[1.0, 0.85], pans=[0.1, -0.1])
    return reverb(x, 0.15, 0.08)


@sfx("ui", offset=2.5, max_len=0.3)
def card_flip():
    # Card flick: paper snap + short airy thwip.
    d = 0.2
    snap = bp(noise(0.012, 251), 1800, 7000) * env_exp(0.012, 0.0025, 0.0002)
    thwip = whoosh(0.1, 1200, 5500, 0.05, 252, width=1.2, curve_k=0.7, fall=0.015)
    flap = bp(noise(0.03, 253), 600, 2500) * env_exp(0.03, 0.006, 0.0005)
    x = mix(d, (0, 0.7, stereo(snap, -0.2)), (0.01, 0.5, pan_curve(thwip, -0.4, 0.4)), (0.085, 0.45, stereo(flap, 0.2)))
    return reverb(x, 0.15, 0.08)


# ---------------------------------------------------------------------------------------------
# Sounds: jingles
# ---------------------------------------------------------------------------------------------
@sfx("jingle", offset=-1.5, max_len=1.5)
def player_down():
    # Player knocked out: soft fall thud, then a sad descending three-note phrase.
    d = 1.3
    voice = lambda f, dd: lp(square(f, dd), 1800) * env_ahd(dd, 0.01, 0.12, 0.12) * 0.5 + mallet(f, dd, 0.3, 0.3) * 0.5
    x = arp([71, 67, 64], 0.2, d, voice, gains=[0.8, 0.8, 0.9])
    last = lp(square(sweep(midi(60), midi(55), 0.5, k=1.5), 0.5), 1500) * env_ahd(0.5, 0.01, 0.2, 0.15) * 0.45
    x = x + at(d, 0.6, last) + 0.6 * at(d, 0, thump(120, 45, 0.3, 0.06, 2))
    return reverb(x, 0.7, 0.22)


@sfx("jingle", max_len=1.8)
def build_complete():
    # Building finished: hammer tap, rising major arpeggio, sparkly bell chord.
    d = 1.5
    voice = lambda f, dd: mallet(f, dd, 0.8, 0.3)
    x = arp([72, 76, 79, 84], 0.085, d, voice, gains=[0.7, 0.75, 0.8, 0.9], pans=[-0.4, -0.15, 0.15, 0.4])
    chord = sum(stereo(bell(midi(m), d - 0.36, tau=0.5, bright=0.7), p) for m, p in ((79, -0.3), (84, 0.0), (88, 0.3)))
    sp = sparkle(d, 10, 261, [midi(m) for m in (96, 100, 103, 108)], start=0.35, spread=0.6, tau=0.1)
    x = 0.55 * x + 0.22 * at(d, 0.36, chord) + 0.12 * sp + 0.3 * at(d, 0, stereo(hammer()[0][:n_of(0.2)]))
    return reverb(x, 0.9, 0.25)


@sfx("jingle", offset=-1.0, max_len=1.2)
def enhance_charge():
    # Enhancement in progress: rising tremolo FM tone + noise riser, ends at the peak.
    d = 1.0
    f = sweep(200, 1300, d, k=1.4)
    idx = np.linspace(0.5, 3.0, n_of(d))
    tone = fm(f, 2.0, idx, d)
    trem = 0.6 + 0.4 * np.sin(2 * np.pi * np.cumsum(sweep(6, 28, d, curve="lin")) / SR)
    tone *= trem * np.linspace(0.2, 1, n_of(d)) ** 1.5
    tone = lp(tone, 5000)
    rise = sweep_filter(noise(d, 271), sweep(300, 6000, d, k=1.5), "band", 1.2) * np.linspace(0, 1, n_of(d)) ** 2.5
    sp = sparkle(d, 10, 272, [midi(m) for m in (88, 91, 95, 100)], start=0.5, spread=0.45, tau=0.05)
    x = stereo(tone, 0, 0.3, 0.3) * 0.4 + 0.5 * stereo(rise, 0, 0.8, 0.6) + 0.15 * sp
    x[:, -n_of(0.02):] *= np.linspace(1, 0, n_of(0.02))
    return reverb(x, 0.3, 0.15)


@sfx("jingle", max_len=1.4)
def enhance_success():
    # Success: bright ding + quick G major arpeggio + sparkle.
    d = 1.2
    ding = bell(midi(91), d, tau=0.5)
    voice = lambda f, dd: mallet(f, dd, 0.9, 0.25)
    x = arp([79, 83, 86, 91], 0.06, d, voice, gains=[0.6, 0.7, 0.8, 0.9], pans=[-0.3, -0.1, 0.1, 0.3])
    sp = sparkle(d, 12, 281, [midi(m) for m in (95, 98, 103, 107)], start=0.2, spread=0.5, tau=0.08)
    x = 0.5 * x + 0.3 * at(d, 0.24, stereo(ding, 0, 0.3, 0.3)) + 0.15 * sp + 0.25 * at(d, 0, thump(180, 70, 0.15, 0.03, 1.5))
    return reverb(x, 0.9, 0.28, damp=9000)


@sfx("jingle", offset=2.0, max_len=2.4)
def enhance_great():
    # Great success: impact, two-octave C major run, big held bell chord, cascading sparkles.
    d = 2.1
    boom = thump(140, 40, 0.6, 0.12, 3)
    crash = hp(noise(1.5, 291), 4000) * env_exp(1.5, 0.35, 0.002)
    voice = lambda f, dd: mallet(f, dd, 0.9, 0.3)
    run = arp([72, 76, 79, 84, 88, 91, 96], 0.055, d, voice, gains=[0.6, 0.65, 0.7, 0.75, 0.8, 0.85, 0.95],
              pans=[-0.6, -0.4, -0.2, 0, 0.2, 0.4, 0.6])
    t_ch = 0.4
    chord = np.zeros((2, n_of(d - t_ch)))
    for m, p in ((72, -0.5), (76, -0.2), (79, 0.2), (84, 0.5), (88, 0.0)):
        chord += stereo(bell(midi(m), d - t_ch, tau=0.8, bright=0.7), p)
    pad = sum(stereo(saw(midi(m) * (1 + det), d - t_ch, top=6000), p)
              for m, det, p in ((60, 0.0, -0.4), (64, 0.003, 0.4), (67, -0.003, 0.0)))
    pad = np.stack([lp(ch, 2500) for ch in pad]) * env_ahd(d - t_ch, 0.06, 0.5, 0.4)
    sp = sparkle(d, 30, 292, [midi(m) for m in (96, 98, 100, 103, 105, 108)], start=0.35, spread=1.2, tau=0.1)
    x = (0.8 * at(d, 0, boom) + 0.09 * at(d, 0.38, stereo(lp(crash, 9000), 0, 1.0, 0.7)) + 0.45 * run
         + 0.2 * at(d, t_ch, chord) + 0.12 * at(d, t_ch, pad) + 0.16 * sp)
    return reverb(x, 1.4, 0.3, damp=9000)


@sfx("jingle", offset=-2.0, max_len=1.0)
def enhance_fail():
    # Failed: deflating "wah-wah" (two muted notes, falling), soft puff. Sad but short.
    d = 0.8
    def wah(f0, f1, dd):
        f = sweep(f0, f1, dd, k=1.6)
        x = saw(f, top=5000)
        cut = 400 + 1600 * env_ahd(dd, 0.04, 0.05, 0.12)
        return sweep_filter(x, cut, "low") * env_ahd(dd, 0.015, 0.12, 0.08)
    x = mix(d, (0, 0.5, wah(midi(67), midi(66), 0.25)), (0.24, 0.55, wah(midi(63), midi(58), 0.45)))
    puff = lp(noise(0.3, 301), 1200) * env_swell(0.3, 0.03, 1, 0.07)
    x += 0.3 * at(d, 0.0, stereo(puff, 0, 0.5, 0.4))
    return reverb(x, 0.4, 0.15)


@sfx("jingle", offset=0.0, max_len=1.3)
def enhance_break():
    # Item destroyed: glass/metal shatter, low thud, falling minor sigh.
    d = 1.1
    crack = sat(hp(noise(0.05, 311), 1500) * env_exp(0.05, 0.01, 0.0002) * 3, 3)
    r = rng(312)
    shards = np.zeros((2, n_of(d)))
    for i in range(24):
        t0 = 0.005 + 0.35 * r.uniform(0, 1) ** 1.5
        f = r.uniform(1800, 6500)
        ln = d - t0
        s = modal([f, f * 1.53, f * 2.21], [1, 0.5, 0.25], [r.uniform(0.03, 0.12), 0.04, 0.02], ln, seed=313 + i)
        shards += r.uniform(0.2, 1.0) * at(d, t0, stereo(s * env_exp(ln, 10, 0.0005), r.uniform(-0.9, 0.9)))
    tinkle = grains(d, 30, 340, (3000, 8000), (0.003, 0.01), 1.2)
    thud = thump(110, 40, 0.5, 0.09, 3)
    sigh = lp(square(sweep(midi(64), midi(52), 0.6, k=1.3), 0.6), 1400) * env_ahd(0.6, 0.02, 0.25, 0.12)
    x = (0.9 * at(d, 0, stereo(crack, 0, 0.5)) + 0.15 * shards + 0.25 * at(d, 0.03, stereo(tinkle, 0, 1.0, 0.5))
         + 0.8 * at(d, 0, thud) + 0.3 * at(d, 0.3, sigh))
    return reverb(x, 0.7, 0.22, damp=7000)


@sfx("jingle", max_len=2.4)
def dungeon_clear():
    # Dungeon cleared: brass fanfare C-C-E-G | C' held, timpani hits, cymbal swell, bells.
    d = 2.2
    notes = [(0.0, 72, 0.12), (0.13, 72, 0.12), (0.26, 76, 0.12), (0.39, 79, 0.14), (0.55, 84, 1.0)]
    x = np.zeros((2, n_of(d)))
    for t0, m, ln in notes:
        v = brass(midi(m), ln + 0.25, attack=0.02, tau=0.3 if ln < 0.5 else 0.6)
        v2 = brass(midi(m - 12) * 1.002, ln + 0.25, attack=0.02, tau=0.3 if ln < 0.5 else 0.6)
        x += at(d, t0, stereo(v, -0.2) * 0.5 + stereo(v2, 0.2) * 0.35)
    for m in (64, 67):  # harmony on the held note
        x += 0.3 * at(d, 0.55, stereo(brass(midi(m), 1.2, 0.03, 0.6), 0.4 if m == 64 else -0.4))
    timp = lambda: thump(110, 70, 0.6, 0.15, 1.5)
    x += 0.5 * at(d, 0.0, timp()) + 0.5 * at(d, 0.39, timp()) + 0.7 * at(d, 0.55, timp())
    cym = hp(noise(1.6, 321), 5000) * env_ahd(1.6, 0.02, 0.1, 0.45)
    x += 0.12 * at(d, 0.55, stereo(cym, 0, 1.0, 0.6))
    x += 0.1 * at(d, 0.55, stereo(bell(midi(96), 1.2, 0.5), 0.3)) + 0.12 * sparkle(d, 15, 322, [midi(m) for m in (96, 100, 103)], 0.6, 0.9)
    return reverb(sat(x * 0.8, 1.2), 1.1, 0.25)


@sfx("jingle", max_len=1.9)
def rank_reveal():
    # Rank / big reward reveal: snare-roll riser and rising tone, then a bright hit + chord.
    d = 1.6
    hit_t = 0.85
    roll_rate = sweep(14, 34, hit_t, curve="lin")
    roll_ph = np.cumsum(roll_rate) / SR
    roll_imp = np.diff(np.floor(roll_ph), prepend=0)
    snare = np.zeros(n_of(hit_t))
    idx = np.nonzero(roll_imp)[0]
    hitlen = n_of(0.03)
    burst = bp(noise(0.03, 332), 1500, 8000) * env_exp(0.03, 0.008, 0.0005)
    for i in idx:
        e = min(len(snare), i + hitlen)
        snare[i:e] += burst[:e - i] * (0.3 + 0.7 * i / len(snare))
    rise_tone = fm(sweep(220, 880, hit_t, k=1.5), 2.0, 1.0, hit_t) * np.linspace(0, 1, n_of(hit_t)) ** 2
    rise_tone = lp(rise_tone, 4000)
    boom = thump(160, 40, 0.6, 0.12, 3)
    crash = hp(noise(0.8, 333), 3500) * env_exp(0.8, 0.25, 0.002)
    chord = sum(stereo(brass(midi(m), d - hit_t, 0.01, 0.4, (900, 5000)), p) for m, p in ((72, -0.4), (76, 0.4), (79, 0), (84, 0)))
    bl = bell(midi(96), d - hit_t, 0.5)
    x = (0.45 * at(d, 0, stereo(snare, 0, 0.6, 0.3)) + 0.2 * at(d, 0, stereo(rise_tone, 0, 0.3))
         + 0.9 * at(d, hit_t, boom) + 0.25 * at(d, hit_t, stereo(crash, 0, 1.0, 0.6))
         + 0.22 * at(d, hit_t, chord) + 0.15 * at(d, hit_t, stereo(bl, 0, 0.3)))
    return reverb(x, 1.0, 0.22)


@sfx("jingle", offset=0.0, max_len=1.4)
def quest():
    # Quest / reward complete: bright E major bell arpeggio, sparkle.
    d = 1.2
    voice = lambda f, dd: bell(f, dd, tau=0.4, bright=0.8) * 0.6 + mallet(f, dd, 0.6, 0.25) * 0.5
    x = arp([76, 80, 83, 88], 0.075, d, voice, gains=[0.7, 0.75, 0.8, 1.0], pans=[-0.35, -0.1, 0.1, 0.35])
    sp = sparkle(d, 10, 341, [midi(m) for m in (95, 100, 104)], start=0.25, spread=0.5, tau=0.08)
    return reverb(0.5 * x + 0.13 * sp, 0.9, 0.25, damp=9000)


@sfx("jingle", max_len=2.5)
def ending():
    # Ending: celebratory melody over warm chords, bells and a final shimmer.
    d = 2.7
    step = 0.16
    mel = [72, 76, 79, 76, 79, 84, 83, 84]
    voice = lambda f, dd: bell(f, dd, tau=0.45, bright=0.7) * 0.5 + mallet(f, dd, 0.5, 0.3) * 0.5
    x = arp(mel, step, d, voice, gains=[0.7] * 7 + [1.0], pans=[-0.2, 0.2] * 4)
    pads = [(0.0, (60, 64, 67)), (0.64, (65, 69, 72)), (0.96, (67, 71, 74)), (1.12, (60, 64, 67, 72))]
    for i, (t0, ch) in enumerate(pads):
        ln = (pads[i + 1][0] if i + 1 < len(pads) else d) - t0 + 0.2
        ln = min(ln, d - t0)
        p = sum(stereo(saw(midi(m) * (1 + 0.002 * (j - 1)), ln, top=5000), (j - 1.5) * 0.4) for j, m in enumerate(ch))
        p = np.stack([lp(c, 1800) for c in p]) * env_ahd(ln, 0.05, ln - 0.2 if ln > 0.4 else 0.1, 0.25 if i < 3 else 0.6)
        x += 0.09 * at(d, t0, p)
    x += 0.15 * sparkle(d, 20, 351, [midi(m) for m in (96, 100, 103, 108)], 1.12, 1.0)
    x += 0.35 * at(d, 1.12, thump(110, 50, 0.4, 0.1, 1.5))
    return reverb(x, 1.3, 0.3)


# ---------------------------------------------------------------------------------------------
# Mastering and verification
# ---------------------------------------------------------------------------------------------
K1 = ([1.53512485958697, -2.69169618940638, 1.19839281085285], [1.0, -1.69065929318241, 0.73248077421585])
K2 = ([1.0, -2.0, 1.0], [1.0, -1.99004745483398, 0.99007225036621])


def kloud_max(x, win=0.1):
    """Max short-window K-weighted loudness (LUFS scale)."""
    pw = np.zeros(x.shape[1])
    for ch in x:
        y = signal.lfilter(*K2, signal.lfilter(*K1, ch))
        pw += y ** 2
    w = n_of(win)
    if x.shape[1] < w:
        pw = np.pad(pw, (0, w - x.shape[1]))
    ms = uniform_filter1d(pw, w)
    return -0.691 + 10 * np.log10(np.max(ms) + 1e-12)


def db(v):
    return 20 * np.log10(max(v, 1e-12))


def limit(x, ceil_db):
    """Lookahead peak limiter (1 ms lookahead, 40 ms release)."""
    thr = 10 ** (ceil_db / 20)
    a = np.max(np.abs(x), axis=0)
    la = n_of(0.001)
    env = maximum_filter1d(a, size=2 * la + 1)
    g = np.minimum(1.0, thr / np.maximum(env, 1e-9))
    # Release smoothing: gain may drop instantly, recovers with a one-pole.
    rel = np.exp(-1 / (0.04 * SR))
    gs = g.copy()
    for i in range(1, len(gs)):
        gs[i] = min(g[i], rel * gs[i - 1] + (1 - rel) * g[i])
    gs = uniform_filter1d(gs, la)
    y = x * gs
    p = np.max(np.abs(y))
    if p > thr:
        y *= thr / p
    return y


def finish(x, max_len=None):
    if x.ndim == 1:
        x = stereo(x)
    # DC / subsonic removal.
    x = np.stack([hp(ch, 25, order=2) for ch in x])
    x = x - x.mean(axis=1, keepdims=True)
    # Trim the tail below -66 dBFS, keep at least 20 ms.
    a = np.max(np.abs(x), axis=0)
    above = np.nonzero(a > 10 ** (-66 / 20) * np.max(a))[0]
    end = min(x.shape[1], (above[-1] + n_of(0.01)) if len(above) else x.shape[1])
    cut = bool(max_len) and end > n_of(max_len)
    if cut:
        end = n_of(max_len)
    x = x[:, :max(end, n_of(0.02))]
    if cut:  # tail was truncated: long equal-power fade so the reverb dies naturally
        nf = min(n_of(0.15), x.shape[1] // 4)
        x[:, -nf:] *= np.cos(np.linspace(0, np.pi / 2, nf)) ** 2
    return x


def fades(x, fin=0.001, fout=0.008):
    n_in, n_out = n_of(fin), min(n_of(fout), x.shape[1] // 3)
    x = x.copy()
    x[:, :n_in] *= (0.5 - 0.5 * np.cos(np.linspace(0, np.pi, n_in)))
    x[:, -n_out:] *= (0.5 + 0.5 * np.cos(np.linspace(0, np.pi, n_out)))
    return x


def master(key):
    info = REGISTRY[key]
    target = CATEGORIES[info["cat"]] + info["offset"]
    x = finish(np.asarray(info["fn"](), dtype=np.float64), info["max_len"])
    x = fades(x)
    for _ in range(6):
        cur = kloud_max(x)
        x = x * 10 ** ((target - cur) / 20)
        x = limit(x, PEAK_CEIL_DB)
        if abs(kloud_max(x) - target) < 0.2:
            break
    x = x - x.mean(axis=1, keepdims=True)
    x = fades(x)
    return x, target


def longest_silence(x, thr_db=-60.0, frame=0.01):
    a = np.max(np.abs(x), axis=0)
    f = n_of(frame)
    nfr = len(a) // f
    if nfr == 0:
        return 0.0
    rms = np.sqrt(np.mean((a[:nfr * f].reshape(nfr, f)) ** 2, axis=1))
    loud = 20 * np.log10(rms + 1e-12) > thr_db
    idx = np.nonzero(loud)[0]
    if len(idx) == 0:
        return len(a) / SR
    inner = ~loud[idx[0]:idx[-1] + 1]
    best = run = 0
    for s in inner:
        run = run + 1 if s else 0
        best = max(best, run)
    return best * frame


def ffmpeg_integrated(path):
    r = subprocess.run(["ffmpeg", "-hide_banner", "-nostats", "-i", path, "-af", "ebur128", "-f", "null", "-"],
                       capture_output=True, text=True)
    val = None
    for line in r.stderr.splitlines():
        line = line.strip()
        if line.startswith("I:") and "LUFS" in line:
            val = float(line.split()[1])
    return val


def decode_peak(path):
    r = subprocess.run(["ffmpeg", "-hide_banner", "-v", "error", "-i", path, "-f", "f32le", "-ac", "2", "-ar", str(SR), "-"],
                       capture_output=True)
    y = np.frombuffer(r.stdout, dtype=np.float32)
    return db(float(np.max(np.abs(y)))) if len(y) else None


def verify(key, x, target):
    info = REGISTRY[key]
    dur = x.shape[1] / SR
    pk = db(float(np.max(np.abs(x))))
    dc = float(np.max(np.abs(x.mean(axis=1))))
    head = float(np.max(np.abs(x[:, :n_of(0.0002)])))
    tail = float(np.max(np.abs(x[:, -n_of(0.002):])))
    first, last = float(np.max(np.abs(x[:, 0]))), float(np.max(np.abs(x[:, -1])))
    loud = kloud_max(x)
    sil = longest_silence(x)
    lo, hi = CATEGORIES[info["cat"]] - CAT_RANGE, CATEGORIES[info["cat"]] + CAT_RANGE
    checks = {
        "peak": pk <= -1.0,
        "dc": dc < 0.001,
        "clicks": first < 1e-3 and last < 1e-3 and head < 0.05 and tail < 10 ** (-30 / 20),
        "loud": abs(loud - target) <= TOLERANCE and lo <= target <= hi,
        "silence": sil <= 0.15 if dur < 1.5 else sil <= 0.3,
        "length": DUR_KEY.get(key, DUR_RANGE[info["cat"]])[0] - 1e-6 <= dur <= DUR_KEY.get(key, DUR_RANGE[info["cat"]])[1] + 1e-6,
    }
    return dict(dur=dur, peak=pk, dc=dc, head=head, tail=db(tail), loud=loud, target=target, sil=sil, checks=checks)


def write_wav(path, x):
    wavfile.write(path, SR, (np.clip(x.T, -1, 1) * 32767).astype(np.int16))


def write_meta(ogg_path):
    meta = ogg_path + ".meta"
    if os.path.exists(meta):
        return False
    rel = os.path.relpath(ogg_path, ROOT).replace(os.sep, "/")
    guid = hashlib.md5(("dotRPG:" + rel).encode()).hexdigest()
    body = (f"fileFormatVersion: 2\nguid: {guid}\nAudioImporter:\n  externalObjects: {{}}\n  serializedVersion: 8\n"
            "  defaultSettings:\n    serializedVersion: 2\n    loadType: 0\n    sampleRateSetting: 0\n"
            "    sampleRateOverride: 44100\n    compressionFormat: 1\n    quality: 0.7\n    conversionMode: 0\n"
            "    preloadAudioData: 1\n  platformSettingOverrides: {}\n  forceToMono: 0\n  normalize: 0\n"
            "  loadInBackground: 0\n  ambisonic: 0\n  3D: 1\n  userData: \n  assetBundleName: \n  assetBundleVariant: \n")
    with open(meta, "w", newline="\n") as f:
        f.write(body)
    return True


def preview(masters):
    gap = np.zeros((2, n_of(0.6)))
    sep_t = tvec(0.09)
    sep = 0.03 * np.sin(2 * np.pi * 1000 * sep_t) * np.hanning(len(sep_t))
    parts = []
    for cat in ORDER:
        keys = [k for k in REGISTRY if REGISTRY[k]["cat"] == cat and k in masters]
        if not keys:
            continue
        parts += [stereo(sep) / np.sqrt(2), np.zeros((2, n_of(0.3))), stereo(sep) / np.sqrt(2), gap]
        for k in keys:
            parts += [masters[k], gap]
    return np.concatenate(parts, axis=1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--only", default="")
    ap.add_argument("--no-ogg", action="store_true")
    ap.add_argument("--replace", action="store_true", help="allow replacing an existing <key>.ogg")
    args = ap.parse_args()
    keys = [k for k in REGISTRY if not args.only or k in args.only.split(",")]
    os.makedirs(WAV_DIR, exist_ok=True)
    os.makedirs(OGG_DIR, exist_ok=True)

    existing = [k for k in keys if os.path.exists(os.path.join(OGG_DIR, k + ".ogg"))]
    if existing and not args.replace and not args.no_ogg:
        print("Existing sfx ogg found (rerun with --replace to overwrite):", ", ".join(existing))
        sys.exit(2)

    masters, results = {}, {}
    for k in keys:
        x, target = master(k)
        masters[k] = x
        path = os.path.join(WAV_DIR, k + ".wav")
        write_wav(path, x)
        results[k] = verify(k, x, target)
        results[k]["ebur128_I"] = ffmpeg_integrated(path) if results[k]["dur"] >= 0.4 else None
        if not args.no_ogg:
            ogg = os.path.join(OGG_DIR, k + ".ogg")
            subprocess.run(["ffmpeg", "-hide_banner", "-v", "error", "-y", "-i", path, "-c:a", "libvorbis", "-q:a", "6", ogg],
                           check=True)
            results[k]["ogg_peak"] = decode_peak(ogg)
            results[k]["checks"]["ogg_peak"] = results[k]["ogg_peak"] is not None and results[k]["ogg_peak"] <= -1.0
            results[k]["meta_new"] = write_meta(ogg)
        print(f"{k:16s} {results[k]['dur']*1000:6.0f} ms  loud {results[k]['loud']:6.1f}  "
              f"peak {results[k]['peak']:5.1f}  {'OK' if all(results[k]['checks'].values()) else 'FAIL ' + str([c for c, v in results[k]['checks'].items() if not v])}")

    if not args.only:
        write_wav(os.path.join(WAV_DIR, "_preview_all.wav"), preview(masters))

    lines = ["dotRPG SFX verification report (Tools/sfx/make_sfx.py)",
             "loud = max 100 ms K-weighted loudness (LUFS scale); ebur128 I = ffmpeg integrated (files >= 400 ms only)",
             "category targets: " + ", ".join(f"{c} {v:+.1f}" for c, v in CATEGORIES.items())
             + f"; per-key offset within +-{CAT_RANGE} dB, measured within +-{TOLERANCE} dB",
             "checks: peak <= -1.0 dBFS (wav and decoded ogg), |DC| < 0.001, first/last sample < 1e-3 with 1 ms in / 8 ms out ramps,"
             " inner silence (< -60 dBFS, 10 ms frames) <= 150 ms",
             "duration ranges: " + ", ".join(f"{c} {a*1000:.0f}-{b*1000:.0f} ms" for c, (a, b) in DUR_RANGE.items())
             + "; blip/select 30-80 ms", ""]
    hdr = f"{'key':16s} {'cat':7s} {'dur_ms':>6s} {'peak':>6s} {'oggpk':>6s} {'dc':>8s} {'tail_db':>7s} {'loud':>6s} {'target':>6s} {'ebur_I':>6s} {'sil_ms':>6s}  result"
    lines.append(hdr)
    fails = 0
    for cat in ORDER:
        for k in keys:
            if REGISTRY[k]["cat"] != cat:
                continue
            r = results[k]
            ok = all(r["checks"].values())
            fails += not ok
            ei = f"{r['ebur128_I']:6.1f}" if r.get("ebur128_I") is not None else "   n/a"
            op = f"{r['ogg_peak']:6.1f}" if r.get("ogg_peak") is not None else "   n/a"
            lines.append(f"{k:16s} {cat:7s} {r['dur']*1000:6.0f} {r['peak']:6.1f} {op} {r['dc']:8.5f} {r['tail']:7.1f} "
                         f"{r['loud']:6.1f} {r['target']:6.1f} {ei} {r['sil']*1000:6.0f}  "
                         + ("PASS" if ok else "FAIL " + ",".join(c for c, v in r["checks"].items() if not v)))
    lines += ["", f"{len(keys) - fails}/{len(keys)} pass"]
    for cat in ORDER:
        vals = [results[k]["loud"] for k in keys if REGISTRY[k]["cat"] == cat]
        if vals:
            lines.append(f"  {cat:7s} loud mean {np.mean(vals):6.1f}  range {min(vals):6.1f}..{max(vals):6.1f}  (n={len(vals)})")
    if not args.only:
        with open(os.path.join(WAV_DIR, "report.txt"), "w") as f:
            f.write("\n".join(lines) + "\n")
    print("\n".join(lines[-7:]))


if __name__ == "__main__":
    main()
