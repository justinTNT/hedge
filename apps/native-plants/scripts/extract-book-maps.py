#!/usr/bin/env python3
"""Extract reviewed 2022 distribution maps at native resolution (Pillow + pypdfium2)."""
from pathlib import Path
import argparse, hashlib, io, json
import pypdfium2 as pdfium
from PIL import ImageOps


def digest(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path.home() / 'Desktop/brocky')
    args = parser.parse_args()
    root = args.source.resolve(strict=True)
    manifest = json.loads((Path(__file__).resolve().parents[1] / 'data/book-distribution-maps.json').read_text())
    if manifest['version'] != 1:
        raise ValueError('Unsupported map manifest version')
    source = (root / manifest['sourcePath']).resolve(strict=True)
    derived = (root / manifest['derivedRoot']).resolve()
    if not source.is_relative_to(root) or not derived.is_relative_to(root) or derived == root:
        raise ValueError('Map source and outputs must stay inside the archive')
    if digest(source.read_bytes()) != manifest['sourceSha256']:
        raise ValueError('Source proof changed; review map allocations before extracting')
    outputs = [(root / item['outputPath']).resolve() for item in manifest['images']]
    if len(set(outputs)) != len(outputs):
        raise ValueError('Duplicate map output paths')
    for item, output in zip(manifest['images'], outputs):
        if not output.is_relative_to(derived) or output.suffix != '.png':
            raise ValueError(f'Invalid map output: {output}')
        if output.exists() and digest(output.read_bytes()) != item['sha256']:
            raise ValueError(f'Changed extraction; refusing to overwrite {output}')
    created = 0
    with pdfium.PdfDocument(str(source)) as pdf:
        for item, output in zip(manifest['images'], outputs):
            if output.exists():
                continue
            page = pdf[item['pdfPage'] - 1]
            try:
                obj = list(page.get_objects())[item['objectIndex']]
                if not isinstance(obj, pdfium.PdfImage):
                    raise ValueError(f'Map object changed: {item["reference"]}')
                bitmap = obj.get_bitmap().to_pil()
                if bitmap.mode not in ('1', 'L'):
                    raise ValueError(f'Expected monochrome map mask: {item["reference"]}')
                image = ImageOps.invert(bitmap.convert('L'))
                if image.size != (item['width'], item['height']):
                    raise ValueError(f'Map dimensions changed: {item["reference"]}')
                buffer = io.BytesIO()
                image.save(buffer, 'PNG')
                payload = buffer.getvalue()
                if digest(payload) != item['sha256']:
                    raise ValueError(f'Map pixels changed: {item["reference"]}')
                output.parent.mkdir(parents=True, exist_ok=True)
                with output.open('xb') as file:
                    file.write(payload)
                created += 1
            finally:
                page.close()
    print(f'{created} maps extracted; {len(outputs)-created} existing maps verified. Native resolution retained.')


if __name__ == '__main__':
    main()
