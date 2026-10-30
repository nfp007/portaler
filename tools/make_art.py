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
    """Ring pin; with a category the icon sits big in the ring's open centre (readable on the map)."""
    S = 64  # same size as the plain ring, so pins keep their size on the map
    im = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    im.alpha_composite(ring, (0, 0))
    if key:
        size = 34
        icon = draw(key, size)
        # dark outline around the icon so it stands out on any map colour
        a = icon.getchannel('A')
        from PIL import ImageFilter
        halo = Image.new('RGBA', (size, size), (20, 10, 4, 255))
        halo.putalpha(a.filter(ImageFilter.MaxFilter(5)))
        x = (S - size) // 2
        im.alpha_composite(halo, (x, x))
        im.alpha_composite(icon, (x, x))
    return im

pin(None).save(os.path.join(ART, 'pin_none.png'))
for key, _ in CATS:
    if key != 'none':
        pin(key).save(os.path.join(ART, f'pin_{key}.png'))
print('done')
