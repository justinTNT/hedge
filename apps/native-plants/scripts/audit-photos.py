#!/usr/bin/env python3
"""Inventory media candidates across the whole archive. Never allocate or publish them."""
import argparse, collections, json, re, unicodedata
from pathlib import Path

def key(value):
    return re.sub(r'\s+', ' ', re.sub(r'[._]', ' ', unicodedata.normalize('NFKC',value))).strip().casefold()

def pattern(name):
    return re.compile(r'(?<![\w-])'+re.escape(key(name))+r'(?![\w-])')

def audit(root, catalogue, decisions):
    files=[(str(p.relative_to(root)),key(p.stem)) for p in sorted(root.rglob('*'))
           if p.is_file() and p.suffix.lower() in ('.jpg','.jpeg','.tif','.tiff','.png')]
    imported=collections.Counter(p['plant_id'] for p in catalogue['photos'])
    imported_paths={p['source_evidence'].split('; SHA256 ',1)[0] for p in catalogue['photos']}
    allocations={a['path']:a for a in decisions.get('photoAllocations',[])}
    holds=decisions.get('heldPhotoSpecies',{})
    accounts=[]
    for plant in catalogue['plants']:
        name=plant['scientific_name'];full=pattern(name)
        ranked=bool(re.search(r'\b(?:subsp|var)\.',name))
        base=pattern(' '.join(name.split()[:2])) if ranked else None
        candidates=[];rank_only=[]
        for path,filename in files:
            if full.search(filename):
                decision=allocations.get(path)
                state=('imported' if path in imported_paths else
                       'reviewed-selection' if decision and decision['include'] else
                       'reviewed-exclusion' if decision else 'unreviewed')
                candidates.append(dict(path=path,status=state,reason=decision['reason'] if decision else None))
            elif base and base.search(filename):
                rank_only.append(dict(path=path,status='reviewed' if path in allocations else 'unreviewed'))
        pending=sum(c['status']=='unreviewed' for c in candidates)
        reviewed=[dict(path=path,category=a.get('reviewCategory'),include=a['include'],reason=a['reason'])
                  for path,a in allocations.items() if a['plant']==name]
        rank_review=bool(rank_only) or any(a['category']=='rank-review' for a in reviewed)
        status=('illustrated' if imported[plant['id']] else 'editorial-hold' if name in holds else
                'unreviewed-full-name-candidates' if pending else 'rank-review' if rank_review else
                'reviewed-candidates-only' if candidates else 'no-full-name-filename-match')
        accounts.append(dict(id=plant['id'],plant=name,importedPhotos=imported[plant['id']],status=status,
                             fullNameCandidates=candidates,rankOnlyCandidates=rank_only,reviewedAllocations=reviewed,hold=holds.get(name)))
    return dict(imageFilesIndexed=len(files),accounts=accounts,
                statuses=dict(collections.Counter(a['status'] for a in accounts)),
                note='Source-import baseline, not live admin state. Filename candidates can be maps, duplicates or misidentifications. Rank-only matches are suggestions, never allocations. No filename match does not prove there is no photograph in the archive.')

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--source',type=Path,default=Path.home()/'Desktop/brocky');args=ap.parse_args()
    app=Path(__file__).resolve().parents[1];data=app/'data'
    result=audit(args.source.resolve(),json.loads((data/'catalogue-source.json').read_text()),json.loads((data/'editorial-decisions.json').read_text()))
    (data/'photo-audit.json').write_text(json.dumps(result,ensure_ascii=False,indent=2)+'\n')
    lines=['# Photo coverage audit','',result['note'],'',f"Indexed {result['imageFilesIndexed']} image files.",'',
           '| Unillustrated account | Review status | Full-name candidates | Rank-only candidates |','| --- | --- | ---: | ---: |']
    for a in result['accounts']:
        if not a['importedPhotos']:lines.append(f"| {a['plant']} | {a['status']} | {len(a['fullNameCandidates'])} | {len(a['rankOnlyCandidates'])} |")
    (data/'photo-audit.md').write_text('\n'.join(lines)+'\n')
    print(json.dumps({k:v for k,v in result.items() if k not in ('accounts','note')},indent=2))
    print('Detailed candidates: data/photo-audit.json; remaining coverage: data/photo-audit.md')

if __name__=='__main__': main()
