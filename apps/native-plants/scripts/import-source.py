#!/usr/bin/env python3
"""Mine Brock's structured manuscript and curated photographs; never modify the archive.

Imports are insert-only. Admin edits and publication decisions always win on re-import.
Full evidence and unresolved joins remain in local data files, never the public catalogue.
"""
from __future__ import annotations
import argparse, collections, hashlib, json, re, subprocess, unicodedata
from pathlib import Path
from zipfile import ZipFile
from xml.etree import ElementTree as ET
from html.parser import HTMLParser
from PIL import Image, ImageOps

W = '{http://schemas.openxmlformats.org/wordprocessingml/2006/main}'
LABELS = ['Aboriginal Uses', 'Male cones', 'Female cones', 'Male flowers', 'Female flowers',
          'Fertile fronds', 'Sterile fronds', 'Habit', 'Bark', 'Leaves', 'Phyllodes', 'Flowers',
          'Fruit', 'Flowering', 'Fruiting', 'Features', 'Habitat', 'Cult', 'Notes', 'Dist', 'Ref',
          'Fronds', 'Spores', 'Cones']
LABEL_RE = re.compile(r'(?<!\w)(' + '|'.join(LABELS) + r')\s*:', re.I)
FORMS = dict(AQ='Aquatic', BA='Bamboo', CL='Climber', CY='Cycad', FE='Fern', HE='Herb',
             MA='Mangrove', MI='Mistletoe', OR='Orchid', PA='Palm', SH='Shrub', TR='Tree')
SUN = dict(FSH='Full shade', PSH='Partial shade', FSU='Full sun')
WATER = dict(LW='Little water', MW='Moderate water', HW='High water')
FEATURES = dict(FL='Flower display', FRT='Interesting fruit', SFL='Form & foliage',
                GC='Groundcover', SCR='Screening', REV='Revegetation')
WILDLIFE = dict(BEWA='Bees & wasps', BIN='Nectar-feeding birds', BUMO='Butterflies & moths',
                BIC='Seed-eating birds', BIF='Fruit-eating birds')
CREDITS = dict(IM='Ian Morris', WB='William Burgess', RD='Russell Dempster', GF='Gary Fox')
FIELDS = {'Habit':'habit','Bark':'bark','Leaves':'leaves','Phyllodes':'phyllodes','Flowers':'flowers',
          'Fruit':'fruit','Flowering':'flowering','Fruiting':'fruiting','Features':'features',
          'Habitat':'habitat','Cult':'cultivation','Aboriginal Uses':'traditional_uses',
          'Notes':'notes','Dist':'distribution','Ref':'source_references'}

def clean(s): return re.sub(r'\s+', ' ', s.replace('\u00a0',' ')).strip()
def norm(s): return clean(unicodedata.normalize('NFKC', s)).casefold()
def slug(s): return re.sub(r'[^a-z0-9]+','-',norm(s)).strip('-')
def identity(s): return 'plant-' + hashlib.sha256(norm(s).encode()).hexdigest()[:16]
def accepted(e):
    if e.tag in (W+'del', W+'moveFrom'): return ''
    if e.tag == W+'t': return e.text or ''
    if e.tag in (W+'br', W+'tab'): return ' '
    return ''.join(accepted(c) for c in e)
def paragraphs(path):
    with ZipFile(path) as z: root = ET.fromstring(z.read('word/document.xml'))
    return [clean(accepted(p)) for p in root.findall('.//'+W+'body/'+W+'p')]
def sections(text):
    matches=list(LABEL_RE.finditer(text)); out={}
    for i,m in enumerate(matches):
        key=next(k for k in LABELS if k.casefold()==m.group(1).casefold())
        value=clean(text[m.end():matches[i+1].start() if i+1<len(matches) else len(text)])
        out[key]=clean(out.get(key,'')+' '+value)
    return out

class Cells(HTMLParser):
    def __init__(self): super().__init__(); self.rows=[]; self.row=[]; self.cell=None
    def handle_starttag(self,t,a):
        if t=='tr': self.row=[]
        if t in ('td','th'): self.cell=''
    def handle_data(self,s):
        if self.cell is not None:self.cell+=s
    def handle_endtag(self,t):
        if t in ('td','th'): self.row.append(clean(self.cell)); self.cell=None
        if t=='tr': self.rows.append(self.row)
