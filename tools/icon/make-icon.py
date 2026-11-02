# GitKnife のアプリアイコンを作る：水色の丸、白い縁、中に上向きのナイフの刃（根元の細くなる所まで）。
# 形は Themes/Icons.xaml の 64x64 の座標の Geometry を読む。M / L / C / Z（絶対座標）だけを使う。
#   python -I tools/icon/make-icon.py            # src/GitKnife/Assets/GitKnife.ico を書く
#   python -I tools/icon/make-icon.py --preview  # 大きさ違いの一覧を .work/gitknife-icon-sizes.png に書く
import re
import sys
from pathlib import Path
from PIL import Image, ImageDraw, ImageFont

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / "src" / "GitKnife" / "Assets"

# 丸：中心 (32,32)、外の白い縁 r=31、中の水色 r=28
CENTER, OUTER, INNER = 32.0, 31.0, 28.0
BLUE = (66, 182, 236, 255)
WHITE = (255, 255, 255, 255)
BEVEL = (206, 233, 248, 255)

# 刃と研ぎ面の形は Themes/Icons.xaml の Icon.AppKnife / Icon.AppKnifeBevel から読む（ダイアログのアイコンと同じ形にする）
ICONS = (ROOT / "src" / "GitKnife" / "Themes" / "Icons.xaml").read_text(encoding="utf-8-sig")
geometry = lambda key: re.search(f'<Geometry x:Key="{key}">([^<]*)</Geometry>', ICONS).group(1)
BLADE = geometry("Icon.AppKnife")
BEVEL_PATH = geometry("Icon.AppKnifeBevel")

SS = 4  # 大きく描いて縮小し、縁をなめらかにする


def parse(path, scale, n=48):
    tokens = re.findall(r"[MLCZ]|-?[0-9.]+", path)
    polys, pts, i, cur = [], [], 0, (0.0, 0.0)
    while i < len(tokens):
        t = tokens[i]
        i += 1
        if t == "M":
            if pts:
                polys.append(pts)
            cur = (float(tokens[i]), float(tokens[i + 1])); i += 2
            pts = [cur]
        elif t == "L":
            cur = (float(tokens[i]), float(tokens[i + 1])); i += 2
            pts.append(cur)
        elif t == "C":
            p1 = (float(tokens[i]), float(tokens[i + 1]))
            p2 = (float(tokens[i + 2]), float(tokens[i + 3]))
            p3 = (float(tokens[i + 4]), float(tokens[i + 5])); i += 6
            p0 = cur
            for k in range(1, n + 1):
                s = k / n
                u = 1 - s
                pts.append((u**3 * p0[0] + 3 * u * u * s * p1[0] + 3 * u * s * s * p2[0] + s**3 * p3[0],
                            u**3 * p0[1] + 3 * u * u * s * p1[1] + 3 * u * s * s * p2[1] + s**3 * p3[1]))
            cur = p3
        elif t == "Z":
            polys.append(pts)
            pts = []
    if pts:
        polys.append(pts)
    return [[(x * scale, y * scale) for x, y in poly] for poly in polys]


def render(size):
    S = size * SS
    k = S / 64
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    c = CENTER * k
    d.ellipse((c - OUTER * k, c - OUTER * k, c + OUTER * k, c + OUTER * k), fill=WHITE)
    d.ellipse((c - INNER * k, c - INNER * k, c + INNER * k, c + INNER * k), fill=BLUE)

    blade = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    b = ImageDraw.Draw(blade)
    for poly in parse(BLADE, k):
        b.polygon(poly, fill=WHITE)
    for poly in parse(BEVEL_PATH, k):
        b.polygon(poly, fill=BEVEL)
    mask = Image.new("L", (S, S), 0)
    ImageDraw.Draw(mask).ellipse((c - INNER * k, c - INNER * k, c + INNER * k, c + INNER * k), fill=255)
    alpha = Image.composite(blade.getchannel("A"), Image.new("L", (S, S), 0), mask)
    blade.putalpha(alpha)
    img.alpha_composite(blade)
    return img.resize((size, size), Image.LANCZOS)


SIZES = [16, 20, 24, 32, 40, 48, 64, 128, 256]


def preview():
    big = [256, 128, 64]
    small = [48, 40, 32, 24, 20, 16]
    w = 40 + sum(s + 30 for s in big) + sum(s + 24 for s in small) + 20
    sheet = Image.new("RGBA", (w, 2 * 330), (0, 0, 0, 0))
    font = ImageFont.load_default()
    for row, bg in enumerate([(32, 32, 32, 255), (246, 246, 246, 255)]):
        y0 = row * 330
        ImageDraw.Draw(sheet).rectangle((0, y0, w, y0 + 330), fill=bg)
        fg = (220, 220, 220, 255) if row == 0 else (60, 60, 60, 255)
        x = 30
        for s in big + small:
            sheet.alpha_composite(render(s), (x, y0 + 30 + (256 - s)))
            ImageDraw.Draw(sheet).text((x, y0 + 296), f"{s}", fill=fg, font=font)
            x += s + (30 if s in big else 24)
    out = ROOT / ".work" / "gitknife-icon-sizes.png"
    sheet.save(out)
    print(out)


def main():
    if "--preview" in sys.argv:
        preview()
        return
    OUT.mkdir(parents=True, exist_ok=True)
    frames = [render(n) for n in SIZES]
    frames[-1].save(OUT / "GitKnife.ico", sizes=[(n, n) for n in SIZES], append_images=frames[:-1])


if __name__ == "__main__":
    main()
