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
    """Ring pin; with a category a big round badge sits in the lower right corner."""
    S = 64  # same size as the plain ring, so pins keep their size on the map
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    im.alpha_composite(ring, (0, 0))
    if key:
        D = 43           # badge diameter (two thirds of the pin, readable at map size)
        big = 4          # supersample the badge circle
        b = Image.new('RGBA', (D * big, D * big), (0, 0, 0, 0))
        d = ImageDraw.Draw(b)
        d.ellipse([2 * big, 2 * big, (D - 2) * big, (D - 2) * big], fill=(30, 20, 12, 255), outline=(255, 200, 90, 255), width=3 * big)
        b = b.resize((D, D), Image.LANCZOS)
        icon = draw(key, 29)
        b.alpha_composite(icon, ((D - 29) // 2, (D - 29) // 2))
        im.alpha_composite(b, (S - D, S - D))
    return im

pin(None).save(os.path.join(ART, 'pin_none.png'))
for key, _ in CATS:
    if key != 'none':
        pin(key).save(os.path.join(ART, f'pin_{key}.png'))
print('done')
