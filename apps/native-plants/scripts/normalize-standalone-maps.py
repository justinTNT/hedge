#!/usr/bin/env python3
"""Collect original 2026 maps and reproduce the reviewed display crops (Pillow)."""
from pathlib import Path
import argparse, hashlib, io, json
from PIL import Image, ImageOps

def digest(data): return hashlib.sha256(data).hexdigest()

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--source',type=Path,default=Path.home()/'Desktop/brocky')
    root=ap.parse_args().source.resolve(strict=True)
    manifest=json.loads((Path(__file__).resolve().parents[1]/'data/standalone-distribution-maps.json').read_text())
    if manifest['version']!=1: raise ValueError('Unsupported standalone map manifest')
    for item in manifest['images']:
        src=(root/item['sourcePath']).resolve(strict=True)
        original=(root/item['originalCollectionPath']).resolve()
        output=(root/item['outputPath']).resolve()
        if not all(p.is_relative_to(root) for p in [src,original,output]):raise ValueError('Map paths must stay inside archive')
        payload=src.read_bytes()
        if digest(payload)!=item['sourceSha256']:raise ValueError('Original map changed: '+str(src))
        if original.exists() and original.read_bytes()!=payload:
            raise ValueError('Refusing to overwrite changed original: '+str(original))
        if output.exists():
            if digest(output.read_bytes())!=item['sha256']:
                raise ValueError('Refusing to overwrite changed map: '+str(output))
            if not original.exists():
                original.parent.mkdir(parents=True,exist_ok=True)
                with original.open('xb') as f:f.write(payload)
            continue
        with Image.open(src) as raw:
            image=ImageOps.exif_transpose(raw).convert('L')
            l,t,r,b=item['crop']
            if not(0<=l<r<=image.width and 0<=t<b<=image.height):raise ValueError('Invalid map crop')
            image=image.crop((l,t,r,b));image.thumbnail((item['maxDimension'],item['maxDimension']),Image.Resampling.LANCZOS)
            if image.size!=(item['width'],item['height']):raise ValueError('Map dimensions changed')
            buf=io.BytesIO();image.save(buf,'PNG');derived=buf.getvalue()
        if digest(derived)!=item['sha256']:raise ValueError('Normalized PNG differs from reviewed output; use the extraction runtime recorded in MAP-REVIEW.md (image/PNG library builds can differ)')
        for path,content in [(original,payload),(output,derived)]:
            if path.exists():
                if path.read_bytes()!=content:raise ValueError('Refusing to overwrite changed map: '+str(path))
            else:
                path.parent.mkdir(parents=True,exist_ok=True)
                with path.open('xb') as f:f.write(content)
    print(f'{len(manifest["images"])} originals collected and normalized maps verified.')

if __name__=='__main__':main()
