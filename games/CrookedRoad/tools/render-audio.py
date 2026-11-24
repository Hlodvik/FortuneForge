"""Render Crooked Road's original, seeded sound suite using only the standard library.

Run: py -3.10 games/CrookedRoad/tools/render-audio.py
All tones and textures are synthesized here; no recordings or external samples.
"""
from __future__ import annotations

import hashlib
import math
from pathlib import Path
import random
import struct
import wave

SAMPLE_RATE = 22050
OUTPUT = Path(__file__).resolve().parents[1] / "client/FortuneForge.Games.CrookedRoad.Client/src/assets/audio"
TAU = math.tau


def seed(name: str) -> random.Random:
    return random.Random(int.from_bytes(hashlib.sha256(name.encode()).digest()[:8], "big"))


def pluck(midi: int, duration: float, name: str, warmth: float = 0.994) -> list[float]:
    """A damped string with a short, soft excitation and a low body resonance."""
    frequency = 440 * 2 ** ((midi - 69) / 12)
    delay = max(2, round(SAMPLE_RATE / frequency - 0.5))
    rng = seed(name)
    string = [rng.uniform(-1, 1) for _ in range(delay)]
    for _ in range(3):
        string = [(string[index] + string[(index + 1) % delay]) * 0.5 for index in range(delay)]
    peak = max(abs(value) for value in string) or 1
    string = [value / peak for value in string]
    result = []
    for index in range(round(duration * SAMPLE_RATE)):
        position = index % delay
        value = string[position]
        string[position] = warmth * (value + string[(position + 1) % delay]) * 0.5
        time = index / SAMPLE_RATE
        attack = min(1, time / 0.005)
        release = min(1, max(0, duration - time) / 0.08)
        body = 0.08 * math.sin(TAU * frequency * time) * math.exp(-time * 5)
        result.append((value * math.exp(-time * 1.4) + body) * attack * release)
    return result


def tap(kind: str, name: str, duration: float = 0.16) -> list[float]:
    rng = seed(name)
    result = []
    filtered = 0.0
    modes = (210, 393, 641) if kind == "stone" else (760, 1140, 1710)
    for index in range(round(duration * SAMPLE_RATE)):
        time = index / SAMPLE_RATE
        filtered = filtered * 0.78 + rng.uniform(-1, 1) * 0.22
        attack = min(1, time / 0.003)
        envelope = math.exp(-time * (32 if kind == "stone" else 23))
        tone = sum(math.sin(TAU * frequency * time) / (mode + 1) for mode, frequency in enumerate(modes))
        result.append((tone * 0.24 + filtered * 0.36) * envelope * attack)
    return result


def add(target: list[float], sound: list[float], at: float, gain: float = 1, circular: bool = False) -> None:
    start = round(at * SAMPLE_RATE)
    for offset, value in enumerate(sound):
        index = start + offset
        if circular:
            index %= len(target)
        elif index >= len(target):
            break
        target[index] += value * gain


def room(signal: list[float], circular: bool = False) -> list[float]:
    wet = signal.copy()
    for delay, gain in ((0.071, 0.17), (0.137, 0.10), (0.223, 0.055)):
        add(wet, signal, delay, gain, circular)
    return wet


def write(name: str, signal: list[float], peak: float, loop: bool = False) -> None:
    signal = room(signal, loop)
    mean = sum(signal) / len(signal)
    signal = [value - mean for value in signal]
    maximum = max(abs(value) for value in signal) or 1
    signal = [value * peak / maximum for value in signal]
    if not loop:
        fade = round(0.008 * SAMPLE_RATE)
        for index in range(fade):
            signal[index] *= index / fade
            signal[-index - 1] *= index / fade
    samples = [round(max(-0.98, min(0.98, value)) * 32767) for value in signal]
    path = OUTPUT / f"crooked-road-{name}-v1.wav"
    with wave.open(str(path), "wb") as stream:
        stream.setparams((1, 2, SAMPLE_RATE, len(samples), "NONE", "not compressed"))
        stream.writeframes(struct.pack(f"<{len(samples)}h", *samples))
    rms = math.sqrt(sum(value * value for value in signal) / len(signal))
    boundary = abs(signal[0] - signal[-1])
    if max(abs(value) for value in samples) >= 32767:
        raise RuntimeError(f"Clipping in {name}")
    if loop and boundary > 0.015:
        raise RuntimeError(f"Loop boundary discontinuity in {name}: {boundary}")
    print(f"{path.name}: {len(samples) / SAMPLE_RATE:.3f}s, {path.stat().st_size} bytes, peak {peak:.3f}, RMS {rms:.4f}, boundary {boundary:.5f}")


