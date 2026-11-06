"""MyLocalHost のアイコン（Resources/app.ico）を作る。

赤い角丸の四角に、サーバーの棚（3段）と ▶ のバッジ。色は Enarge のサイトに合わせている。
- 40px 以上: 大きな絵を縮小する
- 32px 以下: ピクセルの格子に合わせて、サイズごとに形を描き分ける（にじみを防ぐ）

使い方（リポジトリのルートで）:
    py tools/make_icon.py
必要なもの: Pillow（py -m pip install pillow）
"""
import struct
import sys
from io import BytesIO
from pathlib import Path

from PIL import Image, ImageDraw

ROOT = Path(__file__).resolve().parent.parent
ICO_PATH = ROOT / "MyLocalHost" / "Resources" / "app.ico"
PREVIEW_DIR = Path(sys.argv[1]) if len(sys.argv) > 1 else None

RED_TOP = (229, 106, 85)   # #e56a55
RED_BOT = (200, 67, 47)    # #c8432f
CREAM = (250, 244, 232)    # #faf4e8（Enarge の生成り色）
RED = (216, 80, 62)        # #d8503e（Enarge の赤）
SLOT = (232, 205, 190)

ICO_SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]

# 大きいサイズ用の形（1 を一辺とした割合）
LARGE = dict(
    margin=.05, radius=.21,
    bar_x=(.19, .69), bar_y0=.17, bar_h=.135, bar_gap=.045, bar_radius=.035,
    led_x=.075, led_r=.2, slot=True,
    badge_c=.715, badge_r=.20, badge_gap=.05,
)

# 小さいサイズ用の形（ピクセル単位。格子にそろえて、くっきり見せる）
SMALL = {
    16: dict(margin=0, radius=3.5, bar_x=(2, 11), bar_y0=2, bar_h=3, bar_gap=1, bar_radius=1,
             led=(3, 4), slot=False, badge_c=11.5, badge_r=4.5, badge_gap=1),
    20: dict(margin=0, radius=4.5, bar_x=(3, 14), bar_y0=3, bar_h=3, bar_gap=1, bar_radius=1,
             led=(4, 5), slot=False, badge_c=14.5, badge_r=5.5, badge_gap=1),
    24: dict(margin=1, radius=5, bar_x=(4, 16), bar_y0=4, bar_h=4, bar_gap=1, bar_radius=1,
             led=(5, 7), slot=False, badge_c=17, badge_r=6, badge_gap=1.5),
    32: dict(margin=1, radius=7, bar_x=(6, 23), bar_y0=6, bar_h=5, bar_gap=1.5, bar_radius=1.5,
             led=(8, 10), slot=False, badge_c=22.5, badge_r=8, badge_gap=2),
}


