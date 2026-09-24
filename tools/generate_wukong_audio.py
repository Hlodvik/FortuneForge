"""Generate FortuneForge's original, dependency-free Wukong slot SFX palette."""

from __future__ import annotations

import math
import random
import struct
import wave
from pathlib import Path


SAMPLE_RATE = 44_100
OUTPUT = Path(__file__).parents[1] / "fortuneforge.client" / "src" / "assets" / "slots" / "audio"


def write_wav(name: str, samples: list[float]) -> None:
    peak = max(0.001, max(abs(sample) for sample in samples))
    scale = 0.86 / peak
    pcm = b"".join(struct.pack("<h", round(max(-1, min(1, sample * scale)) * 32767)) for sample in samples)
    with wave.open(str(OUTPUT / name), "wb") as target:
        target.setnchannels(1)
        target.setsampwidth(2)
        target.setframerate(SAMPLE_RATE)
        target.writeframes(pcm)


def temple_bell(duration: float, root: float, weight: float = 1.0) -> list[float]:
    partials = ((1.0, 1.0, 2.6), (2.01, 0.42, 3.5), (2.72, 0.26, 4.2), (4.08, 0.12, 5.4))
    samples: list[float] = []
    for index in range(round(duration * SAMPLE_RATE)):
        time = index / SAMPLE_RATE
        attack = min(1.0, time / 0.008)
        value = sum(
            amplitude * math.sin(2 * math.pi * root * ratio * time) * math.exp(-decay * time)
            for ratio, amplitude, decay in partials
        )
        samples.append(value * attack * weight)
    return samples


def staff_whoosh() -> list[float]:
    duration = 0.48
    rng = random.Random(508)
    phase = 0.0
    smooth_noise = 0.0
    samples: list[float] = []
    for index in range(round(duration * SAMPLE_RATE)):
        time = index / SAMPLE_RATE
        progress = time / duration
        envelope = math.sin(math.pi * progress) ** 1.45
        frequency = 180 + 760 * progress * progress
        phase += 2 * math.pi * frequency / SAMPLE_RATE
        smooth_noise = 0.9 * smooth_noise + 0.1 * rng.uniform(-1, 1)
        strike = math.sin(2 * math.pi * 1180 * time) * math.exp(-38 * max(0, time - 0.34)) if time >= 0.34 else 0
        samples.append((0.38 * math.sin(phase) + 0.32 * smooth_noise) * envelope + 0.22 * strike)
    return samples


def cloud_rush() -> list[float]:
    duration = 1.0
    rng = random.Random(777)
    smooth_noise = 0.0
    raw: list[float] = []
    for index in range(SAMPLE_RATE):
        time = index / SAMPLE_RATE
        smooth_noise = 0.965 * smooth_noise + 0.035 * rng.uniform(-1, 1)
        breath = (0.42 + 0.13 * math.sin(2 * math.pi * 2 * time)) * smooth_noise
        hum = 0.18 * math.sin(2 * math.pi * 110 * time + 0.25 * math.sin(2 * math.pi * 3 * time))
        shimmer = 0.08 * math.sin(2 * math.pi * 440 * time) * (0.5 + 0.5 * math.sin(2 * math.pi * time))
        raw.append(breath + hum + shimmer)

    crossfade = round(0.09 * SAMPLE_RATE)
    for index in range(crossfade):
        mix = index / max(1, crossfade - 1)
        blended = raw[-crossfade + index] * (1 - mix) + raw[index] * mix
        raw[index] = blended
        raw[-crossfade + index] = blended
    return raw


def woodblock(root: float = 720, duration: float = 0.2) -> list[float]:
    rng = random.Random(round(root))
    samples: list[float] = []
    for index in range(round(duration * SAMPLE_RATE)):
        time = index / SAMPLE_RATE
        click = rng.uniform(-1, 1) * math.exp(-95 * time)
        body = (
            math.sin(2 * math.pi * root * time) * math.exp(-31 * time)
            + 0.46 * math.sin(2 * math.pi * root * 1.63 * time) * math.exp(-44 * time)
        )
        samples.append(0.22 * click + 0.78 * body)
    return samples


def gong() -> list[float]:
    duration = 1.65
    rng = random.Random(1964)
    partials = ((132, 1.0), (206, 0.62), (283, 0.36), (421, 0.2), (657, 0.1))
    samples: list[float] = []
    for index in range(round(duration * SAMPLE_RATE)):
        time = index / SAMPLE_RATE
        attack = min(1.0, time / 0.024)
        wobble = 1 + 0.005 * math.sin(2 * math.pi * 4.1 * time)
        body = sum(
            amplitude * math.sin(2 * math.pi * frequency * wobble * time) * math.exp(-(1.55 + position * 0.42) * time)
            for position, (frequency, amplitude) in enumerate(partials)
        )
        noise = rng.uniform(-1, 1) * math.exp(-34 * time)
        samples.append(attack * (0.78 * body + 0.12 * noise))
    return samples


def celestial_chime() -> list[float]:
    duration = 1.4
    roots = ((0.00, 523.25, 0.72), (0.14, 659.25, 0.62), (0.28, 783.99, 0.58), (0.44, 1046.5, 0.48))
    samples = [0.0] * round(duration * SAMPLE_RATE)
    for start, root, volume in roots:
        bell = temple_bell(duration - start, root, volume)
        offset = round(start * SAMPLE_RATE)
        for index, sample in enumerate(bell):
            samples[offset + index] += sample
    return samples


def main() -> None:
    OUTPUT.mkdir(parents=True, exist_ok=True)
    write_wav("wukong-staff-whoosh.wav", staff_whoosh())
    write_wav("wukong-cloud-rush.wav", cloud_rush())
    write_wav("wukong-woodblock.wav", woodblock())
    write_wav("wukong-soft-step.wav", woodblock(root=470, duration=0.26))
    write_wav("wukong-temple-bell.wav", temple_bell(0.9, 523.25))
    write_wav("wukong-gong-win.wav", gong())
    write_wav("wukong-celestial-chime.wav", celestial_chime())


if __name__ == "__main__":
    main()
