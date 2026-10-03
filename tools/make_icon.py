"""Generate the K3Pro app icon (numpad keys + knob + RGB strip) in every format the packages need.

  python tools/make_icon.py

Outputs:
  src/K3Pro.App/Assets/k3pro.ico      Windows exe + window icon (16–256 px)
  src/K3Pro.App/Assets/k3pro.png      256 px (window icon fallback)
  packaging/macos/K3Pro.icns          macOS bundle icon
  packaging/linux/k3pro.png           512 px for the .desktop entry
  docs/images/icon.png                256 px for the README

Drawn at 4x and downsampled for anti-aliasing. Colors follow the app theme (App.axaml).
"""
import os
from PIL import Image, ImageDraw

ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
SIZE = 1024
S = 4  # supersampling factor

BG_TOP = (40, 44, 54)
BG_BOTTOM = (21, 23, 28)
KEY = (52, 58, 70)
KEY_EDGE = (66, 73, 88)
ACCENT = (91, 155, 255)       # AccentBlue
ACCENT_EDGE = (156, 194, 255)
KNOB = (58, 65, 79)
KNOB_RING = (92, 101, 120)
NOTCH = (225, 230, 240)
RGB = [(255, 72, 72), (255, 196, 64), (64, 220, 120), (64, 200, 255), (110, 110, 255), (230, 80, 230)]


def px(v):
    return int(round(v * S))


def lerp(a, b, t):
    return tuple(int(round(a[i] + (b[i] - a[i]) * t)) for i in range(3))


def render():
    w = SIZE * S
    img = Image.new('RGBA', (w, w), (0, 0, 0, 0))

    # Rounded-square body with a vertical gradient
    margin, radius = 64, 210
    grad = Image.new('RGBA', (w, w))
    gd = ImageDraw.Draw(grad)
    for y in range(w):
        gd.line([(0, y), (w, y)], fill=lerp(BG_TOP, BG_BOTTOM, y / w) + (255,))
    mask = Image.new('L', (w, w), 0)
    ImageDraw.Draw(mask).rounded_rectangle([px(margin), px(margin), px(SIZE - margin), px(SIZE - margin)], px(radius), fill=255)
    img.paste(grad, (0, 0), mask)

    d = ImageDraw.Draw(img)

    def key(x0, y0, x1, y1, fill=KEY, edge=KEY_EDGE, r=34):
        d.rounded_rectangle([px(x0), px(y0), px(x1), px(y1)], px(r), fill=fill, outline=edge, width=px(6))

    left, right = 176, SIZE - 176
    # Top row: one wide key + the knob (like the real K3 Pro)
    key(left, 168, 548, 318)
    cx, cy, r = 730, 243, 92
    d.ellipse([px(cx - r), px(cy - r), px(cx + r), px(cy + r)], fill=KNOB, outline=KNOB_RING, width=px(12))
    d.line([px(cx + 18), px(cy - 18), px(cx + 58), px(cy - 58)], fill=NOTCH, width=px(16))

    # 3×3 key grid, center key in the accent color
    top, bottom, gap = 362, 800, 30
    kw = (right - left - 2 * gap) / 3
    kh = (bottom - top - 2 * gap) / 3
    for row in range(3):
        for col in range(3):
            x0 = left + col * (kw + gap)
            y0 = top + row * (kh + gap)
            accent = (row, col) == (1, 1)
            key(x0, y0, x0 + kw, y0 + kh, ACCENT if accent else KEY, ACCENT_EDGE if accent else KEY_EDGE)

    # RGB strip
    y0, y1 = 840, 872
    strip = Image.new('RGBA', (px(right - left), px(y1 - y0)))
    sd = ImageDraw.Draw(strip)
    n = len(RGB) - 1
    for x in range(strip.width):
        t = x / (strip.width - 1) * n
        i = min(int(t), n - 1)
        sd.line([(x, 0), (x, strip.height)], fill=lerp(RGB[i], RGB[i + 1], t - i) + (255,))
    smask = Image.new('L', strip.size, 0)
    ImageDraw.Draw(smask).rounded_rectangle([0, 0, strip.width - 1, strip.height - 1], px(16), fill=255)
    img.paste(strip, (px(left), px(y0)), smask)

    return img.resize((SIZE, SIZE), Image.LANCZOS)


def main():
    icon = render()

    def out(rel):
        path = os.path.join(ROOT, *rel.split('/'))
        os.makedirs(os.path.dirname(path), exist_ok=True)
        return path

    icon.save(out('src/K3Pro.App/Assets/k3pro.ico'), sizes=[(s, s) for s in (16, 24, 32, 48, 64, 128, 256)])
    icon.resize((256, 256), Image.LANCZOS).save(out('src/K3Pro.App/Assets/k3pro.png'))
    icon.save(out('packaging/macos/K3Pro.icns'))
    icon.resize((512, 512), Image.LANCZOS).save(out('packaging/linux/k3pro.png'))
    icon.resize((256, 256), Image.LANCZOS).save(out('docs/images/icon.png'))
    print('icon written')


if __name__ == '__main__':
    main()
