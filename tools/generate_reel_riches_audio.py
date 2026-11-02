"""Regenerate compact, original Reel Riches bonus music and effects.

Run with Python plus NumPy. All sounds are rendered offline; the browser only
loads audio files when Reel Riches is visited. The 24-second music loop is
intentionally a *bonus-only* arrangement, not the base-game ambience.
"""

from __future__ import annotations

import math
import wave
from pathlib import Path

import numpy as np

RATE = 24_000
TAU = math.tau
OUTPUT = Path(__file__).parents[1] / "fortuneforge.client" / "src" / "assets" / "slots" / "audio"
RNG = np.random.default_rng(20261031)


def canvas(seconds: float) -> np.ndarray:
    return np.zeros((round(seconds * RATE), 2), dtype=np.float32)


def add(track: np.ndarray, start: float, signal: np.ndarray, pan: float = 0.0) -> None:
    first = round(start * RATE)
    if first >= len(track):
        return
    if first < 0:
        signal = signal[-first:]
        first = 0
    signal = signal[: len(track) - first]
    if not len(signal):
        return
    left = math.cos((pan + 1) * math.pi / 4)
    right = math.sin((pan + 1) * math.pi / 4)
    track[first:first + len(signal), 0] += signal * left
    track[first:first + len(signal), 1] += signal * right


def note(frequency: float, seconds: float, kind: str = "bell") -> np.ndarray:
    time = np.arange(round(seconds * RATE), dtype=np.float32) / RATE
    if kind == "pad":
        env = np.minimum(1, time / 0.34) * np.minimum(1, (seconds - time) / 0.45)
        wave_body = (
            np.sin(TAU * frequency * time)
            + 0.19 * np.sin(TAU * frequency * 1.004 * time + 0.45)
            + 0.25 * np.sin(TAU * frequency * 2.002 * time)
            + 0.1 * np.sin(TAU * frequency * 3.007 * time + 1.1)
        )
        return (env * wave_body).astype(np.float32)
    if kind == "bass":
        env = (1 - np.exp(-time * 37)) * np.exp(-time * 2.2)
        wave_body = np.sin(TAU * frequency * time) + 0.24 * np.sin(TAU * frequency * 2 * time)
        return (env * wave_body).astype(np.float32)
    if kind == "brass":
        env = np.minimum(1, time / 0.055) * np.minimum(1, (seconds - time) / 0.22)
        wave_body = sum(
            (0.76 ** harmonic) * np.sin(TAU * frequency * harmonic * time)
            for harmonic in range(1, 6)
        )
        return (env * wave_body * (0.76 + 0.24 * np.sin(TAU * 4.4 * time))).astype(np.float32)
    # Inharmonic partials give a glassy fishing-trophy shimmer instead of a
    # bare sine-wave computer beep.
    env = (1 - np.exp(-time * 95)) * np.exp(-time * 3.8)
    wave_body = (
        np.sin(TAU * frequency * time)
        + 0.46 * np.sin(TAU * frequency * 2.003 * time + 0.3)
        + 0.2 * np.sin(TAU * frequency * 3.91 * time + 0.9)
    )
    return (env * wave_body).astype(np.float32)


def wash(seconds: float, amount: float = 1.0) -> np.ndarray:
    count = round(seconds * RATE)
    noise = RNG.standard_normal(count).astype(np.float32)
    low = np.convolve(noise, np.ones(47, np.float32) / 47, mode="same")
    return (amount * low).astype(np.float32)


def kick() -> np.ndarray:
    time = np.arange(round(0.43 * RATE), dtype=np.float32) / RATE
    phase = TAU * (56 * time + 0.11 * (1 - np.exp(-time * 28)))
    body = np.sin(phase) * np.exp(-time * 17)
    click = wash(0.43) * np.exp(-time * 60)
    return (0.82 * body + 0.14 * click).astype(np.float32)


def snare() -> np.ndarray:
    time = np.arange(round(0.26 * RATE), dtype=np.float32) / RATE
    noise = RNG.standard_normal(len(time)).astype(np.float32)
    high = noise - np.convolve(noise, np.ones(17, np.float32) / 17, mode="same")
    return (0.31 * high * np.exp(-time * 21) + 0.14 * np.sin(TAU * 163 * time) * np.exp(-time * 30)).astype(np.float32)