def table(path):
    html=subprocess.run(['textutil','-convert','html','-stdout',str(path)],capture_output=True,text=True,check=True).stdout
    parser=Cells();parser.feed(html);return parser.rows
def codes(raw, mapping, issues, name, field):
    tokens=[t for t in re.split(r'[,\s/]+',raw.strip()) if t]
    unknown=[t for t in tokens if t not in mapping]
    if unknown: issues.append(dict(plant=name,kind='unknown-code',field=field,value=raw,unknown=unknown))
    return '|'.join(mapping[t] for t in tokens if t in mapping)

def scientific_heading(s):
    name=re.split(r'\s*\(prev\.?',s,flags=re.I)[0].strip()
    return bool(re.fullmatch(r'[A-Z][a-z]+ (?:[a-z][a-z-]+(?: (?:subsp\.|var\.) [a-z-]+)?|sp\..+)',name))

def extract(root,account_ids=None):
    account_ids=account_ids or {}
    source=next(root.rglob('3. NPNA 2026 PLANT DESCRIPTIONS.docx'))
    ps=paragraphs(source); anchors=[i for i,p in enumerate(ps) if p.startswith('Family:')]
    heads=[]
    for i in anchors:
        candidates=[j for j in range(max(0,i-7),i) if scientific_heading(ps[j])]
        if not candidates: raise ValueError(f'Missing heading before paragraph {i}: {ps[i-5:i+1]}')
        heads.append(candidates[-1])
    issues=[]; plants=[]
    for n,(h,a) in enumerate(zip(heads,anchors)):
        heading=ps[h]; name=clean(re.split(r'\s*\(prev\.?',heading,flags=re.I)[0]).rstrip('(').strip()
        aliases=re.findall(r'\(prev\.?\s*([^)]*)\)',heading,flags=re.I)
        end=heads[n+1] if n+1<len(heads) else len(ps)
        body=' '.join(p for p in ps[a+1:end] if p)
        desc=sections(body)
        if not desc.get('Habit'):issues.append(dict(plant=name,kind='missing-habit'))
        extra=[f'{k}: {v}' for k,v in desc.items() if k not in FIELDS]
        row=dict(id=account_ids.get(name,identity(name)),slug=slug(name),scientific_name=name,
            common_names=' | '.join(p for p in ps[h+1:a] if p),family=clean(ps[a].split(':',1)[1]).rstrip('.'),
            genus=name.split()[0],aliases=' | '.join(aliases),forms='',height='',sun='',water='',
            garden_features='',wildlife='',endemic_nt=False,published=True,sort_order=n,
            source_evidence=f'{source.relative_to(root)}; accepted revision view; heading paragraph {h}; proof comparison pending',
            created_at=1790118000,updated_at=None,deleted_at=None)
        for label,field in FIELDS.items():row[field]=desc.get(label,'')
        if extra:row['notes']=clean(row['notes']+' '+' '.join(extra))
        plants.append(row)
    if len({p['id'] for p in plants}) != len(plants):raise ValueError('Duplicate account identity')
    attrs=table(next(root.rglob('2.B NPNA 2026 TABLE OF PLANT ATTRIBUTES A.A.rtf')))
    byname=collections.defaultdict(list)
    for row in attrs:
        if len(row)!=12:raise ValueError(f'Unexpected table width {len(row)}')
        key=norm(re.split(r'\s*\(prev\.?',row[0],flags=re.I)[0])
        byname[key].append(row)
    used=set()
    for p in plants:
        candidates=byname.get(norm(p['scientific_name']),[])
        if len(candidates)!=1:
            issues.append(dict(plant=p['scientific_name'],kind='attribute-join-unresolved'));continue
        row=candidates[0];used.add(row[0]);p['height']=row[2]
        for field,index,mapping in [('forms',1,FORMS),('sun',6,SUN),('water',7,WATER),('garden_features',3,FEATURES)]:
            p[field]=codes(row[index],mapping,issues,p['scientific_name'],field)
        p['wildlife']=codes(row[9]+','+row[10],WILDLIFE,issues,p['scientific_name'],'wildlife')
    for row in attrs:
        if row[0] not in used:issues.append(dict(plant=row[0],kind='unmatched-attribute-row',values=row))
    back=paragraphs(next(root.rglob('4. NPNA 2026 REF, BIB, GLOSS, FAM LIST, ENDEM LIST, INDEX.docx')))
    start=next(i for i,s in enumerate(back) if s=='LIST OF SPECIES ENDEMIC TO THE NORTHERN TERRITORY')
    endemics=set()
    for line in back[start+1:]:
        if not line:continue
        if not re.match(r'^[A-Z][a-z]+ [a-z]',line):break
        endemics.add(norm(line))
    for p in plants:p['endemic_nt']=norm(p['scientific_name']) in endemics
    unmatched_endemics=endemics-{norm(p['scientific_name']) for p in plants}
    issues.extend(dict(kind='endemic-join-unresolved',name=name) for name in sorted(unmatched_endemics))
    return plants,issues,dict(accounts=len(plants),attributeRows=len(attrs),exactAttributeJoins=len(used),endemicSourceNames=len(endemics))

