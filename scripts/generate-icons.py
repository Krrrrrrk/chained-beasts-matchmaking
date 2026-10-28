"""Regenerate embedded CC0 icon masks. Maintainer tool: Python + Pillow only."""
from pathlib import Path
from PIL import Image, ImageOps
import hashlib, json
root=Path(__file__).resolve().parents[1]
manifest=root/'assets/icons/manifest.json'
data=json.loads(manifest.read_text())
for icon in data['icons']:
    source=manifest.parent/icon['source']
    assert hashlib.sha256(source.read_bytes()).hexdigest()==icon['sourceSha256'], source
    alpha=Image.open(source).convert('RGBA').getchannel('A')
    alpha=alpha.crop(alpha.getbbox())
    alpha=alpha.resize((max(1,round(alpha.width*52/max(alpha.size))),max(1,round(alpha.height*52/max(alpha.size)))),Image.Resampling.NEAREST if 'emotes-pack' in icon['upstream'] else Image.Resampling.LANCZOS)
    mask=Image.new('L',(64,64))
    mask.paste(alpha,((64-alpha.width)//2,(64-alpha.height)//2))
    content=ImageOps.flip(mask).tobytes()
    assert hashlib.sha256(content).hexdigest()==icon['embeddedSha256'], icon['icon']
    (manifest.parent/icon['embedded']).write_bytes(content)
print('Regenerated five pinned CC0 masks; source and embedded hashes match manifest.')
