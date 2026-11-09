"""Generate all Windows/MSIX visual assets for Vellum from assets/icon.png.

Usage: python tools/make_icons.py
Writes into src/Vellum/Assets.
"""
from pathlib import Path
from PIL import Image

ROOT = Path(__file__).resolve().parent.parent
SRC = ROOT / "assets" / "icon.png"
OUT = ROOT / "src" / "Vellum" / "Assets"
SCALES = {100: 1.0, 125: 1.25, 150: 1.5, 200: 2.0, 400: 4.0}


def load_trimmed() -> Image.Image:
    """Load the icon, cut away empty transparent borders and make it square."""
    im = Image.open(SRC).convert("RGBA")
    alpha = im.getchannel("A").point(lambda a: 255 if a > 8 else 0)
    im = im.crop(alpha.getbbox())
    side = max(im.size)
    sq = Image.new("RGBA", (side, side), (0, 0, 0, 0))
    sq.paste(im, ((side - im.width) // 2, (side - im.height) // 2))
    return sq


ICON = load_trimmed()


def canvas(w: int, h: int, fill: float) -> Image.Image:
    """Icon centered on a transparent w x h canvas, taking `fill` of the shorter side."""
    size = max(1, round(min(w, h) * fill))
    icon = ICON.resize((size, size), Image.LANCZOS)
    img = Image.new("RGBA", (w, h), (0, 0, 0, 0))
    img.paste(icon, ((w - size) // 2, (h - size) // 2), icon)
    return img


def save(img: Image.Image, name: str) -> None:
    img.save(OUT / name, optimize=True)


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)

    for scale, k in SCALES.items():
        save(canvas(round(44 * k), round(44 * k), 0.92), f"Square44x44Logo.scale-{scale}.png")
        save(canvas(round(150 * k), round(150 * k), 0.66), f"Square150x150Logo.scale-{scale}.png")
        save(canvas(round(310 * k), round(150 * k), 0.66), f"Wide310x150Logo.scale-{scale}.png")
        save(canvas(round(50 * k), round(50 * k), 0.96), f"StoreLogo.scale-{scale}.png")
        save(canvas(round(620 * k), round(300 * k), 0.5), f"SplashScreen.scale-{scale}.png")

    # Taskbar, Start menu and file explorer sizes
    for size in (16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256):
        img = canvas(size, size, 0.94)
        save(img, f"Square44x44Logo.targetsize-{size}.png")
        save(img, f"Square44x44Logo.targetsize-{size}_altform-unplated.png")
        save(img, f"Square44x44Logo.targetsize-{size}_altform-lightunplated.png")

    # Window / Alt+Tab icon and a small icon for the tab strip
    canvas(256, 256, 0.94).save(
        OUT / "AppIcon.ico", sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)]
    )
    save(canvas(64, 64, 0.94), "AppIcon.png")
    print("Assets written to", OUT)


if __name__ == "__main__":
    main()
