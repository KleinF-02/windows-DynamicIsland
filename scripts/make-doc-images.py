"""Builds the README artwork from real app screenshots.

Dev-only helper. Requires Pillow and the screenshots under artifacts/screenshots/.
The generated PNGs in docs/images are committed so the README works without Python.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter, ImageFont

ROOT = Path(__file__).resolve().parent.parent
SHOTS = ROOT / "artifacts" / "screenshots"
OUT = ROOT / "docs" / "images"
SCALE = 1.25
SHADOW = round(14 * SCALE)

FONT_CN_BOLD = "C:/Windows/Fonts/msyhbd.ttc"
FONT_CN = "C:/Windows/Fonts/msyh.ttc"
FONT_EN_BOLD = "C:/Windows/Fonts/segoeuib.ttf"
FONT_EN = "C:/Windows/Fonts/segoeui.ttf"


def font(path: str, size: int) -> ImageFont.FreeTypeFont:
    return ImageFont.truetype(path, size)


def rounded_mask(size: tuple[int, int], radius: int) -> Image.Image:
    factor = 4
    big = Image.new("L", (size[0] * factor, size[1] * factor), 0)
    ImageDraw.Draw(big).rounded_rectangle(
        (0, 0, big.width - 1, big.height - 1), radius=radius * factor, fill=255
    )
    return big.resize(size, Image.Resampling.LANCZOS)


def crop_island(screenshot: Path, box: tuple[int, int, int, int], radius: int) -> Image.Image:
    source = Image.open(screenshot).convert("RGBA")
    crop = source.crop(box)
    crop.putalpha(rounded_mask(crop.size, radius))
    return crop


def backdrop(width: int, height: int) -> Image.Image:
    image = Image.new("RGBA", (width, height), (7, 10, 15, 255))
    top = (17, 22, 32)
    bottom = (5, 7, 10)
    pixels = image.load()
    for y in range(height):
        t = y / max(1, height - 1)
        color = tuple(round(a + (b - a) * t) for a, b in zip(top, bottom))
        for x in range(width):
            pixels[x, y] = (*color, 255)

    glow = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    draw = ImageDraw.Draw(glow)
    for radius, alpha in ((520, 22), (380, 28), (260, 32)):
        draw.ellipse(
            (
                width // 2 - radius,
                height // 2 - radius - 40,
                width // 2 + radius,
                height // 2 + radius - 40,
            ),
            fill=(79, 227, 194, alpha),
        )
    image.alpha_composite(glow.filter(ImageFilter.GaussianBlur(80)))

    # A faint vertical hairline grid adds depth without competing with the island.
    grid = Image.new("RGBA", (width, height), (0, 0, 0, 0))
    gdraw = ImageDraw.Draw(grid)
    for x in range(0, width, 56):
        gdraw.line((x, 0, x, height), fill=(255, 255, 255, 5), width=1)
    image.alpha_composite(grid)
    return image


def drop_shadow(canvas: Image.Image, box: tuple[int, int, int, int], radius: int) -> None:
    shadow = Image.new("RGBA", canvas.size, (0, 0, 0, 0))
    shifted = (box[0] + 2, box[1] + 14, box[2] + 2, box[3] + 16)
    ImageDraw.Draw(shadow).rounded_rectangle(shifted, radius=radius, fill=(0, 0, 0, 210))
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(28)))


def centered_text(
    canvas: Image.Image,
    text: str,
    y: int,
    text_font: ImageFont.FreeTypeFont,
    fill: tuple[int, int, int, int],
) -> None:
    draw = ImageDraw.Draw(canvas)
    bbox = draw.textbbox((0, 0), text, font=text_font)
    x = (canvas.width - (bbox[2] - bbox[0])) // 2
    draw.text((x, y), text, font=text_font, fill=fill)


def make_hero() -> None:
    canvas = backdrop(1400, 720)

    centered_text(canvas, "WINDOWS 11   ·   WPF   ·   .NET 10", 92, font(FONT_EN_BOLD, 13), (112, 124, 145, 255))
    centered_text(canvas, "灵动岛", 128, font(FONT_CN_BOLD, 52), (246, 247, 251, 255))
    centered_text(canvas, "桌面顶部的一颗呼吸感胶囊", 200, font(FONT_CN, 17), (156, 164, 181, 255))

    island = crop_island(
        SHOTS / "clean-expanded.png",
        (678 + SHADOW, 3 + SHADOW, 678 + 565 - SHADOW, 3 + 280 - SHADOW),
        radius=31,
    )
    x = (canvas.width - island.width) // 2
    y = 306
    drop_shadow(canvas, (x, y, x + island.width, y + island.height), 29)
    ImageDraw.Draw(canvas).rounded_rectangle(
        (x, y, x + island.width, y + island.height), radius=31, fill=(5, 7, 10, 255)
    )
    canvas.alpha_composite(island, (x, y))

    centered_text(
        canvas,
        "悬停展开  ·  点击固定  ·  右键菜单",
        650,
        font(FONT_CN, 13),
        (130, 140, 158, 255),
    )

    OUT.mkdir(parents=True, exist_ok=True)
    canvas.convert("RGB").save(OUT / "hero.png", optimize=True)


def make_settings() -> None:
    source = Image.open(SHOTS / "clean-settings3.png").convert("RGBA")
    window = source.crop((663, 89, 1257, 938))
    window.putalpha(rounded_mask(window.size, 11))

    canvas = backdrop(760, 970)
    drop_shadow(canvas, (80, 50, 674, 899), 11)
    ImageDraw.Draw(canvas).rounded_rectangle((80, 50, 674, 899), radius=11, fill=(11, 14, 20, 255))
    canvas.alpha_composite(window, (80, 50))
    canvas.convert("RGB").save(OUT / "settings.png", optimize=True)


def make_compact() -> None:
    island = crop_island(
        SHOTS / "clean-compact.png",
        (788 + SHADOW, 3 + SHADOW, 788 + 345 - SHADOW, 3 + 93 - SHADOW),
        radius=31,
    )

    canvas = Image.new("RGBA", (820, 190), (0, 0, 0, 0))
    drop_shadow(canvas, (240, 55, 240 + island.width, 55 + island.height), 29)
    ImageDraw.Draw(canvas).rounded_rectangle(
        (240, 55, 240 + island.width, 55 + island.height), radius=31, fill=(5, 7, 10, 255)
    )
    canvas.alpha_composite(island, (240, 55))
    canvas.save(OUT / "island-compact.png", optimize=True)


if __name__ == "__main__":
    make_hero()
    make_settings()
    make_compact()
    print(f"Wrote documentation images to {OUT}")
