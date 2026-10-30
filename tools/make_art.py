"""Generates the category icons and map pin sprites in art/ (run: python tools/make_art.py)."""
import os, sys
from PIL import Image, ImageDraw
sys.path.insert(0, os.path.dirname(__file__))
from category_icons import draw, CATS

ART = os.path.join(os.path.dirname(__file__), '..', 'art')
ring = Image.open(os.path.join(ART, 'pin64.png')).convert('RGBA')

for key, _ in CATS:
    if key == 'none':
        continue
    draw(key, 64).save(os.path.join(ART, f'cat_{key}.png'))

def pin(key):
    S = 64  # same size as the plain ring, so pins keep their size on the map
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    im.alpha_composite(ring, (0, 0))
    if key:
        big = 4  # supersample the badge
        b = Image.new('RGBA', (30 * big, 30 * big), (0, 0, 0, 0))
        d = ImageDraw.Draw(b)
        d.ellipse([2 * big, 2 * big, 28 * big, 28 * big], fill=(30, 20, 12, 255), outline=(255, 200, 90, 255), width=3 * big)
        b = b.resize((30, 30), Image.LANCZOS)
        b.alpha_composite(draw(key, 18), (6, 6))
        im.alpha_composite(b, (S - 30, S - 30))
    return im

pin(None).save(os.path.join(ART, 'pin_none.png'))
for key, _ in CATS:
    if key != 'none':
        pin(key).save(os.path.join(ART, f'pin_{key}.png'))
print('done')
