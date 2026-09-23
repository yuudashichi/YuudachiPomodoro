from pathlib import Path
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[1]
dest = root / 'src' / 'XiliPomodoro' / 'Assets'
dest.mkdir(parents=True, exist_ok=True)
im = Image.new('RGBA', (256, 256))
d = ImageDraw.Draw(im)
d.rounded_rectangle((4, 4, 252, 252), radius=66, fill='#adb995')
d.ellipse((57, 68, 199, 210), fill='#f0f2e5', outline='#303d2a', width=11)
d.pieslice((32, 28, 104, 100), 180, 315, fill='#303d2a')
d.pieslice((152, 28, 224, 100), 225, 360, fill='#303d2a')
d.line((128, 104, 128, 141, 163, 162), fill='#303d2a', width=11)
d.line((78, 199, 57, 226), fill='#303d2a', width=11)
d.line((178, 199, 199, 226), fill='#303d2a', width=11)
im.save(dest / 'XiliPomodoro.ico', sizes=[(16,16), (24,24), (32,32), (48,48), (64,64), (128,128), (256,256)])