def tick() -> np.ndarray:
    time = np.arange(round(0.12 * RATE), dtype=np.float32) / RATE
    noise = RNG.standard_normal(len(time)).astype(np.float32)
    high = noise - np.convolve(noise, np.ones(9, np.float32) / 9, mode="same")
    return (0.11 * high * np.exp(-time * 38)).astype(np.float32)


def echo(track: np.ndarray, taps: tuple[tuple[float, float], ...]) -> None:
    dry = track.copy()
    for seconds, gain in taps:
        shift = round(seconds * RATE)
        track[shift:] += dry[:-shift] * gain


def master(track: np.ndarray, looping: bool = False) -> np.ndarray:
    if looping:
        # A brief taper and dominant resolution make the final A chord lead
        # cleanly into the next loop's D minor without a digital seam.
        fade = round(0.035 * RATE)
        shape = np.sin(np.linspace(0, math.pi / 2, fade, dtype=np.float32)) ** 2
        track[:fade] *= shape[:, None]
        track[-fade:] *= shape[::-1, None]
    else:
        # Keep percussive one-shots crisp without a pop when the Audio element
        # begins or releases at a nonzero sample.
        fade = round(0.004 * RATE)
        shape = np.sin(np.linspace(0, math.pi / 2, fade, dtype=np.float32)) ** 2
        track[:fade] *= shape[:, None]
        track[-fade:] *= shape[::-1, None]
    track = np.tanh(track * 1.5).astype(np.float32)
    peak = max(float(np.max(np.abs(track))), 0.001)
    return track * (0.89 / peak)


def write(name: str, track: np.ndarray, looping: bool = False) -> None:
    audio = master(track, looping)
    OUTPUT.mkdir(parents=True, exist_ok=True)
    with wave.open(str(OUTPUT / name), "wb") as target:
        target.setnchannels(2)
        target.setsampwidth(2)
        target.setframerate(RATE)
        target.writeframes((audio * 32767).astype("<i2").tobytes())


def trophy_loop() -> np.ndarray:
    """Eight measured bars: dark lake pulse, climbing melodic hooks, finale."""
    beat = 0.75  # 80 BPM, four beats per bar, 24-second loop.
    track = canvas(24.0)
    chords = (
        (73.42, (293.66, 349.23, 440.00)),   # D minor
        (58.27, (233.08, 293.66, 349.23)),   # B-flat
        (87.31, (261.63, 349.23, 440.00)),   # F
        (65.41, (261.63, 329.63, 392.00)),   # C
        (49.00, (196.00, 233.08, 293.66)),   # G minor
        (58.27, (233.08, 293.66, 349.23)),   # B-flat
        (65.41, (261.63, 392.00, 466.16)),   # C suspended colour
        (55.00, (220.00, 277.18, 329.63)),   # A resolving to D
    )
    for bar, (root, harmony) in enumerate(chords):
        bar_at = bar * 4 * beat
        # The stereo pad remains warm while the short bass and drum create a
        # more urgent free-spin pulse. The second phrase gains a higher voice.
        for voice, freq in enumerate((root * 2, harmony[0], harmony[1])):
            add(track, bar_at, note(freq, 2.95, "pad") * (0.13 if voice == 0 else 0.065), -0.52 + voice * 0.55)
        for step in range(4):
            at = bar_at + step * beat
            add(track, at, note(root, 0.55, "bass") * 0.25, -0.12)
            add(track, at, kick() * (0.42 if step in (0, 2) else 0.25), 0.0)
            if step in (1, 3):
                add(track, at, snare() * 0.17, 0.12)
            for half in range(2):
                eighth = at + half * beat / 2
                pan = -0.3 if (step + half) % 2 else 0.3
                add(track, eighth, tick() * 0.2, pan)
                freq = harmony[(step * 2 + half + bar) % 3] * (2 if bar >= 4 else 1)
                add(track, eighth + beat / 4, note(freq, 0.35) * (0.08 if bar < 4 else 0.12), pan)
        if bar in (0, 2, 4, 6):
            add(track, bar_at + 1.35, note(harmony[2] * 2, 1.4) * 0.13, 0.58)
        if bar >= 4:
            for step in (0, 2):
                add(track, bar_at + (step + 0.5) * beat, note(harmony[1] * 2, 0.58, "brass") * 0.06, 0.32)
    # Shimmering water bed is stereo and modulated rather than constant hiss.
    water = wash(24.0, 0.24)
    time = np.arange(len(water), dtype=np.float32) / RATE
    swell = 0.55 + 0.45 * np.sin(TAU * (0.12 * time + 0.04 * np.sin(TAU * 0.03 * time)))
    add(track, 0, water * swell, -0.54)
    add(track, 0.09, water * swell * 0.75, 0.54)
    echo(track, ((0.18, 0.105), (0.37, 0.07), (0.62, 0.035)))
    return track


