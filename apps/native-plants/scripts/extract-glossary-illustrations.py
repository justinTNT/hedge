#!/usr/bin/env python3
"""Reproduce reviewed glossary illustrations including their printed labels."""
from pathlib import Path
import argparse,hashlib,json,io
import pypdfium2 as pdfium

def main():
    ap=argparse.ArgumentParser(description=__doc__)
    ap.add_argument('--source',type=Path,default=Path.home()/'Desktop/brocky')
    root=ap.parse_args().source.resolve(strict=True)
    manifest=json.loads((Path(__file__).resolve().parents[1]/'data/glossary-illustrations.json').read_text())
    if manifest['version']!=1:raise ValueError('Unsupported glossary illustration manifest')
    source=(root/manifest['sourcePath']).resolve(strict=True)
    if not source.is_relative_to(root) or hashlib.sha256(source.read_bytes()).hexdigest()!=manifest['sourceSha256']:
        raise ValueError('Source proof changed')
    with pdfium.PdfDocument(source) as doc:
        for item in manifest['images']:
            output=(root/item['outputPath']).resolve()
            if not output.is_relative_to(root):raise ValueError('Invalid output path')
            if output.exists():
                if hashlib.sha256(output.read_bytes()).hexdigest()!=item['sha256']:raise ValueError('Existing illustration changed')
                continue
            page=doc[item['pdfPage']-1]
            try:
                image=page.render(scale=item['scale']).to_pil()
                image=image.crop(tuple(v*item['scale'] for v in item['crop']))
                buf=io.BytesIO();image.save(buf,'PNG');payload=buf.getvalue()
            finally:page.close()
            if hashlib.sha256(payload).hexdigest()!=item['sha256']:raise ValueError('Rendered illustration differs from reviewed output')
            output.parent.mkdir(parents=True,exist_ok=True)
            with output.open('xb') as f:f.write(payload)
    print('Glossary illustrations verified.')

if __name__=='__main__':main()
