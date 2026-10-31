from pathlib import Path
from PIL import Image

root = Path('/home/ubuntu/work/NetworkScanner_CSharp_updated')
source = root / 'networkscanner_N_logo.png'
transparent_png = root / 'networkscanner_N_logo_transparent.png'
icon_path = root / 'networkscanner_N_logo.ico'

image = Image.open(source).convert('RGBA')
pixels = image.load()
width, height = image.size
for y in range(height):
    for x in range(width):
        r, g, b, a = pixels[x, y]
        # Generated temporary background is vivid magenta.
        if r > 180 and b > 150 and g < 100 and r - g > 100 and b - g > 80:
            pixels[x, y] = (0, 0, 0, 0)
        elif r > 150 and b > 120 and g < 130 and r - g > 70 and b - g > 50:
            # soften anti-aliased magenta fringe
            pixels[x, y] = (0, 0, 0, 0)

image.thumbnail((1024, 1024), Image.Resampling.LANCZOS)
image.save(transparent_png, format='PNG', optimize=True)
icon = image.resize((256, 256), Image.Resampling.LANCZOS)
icon.save(icon_path, format='ICO', sizes=[(16,16),(24,24),(32,32),(48,48),(64,64),(128,128),(256,256)])
print(f'created {transparent_png}')
print(f'created {icon_path}')
print(f'alpha extrema: {image.getchannel("A").getextrema()}')