def trophy_award() -> np.ndarray:
    track = canvas(2.6)
    time = np.arange(len(track), dtype=np.float32) / RATE
    riser = wash(2.6, 0.4) * np.minimum(1, time / 1.0) * np.exp(-2.5 * np.maximum(0, time - 1.35))
    add(track, 0, riser, -0.35)
    add(track, 0.07, riser * 0.8, 0.45)
    for at, freq, pan in ((0.28, 293.66, -0.4), (0.53, 349.23, 0.3), (0.79, 440, -0.2), (1.08, 587.33, 0.42)):
        add(track, at, note(freq, 1.1, "brass") * 0.25, pan)
        add(track, at + 0.06, note(freq * 2, 0.9) * 0.12, -pan)
    add(track, 1.18, kick() * 0.9)
    echo(track, ((0.21, 0.19), (0.45, 0.11)))
    return track


def line_tension() -> np.ndarray:
    track = canvas(1.35)
    time = np.arange(len(track), dtype=np.float32) / RATE
    env = np.sin(math.pi * time / 1.35) ** 1.7
    phase = TAU * (143 * time + 80 * time * time)
    creak = wash(1.35, 0.23) * env
    add(track, 0, (0.45 * np.sin(phase) * env + creak).astype(np.float32), -0.4)
    add(track, 0, (0.25 * np.sin(phase * 1.006) * env + creak * 0.7).astype(np.float32), 0.45)
    add(track, 0.65, tick() * 0.37, 0.48)
    return track


def cast_land() -> np.ndarray:
    track = canvas(0.7)
    time = np.arange(round(0.48 * RATE), dtype=np.float32) / RATE
    splash = RNG.standard_normal(len(time)).astype(np.float32)
    splash -= np.convolve(splash, np.ones(7, np.float32) / 7, mode="same")
    add(track, 0, splash * np.exp(-time * 17) * 0.21, -0.42)
    add(track, 0.04, splash * np.exp(-time * 13) * 0.18, 0.54)
    add(track, 0.06, note(130.81, 0.35, "bass") * 0.53)
    echo(track, ((0.18, 0.08),))
    return track


def small_catch() -> np.ndarray:
    track = canvas(1.0)
    add(track, 0, note(440, 0.76) * 0.37, -0.4)
    add(track, 0.13, note(587.33, 0.72) * 0.29, 0.4)
    add(track, 0.22, note(880, 0.58) * 0.11, 0.15)
    echo(track, ((0.17, 0.16), (0.3, 0.09)))
    return track


def collect() -> np.ndarray:
    track = canvas(1.1)
    add(track, 0, tick() * 0.75, -0.7)
    add(track, 0.04, note(659.25, 0.75) * 0.33, -0.3)
    add(track, 0.17, note(987.77, 0.73) * 0.27, 0.25)
    add(track, 0.33, note(1318.5, 0.65) * 0.17, 0.68)
    echo(track, ((0.14, 0.14), (0.29, 0.08)))
    return track


def trophy_win() -> np.ndarray:
    track = canvas(2.45)
    add(track, 0, kick() * 0.75)
    for at, freq, pan in (
        (0.05, 293.66, -0.45), (0.22, 349.23, 0.32),
        (0.43, 440, -0.28), (0.63, 587.33, 0.4),
    ):
        add(track, at, note(freq, 1.42, "brass") * 0.27, pan)
        add(track, at, note(freq * 2, 0.9) * 0.14, -pan)
    for at in (0.79, 0.95, 1.14, 1.35):
        add(track, at, collect()[:, 0] * 0.15, 0.5 if int(at * 10) % 2 else -0.5)
    echo(track, ((0.19, 0.16), (0.38, 0.09)))
    return track


def main() -> None:
    write("reel-riches-trophy-loop.wav", trophy_loop(), looping=True)
    write("reel-riches-trophy-award.wav", trophy_award())
    write("reel-riches-line-tension.wav", line_tension(), looping=True)
    write("reel-riches-cast-land.wav", cast_land())
    write("reel-riches-small-catch.wav", small_catch())
    write("reel-riches-special-collect.wav", collect())
    write("reel-riches-trophy-win.wav", trophy_win())


if __name__ == "__main__":
    main()
