"""Portaler picture font: renders every glyph as a PNG in the style of the PORTALER sign
(molten gold face, bevel, worn texture, dark outline, soft shadow)."""
import json, math, os
import numpy as np
from shapely.geometry import LineString, LinearRing, Polygon, Point
from shapely.ops import unary_union
from shapely import affinity
from PIL import Image, ImageDraw, ImageFilter

OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'art', 'font')
os.makedirs(OUT, exist_ok=True)
W = 23          # stroke width in design units (cap height = 140)
TOP, BOT = -24, 170   # vertical design range kept in every image (serifs, comma tail)
PX = 96         # image height in px for the cap height
SS = 4          # supersampling

# glyph = list of polylines ("lines"), closed rings ("rings"), filled polygons ("fills"), serif flag, advance padding
G = {
 'A': dict(lines=[[(0,140),(45,0),(90,140)], [(22,92),(68,92)]]),
 'B': dict(lines=[[(0,0),(0,140)], [(0,0),(58,0),(80,20),(80,48),(60,68),(0,68)], [(60,68),(86,90),(86,118),(64,140),(0,140)]]),
 'C': dict(lines=[[(86,18),(66,0),(24,0),(0,28),(0,112),(24,140),(66,140),(86,122)]]),
 'D': dict(lines=[[(0,0),(0,140)], [(0,0),(48,0),(86,36),(86,104),(48,140),(0,140)]]),
 'E': dict(lines=[[(78,0),(0,0),(0,140),(80,140)], [(0,70),(56,70)]]),
 'F': dict(lines=[[(78,0),(0,0),(0,140)], [(0,70),(56,70)]]),
 'G': dict(lines=[[(86,18),(66,0),(24,0),(0,28),(0,112),(24,140),(66,140),(88,118),(88,80),(48,80)]]),
 'H': dict(lines=[[(0,0),(0,140)], [(86,0),(86,140)], [(0,70),(86,70)]]),
 'I': dict(lines=[[(0,0),(0,140)]]),
 'J': dict(lines=[[(62,0),(62,112),(38,140),(18,140),(0,122)]]),
 'K': dict(lines=[[(0,0),(0,140)], [(80,0),(6,76)], [(32,52),(86,140)]]),
 'L': dict(lines=[[(0,0),(0,140),(76,140)]]),
 'M': dict(lines=[[(0,140),(0,0),(52,72),(104,0),(104,140)]]),
 'N': dict(lines=[[(0,140),(0,0),(86,140),(86,0)]]),
 'O': dict(rings=[[(46,0),(92,70),(46,140),(0,70)]]),
 'P': dict(lines=[[(0,140),(0,0),(60,0),(84,22),(84,52),(60,74),(0,74)]]),
 'Q': dict(rings=[[(46,0),(92,70),(46,140),(0,70)]], lines=[[(52,108),(88,152)]]),
 'R': dict(lines=[[(0,140),(0,0),(60,0),(84,22),(84,52),(60,74),(0,74)], [(42,74),(88,140)]]),
 'S': dict(lines=[[(84,14),(66,0),(20,0),(0,20),(0,48),(20,68),(66,72),(88,92),(88,120),(68,140),(20,140),(0,126)]]),
 'T': dict(lines=[[(0,0),(92,0)], [(46,0),(46,140)]]),
 'U': dict(lines=[[(0,0),(0,112),(24,140),(62,140),(86,112),(86,0)]]),
 'V': dict(lines=[[(0,0),(46,140),(92,0)]]),
 'W': dict(lines=[[(0,0),(26,140),(57,44),(88,140),(114,0)]]),
 'X': dict(lines=[[(0,0),(86,140)], [(86,0),(0,140)]]),
 'Y': dict(lines=[[(0,0),(46,70),(92,0)], [(46,70),(46,140)]]),
 'Z': dict(lines=[[(0,0),(86,0),(0,140),(86,140)]]),
 # digits
 '0': dict(rings=[[(22,0),(62,0),(84,28),(84,112),(62,140),(22,140),(0,112),(0,28)]], lines=[[(66,22),(18,118)]], serif=False),
 '1': dict(lines=[[(12,28),(40,0),(40,140)]]),
 '2': dict(lines=[[(2,22),(24,0),(60,0),(84,22),(84,52),(0,140),(86,140)]]),
 '3': dict(lines=[[(2,18),(22,0),(62,0),(84,22),(84,50),(62,70),(30,70)], [(62,70),(86,92),(86,120),(64,140),(22,140),(0,122)]]),
 '4': dict(lines=[[(64,140),(64,0),(0,98),(92,98)]]),
 '5': dict(lines=[[(84,0),(8,0),(4,66),(60,62),(86,86),(86,118),(64,140),(20,140),(0,124)]]),
 '6': dict(lines=[[(80,10),(58,0),(26,0),(0,30),(0,112),(26,140),(62,140),(86,116),(86,88),(62,66),(0,70)]]),
 '7': dict(lines=[[(0,0),(86,0),(30,140)]]),
 '8': dict(rings=[[(22,0),(62,0),(80,18),(80,48),(62,68),(22,68),(4,48),(4,18)],
                  [(20,68),(66,68),(86,90),(86,120),(66,140),(20,140),(0,120),(0,90)]], serif=False),
 '9': dict(lines=[[(6,130),(28,140),(60,140),(86,110),(86,28),(60,0),(24,0),(0,24),(0,52),(24,74),(86,70)]]),
 # punctuation
 '-': dict(lines=[[(0,82),(52,82)]], serif=False),
 '_': dict(lines=[[(0,150),(86,150)]], serif=False),
 '.': dict(fills=[[(13,112),(26,127),(13,142),(0,127)]]),
 ',': dict(fills=[[(13,112),(26,127),(10,160),(4,158),(9,140),(0,127)]]),
 ':': dict(fills=[[(13,44),(26,59),(13,74),(0,59)], [(13,112),(26,127),(13,142),(0,127)]]),
 '!': dict(lines=[[(13,0),(13,92)]], fills=[[(13,112),(26,127),(13,142),(0,127)]]),
 '?': dict(lines=[[(2,22),(24,0),(60,0),(82,22),(82,50),(44,78),(44,94)]], fills=[[(44,112),(57,127),(44,142),(31,127)]], serif=False),
 "'": dict(lines=[[(10,0),(10,42)]], serif=False),
 '(': dict(lines=[[(42,-10),(8,36),(8,104),(42,150)]], serif=False),
 ')': dict(lines=[[(0,-10),(34,36),(34,104),(0,150)]], serif=False),
 '+': dict(lines=[[(40,44),(40,124)], [(0,84),(80,84)]], serif=False),
 '/': dict(lines=[[(70,-4),(0,144)]], serif=False),
 '#': dict(lines=[[(30,20),(22,124)], [(66,20),(58,124)], [(4,52),(88,52)], [(0,94),(84,94)]], serif=False),
 '&': dict(lines=[[(92,140),(18,56),(10,36),(18,8),(40,0),(58,10),(62,34),(0,96),(0,118),(22,140),(52,140),(86,92)]], serif=False),
}
FILE = {'-':'dash','_':'underscore','.':'dot',',':'comma',':':'colon','!':'exclam','?':'question',"'":'apostrophe',
        '(':'parenleft',')':'parenright','+':'plus','/':'slash','#':'hash','&':'ampersand'}

