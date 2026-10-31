from pathlib import Path
from PIL import Image

root = Path('/home/ubuntu/work/NetworkScanner_CSharp_updated')
source = root / 'networkscanner_logo.png'
icon_path = root / 'networkscanner_logo.ico'
preview_path = root / 'networkscanner_logo_256.png'

image = Image.open(source).convert('RGBA')
image.thumbnail((1024, 1024), Image.Resampling.LANCZOS)
image.save(preview_path, format='PNG', optimize=True)

sizes = [16, 24, 32, 48, 64, 128, 256]
icon = image.resize((256, 256), Image.Resampling.LANCZOS)
icon.save(icon_path, format='ICO', sizes=[(size, size) for size in sizes])
print(f'created {icon_path}')
print(f'preview size: {image.size}')
print(f'alpha extrema: {image.getchannel("A").getextrema()}')
