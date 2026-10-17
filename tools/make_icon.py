"""Generates Veylix.ico: rounded square with a violet gradient and a white "V".
Usage: python tools/make_icon.py Veylix.ico   (needs Pillow)"""
import sys
from PIL import Image, ImageDraw

def render(size):
    S = 1024
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    # diagonal gradient #5B3FD6 -> #A68CFF
    grad = Image.new("RGBA", (S, S))
    a, b = (91, 63, 214), (166, 140, 255)
    px = grad.load()
    for y in range(S):
        for x in range(S):
            t = (x + y) / (2 * (S - 1))
            px[x, y] = tuple(int(a[i] + (b[i] - a[i]) * t) for i in range(3)) + (255,)
    mask = Image.new("L", (S, S), 0)
    m = int(S * 0.04)
    ImageDraw.Draw(mask).rounded_rectangle([m, m, S - m, S - m], radius=int(S * 0.27), fill=255)
    img.paste(grad, (0, 0), mask)
    # soft top highlight
    hl = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    hd = ImageDraw.Draw(hl)
    for y in range(int(S * 0.5)):
        alpha = int(70 * (1 - y / (S * 0.5)))
        hd.line([(0, y), (S, y)], fill=(255, 255, 255, alpha))
    img = Image.alpha_composite(img, Image.composite(hl, Image.new("RGBA", (S, S)), mask))
    # the V
    d = ImageDraw.Draw(img)
    w = int(S * 0.115)
    pts = [(S * 0.28, S * 0.30), (S * 0.50, S * 0.73), (S * 0.72, S * 0.30)]
    d.line(pts, fill=(255, 255, 255, 255), width=w, joint="curve")
    r = w // 2
    for (x, y) in pts:
        d.ellipse([x - r, y - r, x + r, y + r], fill=(255, 255, 255, 255))
    return img.resize((size, size), Image.LANCZOS)

if __name__ == "__main__":
    out = sys.argv[1] if len(sys.argv) > 1 else "Veylix.ico"
    big = render(256)
    big.save(out, format="ICO", sizes=[(16, 16), (20, 20), (24, 24), (32, 32), (40, 40), (48, 48), (64, 64), (128, 128), (256, 256)])
    big.save(out.replace(".ico", "-preview.png"))
    print("written", out)