def photo_candidates(root,plants):
    photo_root=next(p for p in root.rglob('D. PLANT DESCRIPTIONS GENERA A-Z') if p.is_dir())
    names={norm(p['scientific_name']):p for p in plants}; found=[]; issues=[]
    for folder in sorted(photo_root.glob('*/*')):
        if not folder.is_dir():continue
        label=clean(re.sub(r'\s*\([^)]*\)','',folder.name));plant=names.get(norm(label))
        if not plant:continue
        for path in sorted(folder.rglob('*')):
            if path.suffix.lower() not in ('.jpg','.jpeg','.tif','.tiff'):continue
            if not any(p.upper()=='PICK' for p in path.relative_to(folder).parts):continue
            if any(p.upper() in ('EXTRA','EXTRAS') for p in path.relative_to(folder).parts):continue
            filename=norm(re.sub(r'[._]',' ',path.stem))
            exact=norm(label) in filename
            # Never resolve a shortened rank or conflicting folder label by fuzzy matching.
            if not exact:
                issues.append(dict(kind='photo-label-conflict-or-shortened',plant=plant['scientific_name'],path=str(path.relative_to(root))));continue
            names_in_file=[p['scientific_name'] for p in plants if norm(p['scientific_name']) in filename]
            if len(names_in_file)>1:
                issues.append(dict(kind='photo-multiple-taxa',plant=plant['scientific_name'],path=str(path.relative_to(root)),names=names_in_file));continue
            initials=re.findall(r'(?:[.\s])('+'|'.join(CREDITS)+r')(?:[.\s]|$)',path.name)
            credit=CREDITS[initials[-1]] if initials else 'Photographer not recorded'
            priority=0 if re.match(r'^1[. ](?!a)',path.name,re.I) else 1 if path.name.startswith('1') else 2
            found.append(dict(plantId=plant['id'],name=plant['scientific_name'],path=str(path.relative_to(root)),credit=credit,priority=priority))
    return found,issues

def q(v):
    if v is None:return 'NULL'
    if isinstance(v,bool):return '1' if v else '0'
    if isinstance(v,(int,float)):return str(v)
    return "'"+v.replace("'","''")+"'"
def insert(table,row):return f'INSERT OR IGNORE INTO {table} ('+','.join(row)+') VALUES ('+','.join(q(v) for v in row.values())+');'

