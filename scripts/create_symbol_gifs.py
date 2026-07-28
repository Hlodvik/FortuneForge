from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageEnhance


ROOT = Path(__file__).resolve().parents[1]
SYMBOL_ROOT = ROOT / "fortuneforge.client" / "src" / "assets" / "slots" / "symbols"
CANVAS_SIZE = 384
FRAME_COUNT = 18
STAFF_FRAME_COUNT = 54
FRAME_DURATION_MS = 72

SYMBOLS = (
    (SYMBOL_ROOT / "wukong" / "nimbus-cloud.png", SYMBOL_ROOT / "wukong" / "nimbus-cloud-animated.gif", 0.0, False),
    (SYMBOL_ROOT / "wukong" / "immortality-peach.png", SYMBOL_ROOT / "wukong" / "immortality-peach-animated.gif", 0.3, False),
    (SYMBOL_ROOT / "wukong" / "celestial-gourd.png", SYMBOL_ROOT / "wukong" / "celestial-gourd-animated.gif", 0.7, False),
    (SYMBOL_ROOT / "wukong" / "jade-dragon-pearl.png", SYMBOL_ROOT / "wukong" / "jade-dragon-pearl-animated.gif", 1.1, False),
    (SYMBOL_ROOT / "wukong" / "golden-circlet.png", SYMBOL_ROOT / "wukong" / "golden-circlet-animated.gif", 1.5, False),
    (SYMBOL_ROOT / "wukong" / "celestial-staff.png", SYMBOL_ROOT / "wukong" / "celestial-staff-animated.gif", 1.9, True),
    (SYMBOL_ROOT / "wukong" / "wukong-medallion.png", SYMBOL_ROOT / "wukong" / "wukong-medallion-animated.gif", 2.3, False),
    (SYMBOL_ROOT / "free-game.png", SYMBOL_ROOT / "free-game-animated.gif", 2.7, False),
)


def crop_visible(image: Image.Image) -> Image.Image:
    bounds = image.getchannel("A").getbbox()
    return image.crop(bounds) if bounds else image


def fit(image: Image.Image, maximum: int) -> Image.Image:
    ratio = min(maximum / image.width, maximum / image.height)
    size = (max(1, round(image.width * ratio)), max(1, round(image.height * ratio)))
    return image.resize(size, Image.Resampling.LANCZOS)


def palette_frame(frame: Image.Image) -> Image.Image:
    alpha = frame.getchannel("A")
    color = frame.convert("RGB").quantize(colors=255, method=Image.Quantize.MEDIANCUT)
    transparent = alpha.point(lambda value: 255 if value < 36 else 0)
    color.paste(255, mask=transparent)
    color.info["transparency"] = 255
    return color


def make_animation(
    source_path: Path,
    output_path: Path,
    phase: float,
    glint: Image.Image,
    staff_twirl: bool = False,
) -> None:
    source = crop_visible(Image.open(source_path).convert("RGBA"))
    source = fit(source, round(CANVAS_SIZE * 0.78))
    glint = fit(glint, round(CANVAS_SIZE * 0.20))
    frames: list[Image.Image] = []

    frame_count = STAFF_FRAME_COUNT if staff_twirl else FRAME_COUNT

    for index in range(frame_count):
        progress = index / frame_count
        angle = progress * math.tau + phase
        bounce = round(math.sin(angle) * CANVAS_SIZE * 0.012)
        breathe = 1 + math.sin(angle) * 0.018
        idle_tilt = math.sin(angle) * (0.55 if staff_twirl else 1.1)

        twirl_start = 0.56
        twirl_end = 0.75
        twirl_progress = min(1.0, max(0.0, (progress - twirl_start) / (twirl_end - twirl_start)))
        twirl_eased = twirl_progress * twirl_progress * (3 - 2 * twirl_progress)
        tilt = idle_tilt + (twirl_eased * 360 if staff_twirl else 0)

        scaled = source.resize(
            (max(1, round(source.width * breathe)), max(1, round(source.height * breathe))),
            Image.Resampling.LANCZOS,
        ).rotate(tilt, resample=Image.Resampling.BICUBIC, expand=True)

        frame = Image.new("RGBA", (CANVAS_SIZE, CANVAS_SIZE), (0, 0, 0, 0))
        symbol_x = (CANVAS_SIZE - scaled.width) // 2
        symbol_y = (CANVAS_SIZE - scaled.height) // 2 + bounce
        frame.alpha_composite(scaled, (symbol_x, symbol_y))

        glint_progress = twirl_progress if staff_twirl else progress
        glint_strength = max(0.0, math.sin(glint_progress * math.pi)) ** 5
        if glint_strength > 0.02:
            glint_frame = glint.copy()
            glint_frame.putalpha(
                glint_frame.getchannel("A").point(
                    lambda value: round(value * glint_strength * 0.92)
                )
            )
            glint_x = round(CANVAS_SIZE * (0.23 + glint_progress * 0.54) - glint_frame.width / 2)
            glint_y = round(CANVAS_SIZE * (0.68 - glint_progress * 0.45) - glint_frame.height / 2)
            frame.alpha_composite(glint_frame, (glint_x, glint_y))

        brightness = 1 + glint_strength * 0.05
        frame = ImageEnhance.Brightness(frame).enhance(brightness)
        frames.append(palette_frame(frame))

    output_path.parent.mkdir(parents=True, exist_ok=True)
    frames[0].save(
        output_path,
        save_all=True,
        append_images=frames[1:],
        duration=FRAME_DURATION_MS,
        loop=0,
        disposal=2,
        transparency=255,
        optimize=True,
    )
    print(f"Wrote {output_path.relative_to(ROOT)}")


def main() -> None:
    glint = crop_visible(Image.open(SYMBOL_ROOT / "symbol-glint.png").convert("RGBA"))
    for source_path, output_path, phase, staff_twirl in SYMBOLS:
        make_animation(source_path, output_path, phase, glint, staff_twirl)


if __name__ == "__main__":
    main()