def draw_icon(g, size, ss):
    """g の値を size（ピクセル）に対する長さとして、size*ss の大きさで描く"""
    S = size * ss
    unit = S if g is LARGE else ss  # LARGE は割合、SMALL はピクセル

    def p(v):
        return v * unit

    # 背景（縦のグラデーションの角丸四角）
    grad = Image.new("RGBA", (S, S))
    gd = ImageDraw.Draw(grad)
    for y in range(S):
        t = y / (S - 1)
        c = tuple(round(RED_TOP[i] + (RED_BOT[i] - RED_TOP[i]) * t) for i in range(3))
        gd.line([(0, y), (S, y)], fill=c + (255,))
    shape = Image.new("L", (S, S), 0)
    end = (1 - g["margin"]) if g is LARGE else (size - g["margin"])
    ImageDraw.Draw(shape).rounded_rectangle(
        [p(g["margin"]), p(g["margin"]), p(end) - 1, p(end) - 1], radius=p(g["radius"]), fill=255)
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    img.paste(grad, (0, 0), shape)
    d = ImageDraw.Draw(img)

    # サーバーの棚 3 段
    x0, x1 = g["bar_x"]
    y = g["bar_y0"]
    for _ in range(3):
        h = g["bar_h"]
        d.rounded_rectangle([p(x0), p(y), p(x1) - 1, p(y + h) - 1], radius=p(g["bar_radius"]), fill=CREAM)
        if g is LARGE:
            lr = h * g["led_r"]
            lx, ly = x0 + g["led_x"], y + h / 2
            d.ellipse([p(lx - lr), p(ly - lr), p(lx + lr), p(ly + lr)], fill=RED)
            if g["slot"]:
                d.line([(p(x0 + .17), p(ly)), (p(x1 - .07), p(ly))], fill=SLOT, width=round(p(.022)))
        else:
            # ランプは 1 段の真ん中に置く四角
            la, lb = g["led"]
            lh = 1 if h % 2 else 2
            ly = y + (h - lh) / 2
            d.rectangle([p(la), p(ly), p(lb) - 1, p(ly + lh) - 1], fill=RED)
        y += h + g["bar_gap"]

    # ▶ バッジ（周りを背景色で切り抜く）
    c, r, gap = g["badge_c"], g["badge_r"], g["badge_gap"]
    ring = Image.new("L", (S, S), 0)
    ImageDraw.Draw(ring).ellipse([p(c - r - gap), p(c - r - gap), p(c + r + gap), p(c + r + gap)], fill=255)
    ring = Image.composite(ring, Image.new("L", (S, S), 0), shape)
    img.paste(grad, (0, 0), ring)
    d = ImageDraw.Draw(img)
    d.ellipse([p(c - r), p(c - r), p(c + r), p(c + r)], fill=CREAM)
    t = r * .52
    d.polygon([(p(c - t * .62), p(c - t)), (p(c - t * .62), p(c + t)), (p(c + t * 1.05), p(c))], fill=RED)
    return img


def render(size):
    if size in SMALL:
        # 細かく描いて、面積の平均で縮める（格子にそろった縁はくっきり残る）
        return draw_icon(SMALL[size], size, 16).resize((size, size), Image.BOX)
    return draw_icon(LARGE, 1024, 1).resize((size, size), Image.LANCZOS)


def write_ico(path, images):
    """各サイズを PNG のまま入れた .ico を書く"""
    blobs = []
    for im in images:
        buf = BytesIO()
        im.save(buf, "PNG", optimize=True)
        blobs.append(buf.getvalue())
    header = struct.pack("<HHH", 0, 1, len(images))
    offset = 6 + 16 * len(images)
    entries = b""
    for im, blob in zip(images, blobs):
        w = im.width if im.width < 256 else 0
        entries += struct.pack("<BBBBHHII", w, w, 0, 0, 1, 32, len(blob), offset)
        offset += len(blob)
    path.write_bytes(header + entries + b"".join(blobs))


def preview(images, out):
    """明るい背景と暗い背景に並べた確認用の画像"""
    pad = 12
    W = sum(im.width for im in images) + pad * (len(images) + 1)
    H = 256 + pad * 2
    sheet = Image.new("RGB", (W, H * 2 + 200), (255, 255, 255))
    for row, bg in enumerate(((243, 243, 243), (32, 32, 32))):
        ImageDraw.Draw(sheet).rectangle([0, row * H, W, row * H + H - 1], fill=bg)
        x = pad
        for im in images:
            sheet.paste(im, (x, row * H + (H - im.height) // 2), im)
            x += im.width + pad
    # 小さいサイズを 8 倍に拡大（ピクセルの様子を確認する）
    x = pad
    for s in (16, 20, 24):
        im = next(i for i in images if i.width == s)
        big = Image.new("RGBA", im.size, (32, 32, 32, 255))
        big.alpha_composite(im)
        big = big.resize((s * 8, s * 8), Image.NEAREST)
        sheet.paste(big, (x, H * 2 + 4))
        x += s * 8 + pad
    sheet.save(out)


def main():
    images = [render(s) for s in ICO_SIZES]
    write_ico(ICO_PATH, images)
    print("wrote", ICO_PATH)
    if PREVIEW_DIR:
        PREVIEW_DIR.mkdir(parents=True, exist_ok=True)
        preview(images, PREVIEW_DIR / "icon_final_preview.png")
        images[-1].save(PREVIEW_DIR / "icon_256.png")
        print("preview", PREVIEW_DIR / "icon_final_preview.png")


if __name__ == "__main__":
    main()
