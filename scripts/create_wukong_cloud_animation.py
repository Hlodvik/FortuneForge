from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageEnhance


ROOT = Path(__file__).resolve().parents[1]
SOURCE = (
    ROOT
    / "fortuneforge.client"
    / "src"
    / "assets"
    / "slots"
    / "symbols"
    / "wukong"
    / "nimbus-cloud-platform.png"
)
OUTPUT = SOURCE.with_name("nimbus-cloud-platform-animated.webp")
CANVAS_SIZE = 512
FRAME_COUNT = 36
FRAME_DURATION_MS = 80
MESH_COLUMNS = 8
MESH_ROWS = 12


def source_point(x: float, y: float, phase: float) -> tuple[float, float]:
    normalized_x = x / CANVAS_SIZE
    normalized_y = y / CANVAS_SIZE
    cloud_band = math.sin(math.pi * normalized_y) ** 2
    horizontal_roll = math.sin(phase + normalized_y * math.tau * 1.15)
    vertical_roll = math.sin(phase - normalized_x * math.tau * 0.9)
    return (
        x + horizontal_roll * 3.6 * cloud_band,
        y + vertical_roll * 2.1 * cloud_band,
    )


def warp_cloud(source: Image.Image, phase: float) -> Image.Image:
    mesh = []
    cell_width = CANVAS_SIZE / MESH_COLUMNS
    cell_height = CANVAS_SIZE / MESH_ROWS

    for row in range(MESH_ROWS):
        top = round(row * cell_height)
        bottom = round((row + 1) * cell_height)
        for column in range(MESH_COLUMNS):
            left = round(column * cell_width)
            right = round((column + 1) * cell_width)
            top_left = source_point(left, top, phase)
            bottom_left = source_point(left, bottom, phase)
            bottom_right = source_point(right, bottom, phase)
            top_right = source_point(right, top, phase)
            mesh.append(
                (
                    (left, top, right, bottom),
                    (*top_left, *bottom_left, *bottom_right, *top_right),
                )
            )

    frame = source.transform(
        source.size,
        Image.Transform.MESH,
        mesh,
        resample=Image.Resampling.BICUBIC,
    )
    glow = 1 + math.sin(phase - math.pi / 2) * 0.012
    return ImageEnhance.Brightness(frame).enhance(glow)


def main() -> None:
    source = Image.open(SOURCE).convert("RGBA").resize(
        (CANVAS_SIZE, CANVAS_SIZE),
        Image.Resampling.LANCZOS,
    )
    frames = [
        warp_cloud(source, index / FRAME_COUNT * math.tau)
        for index in range(FRAME_COUNT)
    ]
    frames[0].save(
        OUTPUT,
        save_all=True,
        append_images=frames[1:],
        duration=FRAME_DURATION_MS,
        loop=0,
        lossless=False,
        quality=88,
        method=3,
    )
    print(f"Wrote {OUTPUT.relative_to(ROOT)} with {FRAME_COUNT} seamless frames")


if __name__ == "__main__":
    main()