def serif(p, q):
    dx, dy = q[0]-p[0], q[1]-p[1]; n = math.hypot(dx, dy); ux, uy = dx/n, dy/n; px, py = -uy, ux
    a, b, back = W*1.15, W*0.5, -W*0.5
    return Polygon([(p[0]+ux*back+px*a, p[1]+uy*back+py*a), (p[0]+ux*back-px*a, p[1]+uy*back-py*a),
                    (p[0]+ux*W*1.1-px*b, p[1]+uy*W*1.1-py*b), (p[0]+ux*W*1.1+px*b, p[1]+uy*W*1.1+py*b)])

def geometry(ch):
    s = G[ch]; parts = []
    lines = s.get('lines', []); use_serif = s.get('serif', True)
    for r in s.get('rings', []): parts.append(LinearRing(r).buffer(W/2, join_style=2, mitre_limit=2.2))
    for f in s.get('fills', []): parts.append(Polygon(f))
    others_all = [LineString(l) for l in lines] + [LinearRing(r) for r in s.get('rings', [])]
    for i, line in enumerate(lines):
        parts.append(LineString(line).buffer(W/2, cap_style=2, join_style=2, mitre_limit=2.2))
        if not use_serif: continue
        others = [o for j, o in enumerate(others_all) if j != i]
        for p, q in ((line[0], line[1]), (line[-1], line[-2])):
            if any(o.distance(Point(p)) < W for o in others): continue
            parts.append(serif(p, q))
    return unary_union(parts)

