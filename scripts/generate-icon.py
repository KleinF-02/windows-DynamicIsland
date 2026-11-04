"""Generates the Dynamic Island application icon.

Dev-only helper: requires Pillow. The generated .ico is committed, so contributors
do not need Python to build or run the app.
"""

from __future__ import annotations

from pathlib import Path

from PIL import Image, ImageDraw, ImageFilter

SIZE = 1024
ASSETS = Path(__file__).resolve().parent.parent / "src" / "DynamicIsland.App" / "Assets"


def lerp(a: tuple[int, int, int], b: tuple[int, int, int], t: float) -> tuple[int, int, int]:
    return tuple(round(x + (y - x) * t) for x, y in zip(a, b))  # type: ignore[return-value]


def vertical_gradient(size: tuple[int, int], top: tuple[int, int, int], bottom: tuple[int, int, int]) -> Image.Image:
    width, height = size
    image = Image.new("RGBA", size)
    pixels = image.load()
    for y in range(height):
        color = lerp(top, bottom, y / max(1, height - 1))
        for x in range(width):
            pixels[x, y] = (*color, 255)
    return image


def radial_glow(size: int, color: tuple[int, int, int], alpha: int) -> Image.Image:
    layer = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    draw = ImageDraw.Draw(layer)
    steps = 36
    for step in range(steps, 0, -1):
        ratio = step / steps
        radius = int((size / 2) * ratio)
        current_alpha = int(alpha * (1 - ratio) ** 2)
        draw.ellipse(
            (size // 2 - radius, size // 2 - radius, size // 2 + radius, size // 2 + radius),
            fill=(*color, current_alpha),
        )
    return layer.filter(ImageFilter.GaussianBlur(size / 14))


def main() -> None:
    ASSETS.mkdir(parents=True, exist_ok=True)
    canvas = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))

    # Soft outer shadow.
    shadow = Image.new("RGBA", (SIZE, SIZE), (0, 0, 0, 0))
    ImageDraw.Draw(shadow).rounded_rectangle(
        (74, 92, 950, 968), radius=238, fill=(0, 0, 0, 190)
    )
    canvas.alpha_composite(shadow.filter(ImageFilter.GaussianBlur(46)))

    # Rounded square with a midnight gradient.
    tile_mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(tile_mask).rounded_rectangle((62, 62, 962, 962), radius=232, fill=255)
    tile = vertical_gradient((SIZE, SIZE), (24, 29, 42), (5, 7, 11))
    canvas.paste(tile, (0, 0), tile_mask)

    # Thin highlight around the tile.
    ImageDraw.Draw(canvas).rounded_rectangle(
        (64, 64, 960, 960), radius=230, outline=(255, 255, 255, 38), width=4
    )

    # Ambient cyan bloom behind the island.
    bloom = radial_glow(620, (82, 232, 196), 150)
    canvas.alpha_composite(bloom, (170, 240))

    # The island pill.
    pill_box = (222, 392, 802, 664)
    pill_mask = Image.new("L", (SIZE, SIZE), 0)
    ImageDraw.Draw(pill_mask).rounded_rectangle(pill_box, radius=136, fill=255)
    pill = vertical_gradient((SIZE, SIZE), (15, 20, 28), (2, 3, 5))
    canvas.paste(pill, (0, 0), pill_mask)
    ImageDraw.Draw(canvas).rounded_rectangle(
        pill_box, radius=136, outline=(255, 255, 255, 58), width=4
    )

    # Glowing status orb.
    orb_glow = radial_glow(300, (99, 241, 205), 235)
    canvas.alpha_composite(orb_glow, (246, 348))

    orb_size = 132
    orb_mask = Image.new("L", (orb_size, orb_size), 0)
    ImageDraw.Draw(orb_mask).ellipse((0, 0, orb_size - 1, orb_size - 1), fill=255)
    orb = vertical_gradient((orb_size, orb_size), (126, 250, 217), (104, 155, 255))
    canvas.paste(orb, (346, 446), orb_mask)

    # Three sound bars.
    bar_specs = [(612, 486, 34), (672, 452, 68), (732, 474, 46)]
    for x, y, height in bar_specs:
        bar_mask = Image.new("L", (SIZE, SIZE), 0)
        ImageDraw.Draw(bar_mask).rounded_rectangle(
            (x, y, x + 24, y + height), radius=12, fill=235
        )
        bar = vertical_gradient((SIZE, SIZE), (167, 246, 228), (124, 168, 255))
        canvas.paste(bar, (0, 0), bar_mask)

    canvas.save(ASSETS / "app-icon.png")
    canvas.resize((256, 256), Image.Resampling.LANCZOS).save(ASSETS / "app-256.png")

    canvas.save(
        ASSETS / "app.ico",
        format="ICO",
        sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)],
    )
    print(f"Wrote {ASSETS / 'app.ico'}")


if __name__ == "__main__":
    main()
