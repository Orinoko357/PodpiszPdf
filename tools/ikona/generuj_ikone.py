# Generator ikony PodpisPdf (styl Fluent 2). Użycie: python3 generuj_ikone.py ../../src/PodpisPdf/PodpisPdf.ico
# Tworzy też podglądy: ikona-1024.png, ikona-mala-1024.png (wersja dla 16–40 px), arkusz.png.
import math, sys
from PIL import Image, ImageDraw, ImageFilter, ImageChops

S = 1024
BRAND1 = (15, 108, 189, 255)    # Fluent brand blue #0F6CBD
BRAND2 = (91, 95, 199, 255)     # Fluent indigo #5B5FC7
FIOLET = (138, 92, 246, 255)

def gradient(c1, c2, angle=90, size=(S, S)):
    w, h = size
    a = math.radians(angle); dx, dy = math.cos(a), math.sin(a)
    proj = [x*dx + y*dy for x, y in ((0,0),(w,0),(0,h),(w,h))]
    lo, hi = min(proj), max(proj)
    # szybko: gradient 1D rozciągnięty i obrócony
    n = 512
    pas = Image.new('RGBA', (n, 1))
    for i in range(n):
        t = i/(n-1); pas.putpixel((i, 0), tuple(int(c1[k]+(c2[k]-c1[k])*t) for k in range(4)))
    d = int(math.hypot(w, h)) + 4
    g = pas.resize((d, d), Image.BILINEAR).rotate(-angle, resample=Image.BICUBIC)
    return g.crop(((d-w)//2, (d-h)//2, (d-w)//2 + w, (d-h)//2 + h))

def wklej(baza, wyp, maska):
    baza.alpha_composite(Image.composite(wyp, Image.new('RGBA', baza.size, (0,0,0,0)), maska))

def bezier(p0, p1, p2, p3, n=400):
    return [((1-t)**3*p0[0]+3*(1-t)**2*t*p1[0]+3*(1-t)*t**2*p2[0]+t**3*p3[0],
             (1-t)**3*p0[1]+3*(1-t)**2*t*p1[1]+3*(1-t)*t**2*p2[1]+t**3*p3[1]) for t in (i/n for i in range(n+1))]

def kreska(pts, gr):
    m = Image.new('L', (S*2, S*2), 0); d = ImageDraw.Draw(m); r = gr  # 2× nadpróbkowanie
    for x, y in pts:
        d.ellipse([2*x-r, 2*y-r, 2*x+r, 2*y+r], fill=255)
    return m.resize((S, S), Image.LANCZOS)

def kolo(cx, cy, r):
    m = Image.new('L', (S, S), 0); ImageDraw.Draw(m).ellipse([cx-r, cy-r, cx+r, cy+r], fill=255); return m

def cien(maska, rozmycie, przes, krycie):
    c = maska.filter(ImageFilter.GaussianBlur(rozmycie)).point(lambda v: int(v*krycie))
    m = Image.new('L', (S, S), 0); m.paste(c, (0, przes)); return m

def ikona(maly=False):
    img = Image.new('RGBA', (S, S), (0,0,0,0))
    # --- kartka ---
    if maly:
        x0, y0, x1, y1, R, zag = 150, 40, 800, 900, 120, 0
    else:
        x0, y0, x1, y1, R, zag = 132, 56, 744, 920, 104, 200
    kartka = Image.new('L', (S, S), 0)
    ImageDraw.Draw(kartka).rounded_rectangle([x0, y0, x1, y1], radius=R, fill=255)
    if zag:
        ciecie = Image.new('L', (S, S), 0)
        ImageDraw.Draw(ciecie).polygon([(x1-zag, y0-4), (x1+4, y0-4), (x1+4, y0+zag)], fill=255)
        kartka = ImageChops.subtract(kartka, ciecie)
    # plakietka z odstępem (wycięcie w kartce)
    bx, by, br = (712, 728, 250) if not maly else (700, 700, 300)
    odstep = 36 if not maly else 48
    kartka = ImageChops.subtract(kartka, kolo(bx, by, br + odstep))

    wklej(img, Image.new('RGBA', (S, S), (10, 30, 80, 255)), ImageChops.subtract(cien(kartka, 26, 22, 0.30), kolo(bx, by, br + odstep)))
    wklej(img, gradient((255,255,255,255), (225,233,247,255), 60), kartka)
    obw = ImageChops.subtract(kartka, kartka.filter(ImageFilter.MinFilter(5)))
    wklej(img, Image.new('RGBA', (S, S), (160, 178, 210, 255)), obw.point(lambda v: int(v*0.55)))
    if zag:
        z = Image.new('L', (S, S), 0)
        ImageDraw.Draw(z).rounded_rectangle([x1-zag-2, y0-40, x1+40, y0+zag+2], radius=44, fill=255)
        tr = Image.new('L', (S, S), 0)
        ImageDraw.Draw(tr).polygon([(x1-zag, y0), (x1-zag, y0+zag), (x1, y0+zag)], fill=255)
        z = ImageChops.multiply(z, tr)
        wklej(img, Image.new('RGBA', (S, S), (10, 30, 80, 255)), cien(z, 14, 8, 0.25))
        wklej(img, gradient((205,220,248,255), (160,184,236,255), 45), z)
        d = ImageDraw.Draw(img)
        for (yy, dl) in ((292, 300), (372, 380), (452, 240)):
            d.rounded_rectangle([x0+96, yy, x0+96+dl, yy+40], radius=20, fill=(197, 210, 234, 255))

    # --- podpis (zawijas) ---
    if not maly:  # w małych rozmiarach zawijas byłby tylko szumem
        pts = bezier((228, 640), (290, 480), (350, 780), (420, 610))
        wklej(img, gradient(BRAND1, FIOLET, 0), kreska(pts, 30))

    # --- plakietka: koło w gradiencie Fluent ze stalówką ---
    k = kolo(bx, by, br)
    wklej(img, Image.new('RGBA', (S, S), (10, 30, 80, 255)), ImageChops.multiply(cien(k, 14, 10, 0.28), kolo(bx, by + 6, br + 18)))
    wklej(img, gradient((40, 140, 230, 255), (88, 70, 210, 255), 50), k)
    # połysk górnej części (miękkie światło)
    blask = ImageChops.multiply(k, kolo(bx - br*0.25, by - br*0.55, int(br*0.95)))
    wklej(img, Image.new('RGBA', (S, S), (255, 255, 255, 255)), blask.filter(ImageFilter.GaussianBlur(30)).point(lambda v: int(v*0.16)))
    # stalówka (biała), skierowana w lewy dolny róg
    sk = br / 250
    stal = Image.new('L', (S*2, S*2), 0); d = ImageDraw.Draw(stal)
    def P(x, y):  # współrzędne stalówki w jednostkach (oś pionowa, czubek na dole) → obrót 45°
        a = math.radians(45)
        X, Y = x*sk, y*sk
        return (2*(bx + X*math.cos(a) - Y*math.sin(a)), 2*(by + X*math.sin(a) + Y*math.cos(a)))
    ksz = [(0, 150), (-78, 10), (-78, -60), (-46, -120), (46, -120), (78, -60), (78, 10)]
    d.polygon([P(x, y) for x, y in ksz], fill=255)
    # rozcięcie i otwór
    d.line([P(0, 140), P(0, -10)], fill=0, width=int(2*16*sk))
    r = 2*24*sk; cx, cy = P(0, -22); d.ellipse([cx-r, cy-r, cx+r, cy+r], fill=0)
    # górna obejma oddzielona szczeliną
    d.polygon([P(-46, -132), P(46, -132), P(46, -150), P(-46, -150)], fill=0)
    d.polygon([P(-58, -150), P(58, -150), P(58, -175), P(-58, -175)], fill=255)
    stal = stal.resize((S, S), Image.LANCZOS)
    wklej(img, Image.new('RGBA', (S, S), (255, 255, 255, 255)), stal)
    return img

duza = ikona(False); mala = ikona(True)
duza.save('ikona-1024.png'); mala.save('ikona-mala-1024.png')
rozmiary = [256, 128, 64, 48, 40, 32, 24, 20, 16]
obrazy = [(duza if r >= 48 else mala).resize((r, r), Image.LANCZOS) for r in rozmiary]
obrazy[0].save(sys.argv[1], format='ICO', sizes=[(r, r) for r in rozmiary], append_images=obrazy[1:])
ark = Image.new('RGBA', (1060, 600), (243, 243, 243, 255)); ark.paste((32, 32, 32, 255), (0, 300, 1060, 600))
x = 20
for r, o in zip(rozmiary, obrazy):
    ark.alpha_composite(o, (x, 150 - r//2)); ark.alpha_composite(o, (x, 450 - r//2)); x += r + 22
ark.save('arkusz.png')
# powiększenie małych rozmiarów (jak na ekranie 200%)
pow = Image.new('RGBA', (600, 140), (243, 243, 243, 255)); x = 10
for r in (16, 20, 24, 32):
    o = [ob for rr, ob in zip(rozmiary, obrazy) if rr == r][0].resize((r*4, r*4), Image.NEAREST)
    pow.alpha_composite(o, (x, 4)); x += r*4 + 12
pow.save('male-powiekszone.png')