def rast(geom, w, h, ox, sc):
    img = Image.new('L', (w, h), 0); d = ImageDraw.Draw(img)
    polys = [geom] if geom.geom_type == 'Polygon' else list(geom.geoms)
    for p in polys:
        d.polygon([((x-ox)*sc, (y-TOP)*sc) for x, y in p.exterior.coords], fill=255)
        for hole in p.interiors: d.polygon([((x-ox)*sc, (y-TOP)*sc) for x, y in hole.coords], fill=0)
    return np.asarray(img).astype(np.float32) / 255

rng = np.random.RandomState(3)
def render(ch):
    g = geometry(ch); x0, _, x1, _ = g.bounds
    out_w = W*0.55   # dark outline thickness (design units)
    pad = out_w + 8
    sc = PX / 140 * SS
    w = int(math.ceil((x1 - x0 + 2*pad) * sc)); h = int(math.ceil((BOT - TOP) * sc))
    ox = x0 - pad
    face = rast(g, w, h, ox, sc)
    outline = rast(g.buffer(out_w, join_style=2, mitre_limit=2.5), w, h, ox, sc)
    # face colour: vertical gradient bright gold -> deep orange, per glyph cap height
    ys = (np.arange(h)[:, None] / sc + TOP)
    t = np.clip(ys / 140, 0, 1)
    top = np.array([255, 214, 110]); mid = np.array([236, 150, 42]); bot = np.array([150, 70, 18])
    col = np.where(t < 0.55, top + (mid-top)*(t/0.55), mid + (bot-mid)*((t-0.55)/0.45))
    col = np.broadcast_to(col[:, None, :], (h, w, 3)).astype(np.float32).copy()
    # bevel: light from upper-left, shade lower-right
    fb = np.asarray(Image.fromarray((face*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(5*SS/4))).astype(np.float32)/255
    gy, gx = np.gradient(fb)
    light = np.clip(-(gx + gy) * 9, -1, 1)
    edge = np.clip(1 - fb, 0, 1) * 2  # stronger near edges
    shade = light * np.clip(edge, 0, 1)
    col *= (1 + 0.55 * shade)[..., None]
    # worn metal texture
    n = rng.rand(h // 2 + 1, w // 2 + 1).astype(np.float32)
    n = np.asarray(Image.fromarray((n*255).astype(np.uint8)).resize((w, h), Image.BILINEAR).filter(ImageFilter.GaussianBlur(1.2*SS))).astype(np.float32)/255
    col *= (0.86 + 0.28 * n)[..., None]
    scratch = rng.rand(h, w) > 0.997
    sc_img = Image.fromarray((scratch*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(SS*0.7))
    col *= (1 - 0.5*np.asarray(sc_img, np.float32)/255*3).clip(0.5, 1)[..., None]
    col = np.clip(col, 0, 255)
    # compose: shadow, dark outline, face
    rgba = np.zeros((h, w, 4), np.float32)
    shadow = np.asarray(Image.fromarray((outline*255).astype(np.uint8)).filter(ImageFilter.GaussianBlur(3*SS)), np.float32)/255
    sh = np.zeros_like(shadow); dy, dx = int(4*SS), int(3*SS); sh[dy:, dx:] = shadow[:-dy, :-dx]
    a_sh = sh * 0.75
    rgba[..., :3] = np.array([10, 5, 2]); rgba[..., 3] = a_sh
    oc = np.array([38, 20, 8], np.float32)
    a = outline
    rgba[..., :3] = oc * a[..., None] + rgba[..., :3] * (1 - a[..., None]); rgba[..., 3] = a + rgba[..., 3] * (1 - a)
    rgba[..., :3] = col * face[..., None] + rgba[..., :3] * (1 - face[..., None]); rgba[..., 3] = face + rgba[..., 3] * (1 - face)
    img = Image.fromarray(np.clip(rgba, 0, 255).astype(np.uint8) if False else
                          np.dstack([np.clip(rgba[..., :3], 0, 255), np.clip(rgba[..., 3:]*255, 0, 255)]).astype(np.uint8), 'RGBA')
    img = img.resize((w // SS, h // SS), Image.LANCZOS)
    return img, (x1 - x0) * PX / 140

meta = {}
glyphs = {}
for ch in G:
    img, adv = render(ch)
    name = FILE.get(ch, ch if ch.isdigit() else ch)
    img.save(os.path.join(OUT, f'glyph_{name}.png'))
    glyphs[ch] = img
    meta[ch] = dict(file=f'glyph_{name}.png', w=img.width, h=img.height)
meta['_info'] = dict(capHeightPx=PX, baselinePx=round((140 - TOP) * PX / 140), lineHeightPx=round((BOT - TOP) * PX / 140), space=round(40 * PX / 140))
pass  # metrics are constants in src/PortalFont.cs

# ---------- preview sheet ----------
def word(text, scale=1.0, gap=-14):
    imgs = [glyphs.get(c) for c in text.upper()]
    tw = sum((im.width + gap) if im else meta['_info']['space'] for im in imgs)
    canvas = Image.new('RGBA', (max(tw, 1), meta['_info']['lineHeightPx']), (0, 0, 0, 0)); x = 0
    for im in imgs:
        if im is None: x += meta['_info']['space']; continue
        canvas.alpha_composite(im, (x, 0)); x += im.width + gap
    if scale != 1: canvas = canvas.resize((int(canvas.width*scale), int(canvas.height*scale)), Image.LANCZOS)
    return canvas

rows = ['ABCDEFGHIJ', 'KLMNOPQRST', 'UVWXYZ', '0123456789', "-._,:!?'()+/#&"]
SW = 1500
sheet = Image.new('RGBA', (SW, 1250), (34, 26, 20, 255))
d = ImageDraw.Draw(sheet)
for i in range(0, 1250, 18):  # faint wood grain
    d.line([(0, i), (SW, i + 6)], fill=(40, 30, 23, 255), width=8)
y = 20
for r in rows:
    wimg = word(r, 1.0, gap=4)
    sheet.alpha_composite(wimg, ((SW - wimg.width)//2, y)); y += wimg.height + 4
y += 20
for txt, s in (('Swamp Crypts', 0.75), ('BASE Plains - mine_2', 0.55), ('Home sweet home!', 0.35), ('Trader?', 0.22)):
    wimg = word(txt, s)
    sheet.alpha_composite(wimg, ((SW - wimg.width)//2, y)); y += wimg.height + 6
sheet = sheet.crop((0, 0, SW, y + 20))
sheet.convert('RGB').save(os.path.join(OUT, '..', '..', 'docs', 'font-preview.png'))
print(len(G), 'glyphs', meta['_info'], sheet.size)