def main():
    ap=argparse.ArgumentParser();ap.add_argument('--source',type=Path,default=Path.home()/'Desktop/brocky');ap.add_argument('--photos',type=int,default=1)
    ap.add_argument('--accept-source-change',action='store_true',help='Only use after reviewing data/source-change-report.json')
    args=ap.parse_args();root=args.source.resolve();app=Path(__file__).resolve().parents[1]
    data=app/'data';data.mkdir(exist_ok=True)
    source_files=[next(root.rglob(pattern)) for pattern in ['3. NPNA 2026 PLANT DESCRIPTIONS.docx','2.B NPNA 2026 TABLE OF PLANT ATTRIBUTES A.A.rtf','4. NPNA 2026 REF, BIB, GLOSS, FAM LIST, ENDEM LIST, INDEX.docx']]
    fingerprint={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in source_files}
    decisions=json.loads((data/'editorial-decisions.json').read_text()) if (data/'editorial-decisions.json').exists() else {}
    plants,issues,report=extract(root,decisions.get('accountIds',{}));candidates,photo_issues=photo_candidates(root,plants);issues+=photo_issues
    lock=data/'source-lock.json'
    if lock.exists() and json.loads(lock.read_text())!=fingerprint and not args.accept_source_change:
        prior=json.loads((data/'catalogue-source.json').read_text()) if (data/'catalogue-source.json').exists() else {'plants':[]}
        old={p['id']:p for p in prior['plants']};new={p['id']:p for p in plants}
        changes={'oldSources':json.loads(lock.read_text()),'newSources':fingerprint,'addedNames':[p['scientific_name'] for id,p in new.items() if id not in old],
                 'removedNames':[p['scientific_name'] for id,p in old.items() if id not in new],
                 'changedAccounts':[p['scientific_name'] for id,p in new.items() if id in old and p!=old[id]]}
        (data/'source-change-report.json').write_text(json.dumps(changes,ensure_ascii=False,indent=2))
        ap.error('Source files changed. Review data/source-change-report.json and reconcile names/IDs before --accept-source-change. Existing seed and edits have been preserved.')
    photos=[];media=app/'public/media';media.mkdir(parents=True,exist_ok=True)
    for p in plants:
        if p['scientific_name'] in decisions.get('heldPhotoSpecies',{}):
            issues.append(dict(kind='editorial-photo-hold',plant=p['scientific_name'],reason=decisions['heldPhotoSpecies'][p['scientific_name']]))
            continue
        chosen=sorted((c for c in candidates if c['plantId']==p['id']),key=lambda c:(c['priority'],c['path']))[:args.photos]
        for n,c in enumerate(chosen):
            src=root/c['path'];digest=hashlib.sha256(src.read_bytes()).hexdigest();key=digest[:20]
            try:
                with Image.open(src) as original:
                    im=ImageOps.exif_transpose(original).convert('RGB')
                    for width,label in [(640,'thumb'),(1600,'large')]:
                        out=media/f'{key}-{label}.webp'
                        if not out.exists():
                            copy=im.copy();copy.thumbnail((width,width));copy.save(out,'WEBP',quality=83)
                    width,height=im.size
            except Exception as e:
                issues.append(dict(kind='image-decode',path=c['path'],error=str(e)));continue
            photos.append(dict(id='photo-'+key,plant_id=p['id'],image=f'/media/{key}-large.webp',thumbnail=f'/media/{key}-thumb.webp',
                caption=p['scientific_name'],photographer=c['credit'],sort_order=n,published=True,
                source_evidence=f"{c['path']}; SHA256 {digest}; exact folder/filename agreement; PICK selection; editorial image review pending",
                created_at=1790118000,updated_at=None,deleted_at=None))
    owners=collections.defaultdict(set)
    for photo in photos:owners[photo['id']].add(photo['plant_id'])
    conflicts={key for key,ids in owners.items() if len(ids)>1}
    if conflicts:issues.extend(dict(kind='same-image-multiple-accounts',photoId=key,plantIds=sorted(owners[key])) for key in conflicts)
    photos=[p for p in photos if p['id'] not in conflicts]
    report.update(photos=len(photos),plantsWithPhotos=len({p['plant_id'] for p in photos}),families=len({p['family'] for p in plants}),
                  genera=len({p['genus'] for p in plants}),photoCandidates=len(candidates),issues=issues,
                  reviewStatus='Local prototype from accepted manuscript text. Final-proof comparison and editorial photo/credit review pending.')
    lock.write_text(json.dumps(fingerprint,indent=2)+'\n')
    (data/'catalogue-source.json').write_text(json.dumps(dict(plants=plants,photos=photos),ensure_ascii=False,indent=2))
    (data/'photo-candidates.json').write_text(json.dumps(candidates,ensure_ascii=False,indent=2))
    (data/'import-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    (data/'import.sql').write_text('-- Insert-only source import: existing admin edits and deletions are preserved.\nPRAGMA foreign_keys=ON;\n'+
        '\n'.join(insert('plants',p) for p in plants)+'\n'+'\n'.join(insert('plant_photos',p) for p in photos)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k!='issues'},indent=2));print(f'Editorial issues: {len(issues)} (private data/import-report.json)')

if __name__=='__main__':main()