def phrase(name: str, duration: float, notes: list[tuple[float, int, float]], peak: float) -> None:
    sound = [0.0] * round(duration * SAMPLE_RATE)
    for index, (at, midi, gain) in enumerate(notes):
        add(sound, pluck(midi, min(1.45, duration - at), f"{name}-{index}"), at, gain)
    write(name, sound, peak)


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    write("click", tap("stone", "click", 0.10), 0.28)
    write("coin", tap("coin", "coin", 0.22), 0.36)
    write("step", tap("stone", "step", 0.18), 0.34)

    start = [0.0] * round(0.55 * SAMPLE_RATE)
    add(start, tap("stone", "spin-start", 0.14), 0, 0.45)
    add(start, pluck(62, 0.5, "spin-start-string"), 0.025, 0.7)
    add(start, pluck(69, 0.34, "spin-start-upper"), 0.13, 0.35)
    write("spin-start", start, 0.48)

    roll = [0.0] * round(0.96 * SAMPLE_RATE)
    for index in range(16):
        add(roll, tap("stone", f"roll-{index}", 0.09), index * 0.06, 0.30 if index % 4 else 0.45, True)
    write("reel-roll", roll, 0.27, True)

    landed = [0.0] * round(0.33 * SAMPLE_RATE)
    add(landed, tap("stone", "landed", 0.16), 0, 0.6)
    add(landed, pluck(69, 0.30, "landed-string"), 0.015, 0.4)
    write("reel-land", landed, 0.40)

    phrase("small-win", 1.35, [(0, 62, 0.8), (0.14, 66, 0.8), (0.28, 69, 0.9), (0.45, 74, 0.65)], 0.55)
    phrase("big-win", 3.20, [(0, 50, 0.55), (0.02, 62, 0.7), (0.16, 66, 0.7), (0.32, 69, 0.8), (0.50, 71, 0.7), (0.68, 76, 0.7), (0.92, 74, 0.85), (1.16, 69, 0.5), (1.42, 66, 0.5), (1.62, 74, 0.8), (1.66, 62, 0.5)], 0.62)
    phrase("journey-start", 1.75, [(0, 57, 0.45), (0.18, 62, 0.65), (0.36, 64, 0.65), (0.54, 69, 0.8), (0.78, 74, 0.7)], 0.56)
    phrase("journey-end", 2.15, [(0, 76, 0.55), (0.20, 74, 0.65), (0.42, 69, 0.6), (0.66, 66, 0.6), (0.91, 62, 0.85), (0.94, 50, 0.55), (0.97, 69, 0.35)], 0.56)

    duration = 8.0
    ambience = [0.0] * round(duration * SAMPLE_RATE)
    # An original four-bar, three-beat phrase. Tails wrap around the loop.
    melody = [62, 69, 71, 66, 64, 69, 74, 71, 69, 66, 64, 62]
    for index, midi in enumerate(melody):
        at = index * 2 / 3
        add(ambience, pluck(midi, 1.8, f"ambience-{index}"), at, 0.45 if index % 3 == 0 else 0.26, True)
    for index in range(len(ambience)):
        time = index / SAMPLE_RATE
        # Integer periods preserve a continuous loop for the low air-like body.
        ambience[index] += 0.018 * math.sin(TAU * 73.375 * time) + 0.009 * math.sin(TAU * 110 * time)
    write("harp-loop", ambience, 0.23, True)
    print(f"Total suite bytes: {sum(path.stat().st_size for path in OUTPUT.glob('*.wav'))}")


if __name__ == "__main__":
    main()
