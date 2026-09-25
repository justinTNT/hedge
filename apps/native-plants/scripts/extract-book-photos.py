#!/usr/bin/env python3
"""Recreate reviewed native-resolution book photographs in the local Brock archive.

Requires Pillow and pypdfium2. The manifest pins the PDF, image objects, crop
rectangles and output hashes. Existing files are verified, never overwritten.
"""
from __future__ import annotations
import argparse
import hashlib
import io
import json
from pathlib import Path

import pypdfium2 as pdfium


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--source', type=Path, default=Path.home() / 'Desktop/brocky')
    parser.add_argument('--manifest', type=Path,
                        default=Path(__file__).resolve().parents[1] / 'data/book-photo-extractions.json')
    args = parser.parse_args()
    root = args.source.resolve(strict=True)
    manifest = json.loads(args.manifest.read_text())
    if manifest['version'] != 1:
        raise ValueError('Unsupported book-extraction manifest version')
    source = (root / manifest['sourcePath']).resolve(strict=True)
    derived = (root / manifest['derivedRoot']).resolve()
    if not source.is_relative_to(root) or not derived.is_relative_to(root) or derived == root:
        raise ValueError('Source and derived paths must stay inside the archive')
    if sha256(source.read_bytes()) != manifest['sourceSha256']:
        raise ValueError('Book PDF has changed; review the page/object/crop manifest first')
    outputs = []
    for item in manifest['images']:
        path = (root / item['outputPath']).resolve()
        if not path.is_relative_to(derived) or path.suffix != '.png':
            raise ValueError(f'Invalid derived image path: {path}')
        if path.exists() and sha256(path.read_bytes()) != item['sha256']:
            raise ValueError(f'Existing extraction has changed; refusing to overwrite {path}')
        outputs.append(path)
    if len(set(outputs)) != len(outputs):
        raise ValueError('Duplicate extraction paths')

    images = {}
    created = verified = 0
    with pdfium.PdfDocument(str(source)) as pdf:
        for item, output in zip(manifest['images'], outputs):
            if output.exists():
                verified += 1
                continue
            key = (item['pdfPage'], item['objectIndex'])
            if key not in images:
                page = pdf[key[0] - 1]
                try:
                    obj = list(page.get_objects())[key[1]]
                    if not isinstance(obj, pdfium.PdfImage):
                        raise ValueError(f'Object is not an image: {key}')
                    images[key] = obj.get_bitmap().to_pil().convert('RGB')
                finally:
                    page.close()
            image = images[key]
            left, top, right, bottom = item['crop']
            if not (0 <= left < right <= image.width and 0 <= top < bottom <= image.height):
                raise ValueError(f'Crop is outside native image bounds: {item["reference"]}')
            cropped = image.crop((left, top, right, bottom))
            if cropped.size != (item['width'], item['height']):
                raise ValueError(f'Unexpected crop dimensions: {item["reference"]}')
            buffer = io.BytesIO()
            cropped.save(buffer, 'PNG')
            payload = buffer.getvalue()
            if sha256(payload) != item['sha256']:
                raise ValueError(f'Extraction differs from reviewed pixels: {item["reference"]}')
            output.parent.mkdir(parents=True, exist_ok=True)
            with output.open('xb') as file:
                file.write(payload)
            created += 1
    print(f'Book photographs: {created} extracted, {verified} existing files verified; '
          f'{len(manifest["images"])} total. Native resolution retained.')


if __name__ == '__main__':
    main()
