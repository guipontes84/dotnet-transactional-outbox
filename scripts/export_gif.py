"""Exporta as seis etapas do SVG para um GIF de 18 segundos.
Requer: pip install pillow playwright; navegador Edge instalado.
"""
from pathlib import Path
from io import BytesIO
from PIL import Image
from playwright.sync_api import sync_playwright

ROOT = Path(__file__).resolve().parents[1]
SOURCE = ROOT / 'media/outbox-pix.svg'
OUTPUT = ROOT / 'media/outbox-pix.gif'
frames = []
with sync_playwright() as p:
    browser = p.chromium.launch(headless=True, channel='msedge')
    page = browser.new_page(viewport={'width': 1200, 'height': 1500}, device_scale_factor=1)
    page.goto(SOURCE.as_uri())
    page.evaluate('document.documentElement.pauseAnimations()')
    for i in range(6):
        page.evaluate('''t => {
            document.documentElement.setCurrentTime(t);
            for (const a of document.documentElement.getAnimations({subtree:true})) {
                a.pause(); a.currentTime = t * 1000;
            }
        }''', i * 3 + 1.5)
        frame = Image.open(BytesIO(page.screenshot())).convert('RGB')
        frame.save(ROOT / f'media/preview-{i}.png')
        frames.append(frame)
    browser.close()

# Paleta compartilhada por todas as etapas evita troca de cores entre quadros.
samples = Image.new('RGB', (600, 750 * 6))
for i, frame in enumerate(frames):
    samples.paste(frame.resize((600, 750)), (0, i * 750))
palette = samples.quantize(colors=224)
indexed = [f.quantize(palette=palette, dither=Image.Dither.NONE) for f in frames]
indexed[0].save(OUTPUT, save_all=True, append_images=indexed[1:],
                duration=3000, loop=0, disposal=1, optimize=True)
with Image.open(OUTPUT) as gif:
    assert gif.n_frames == 6
    assert sum((gif.seek(i) or gif.info['duration']) for i in range(6)) == 18000
    print(f'{OUTPUT}: {gif.n_frames} quadros, 18 segundos, {OUTPUT.stat().st_size:,} bytes')
