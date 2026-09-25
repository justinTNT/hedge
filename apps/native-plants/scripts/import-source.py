#!/usr/bin/env python3
"""Mine Brock's structured manuscript and curated photographs; never modify the archive.

Records are inserted once. Explicit media corrections use conditional updates; unrelated admin edits remain intact.
Full evidence and unresolved joins remain in local data files, never the public catalogue.
"""
from __future__ import annotations
import argparse, collections, hashlib, io, json, re, subprocess, unicodedata
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
CREDITS = dict(IM='Ian Morris', WB='William Burgess', RD='Russell Dempster', GF='Gary Fox',
    KB='Kym Brennan', IC='Ian Cowie', DH='David Hancock', DL='Diane Lucas', AM='Anita Meadows',
    KM='Keira Meadows', LP='Leigh Patterson', JP='Julia Perdevich', TR='Tissa Ratnayeke',
    JRS='Jeremy Russell-Smith', NS='Nic Smith', BS='Ben Stuckey', AW='Aiden Webb')
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

def photo_key(s): return norm(re.sub(r'[._]', ' ', s))
def has_photo_label(filename,label):
    return bool(re.search(r'(?<![\w-])'+re.escape(photo_key(label))+r'(?![\w-])',photo_key(filename)))

def photo_credit(filename):
    initials=re.findall(r'(?:^|[._\s-])('+'|'.join(CREDITS)+r')(?=\d|[._\s-]|$)',filename)
    return CREDITS[initials[-1]] if initials else 'Photographer not recorded'

def photo_candidates(root,plants,decisions=None):
    decisions=decisions or {}
    photo_root=next(p for p in root.rglob('D. PLANT DESCRIPTIONS GENERA A-Z') if p.is_dir())
    names={photo_key(p['scientific_name']):p for p in plants}; found=[]; issues=[]
    if len(names)!=len(plants): raise ValueError('Ambiguous normalized photo account names')
    aliases={photo_key(label):decision for label,decision in decisions.get('photoNameAliases',{}).items()}
    labels=dict(names)
    for label,decision in aliases.items():
        plant=names[photo_key(decision['plant'])]
        if label in labels and labels[label]['id']!=plant['id']: raise ValueError(f'Conflicting photo alias: {label}')
        if not decision['reason']: raise ValueError(f'Missing photo alias evidence: {label}')
        labels[label]=plant
    for folder in sorted(photo_root.glob('*/*')):
        if not folder.is_dir():continue
        paths=[path for path in sorted(folder.rglob('*')) if path.is_file()
               and path.suffix.lower() in ('.jpg','.jpeg','.tif','.tiff')
               and any(p.upper()=='PICK' for p in path.relative_to(folder).parts)
               and not any(p.upper() in ('EXTRA','EXTRAS') for p in path.relative_to(folder).parts)]
        label=clean(re.sub(r'\s*\([^)]*\)','',folder.name));plant=labels.get(photo_key(label))
        if not plant:
            if paths: issues.append(dict(kind='photo-folder-unmatched',label=label,path=str(folder.relative_to(root)),
                                         photos=[str(p.relative_to(root)) for p in paths]))
            continue
        for path in paths:
            # Never resolve a shortened rank or conflicting folder label by fuzzy matching.
            if not has_photo_label(path.stem,label):
                issues.append(dict(kind='photo-label-conflict-or-shortened',plant=plant['scientific_name'],path=str(path.relative_to(root))));continue
            names_in_file=sorted({p['scientific_name'] for name,p in labels.items() if has_photo_label(path.stem,name)})
            if len(names_in_file)>1:
                issues.append(dict(kind='photo-multiple-taxa',plant=plant['scientific_name'],path=str(path.relative_to(root)),names=names_in_file));continue
            priority=0 if re.match(r'^1[. ](?!a)',path.name,re.I) else 1 if path.name.startswith('1') else 2
            alias=aliases.get(photo_key(label))
            evidence=('reviewed photo-label alias: '+alias['reason']) if alias else 'account/folder/filename agreement after punctuation normalization'
            found.append(dict(plantId=plant['id'],name=plant['scientific_name'],path=str(path.relative_to(root)),
                              credit=photo_credit(path.name),priority=priority,evidence='PICK selection; '+evidence))
    # Other archive areas require individual, hash-pinned decisions, never a fuzzy/global scan.
    allocations=decisions.get('photoAllocations',[])
    allocation_paths={a['path'] for a in allocations}
    if len(allocation_paths)!=len(allocations): raise ValueError('Duplicate photo allocation paths')
    unresolved=[]
    for issue in issues:
        if issue['kind']=='photo-folder-unmatched':
            issue={**issue,'photos':[p for p in issue['photos'] if p not in allocation_paths]}
            if not issue['photos']: continue
        if issue.get('path') not in allocation_paths: unresolved.append(issue)
    issues=unresolved
    for allocation in allocations:
        path=(root/allocation['path']).resolve()
        if not path.is_relative_to(root.resolve()) or not path.is_file(): raise ValueError(f'Invalid photo allocation path: {allocation["path"]}')
        if hashlib.sha256(path.read_bytes()).hexdigest()!=allocation['sha256']: raise ValueError(f'Photo allocation changed; review required: {allocation["path"]}')
        plant=names[photo_key(allocation['plant'])]
        if not allocation['reason']: raise ValueError(f'Missing photo allocation evidence: {path}')
        if not allocation['include']:
            issues.append(dict(kind='editorial-photo-exclusion',plant=plant['scientific_name'],path=allocation['path'],reason=allocation['reason']))
            found=[c for c in found if c['path']!=allocation['path']]
            continue
        if any(c['path']==allocation['path'] for c in found): raise ValueError(f'Duplicate photo allocation: {path}')
        caption=allocation.get('caption',plant['scientific_name'])
        if not isinstance(caption,str) or not caption.strip(): raise ValueError(f'Invalid photo allocation caption: {path}')
        photo_id=allocation.get('photoId')
        if photo_id is not None and not re.fullmatch(r'photo-[0-9a-f]{20}',photo_id): raise ValueError('Invalid pinned photo identity')
        found.append(dict(plantId=plant['id'],name=plant['scientific_name'],path=allocation['path'],credit=allocation['credit'],caption=caption,photoId=photo_id,
                          priority=allocation.get('priority',3),evidence='Explicit photo allocation: '+allocation['reason']))
    return found,issues

def transparent_map_gif(payload):
    """Keep dark map ink, make paper transparent; no dithering or resizing.

    2022 stencil maps are binary, so their ink pixels remain exact. The grayscale
    2026 scans retain pixels below 192 as black, discarding the pale paper/fringe.
    GIF only supports binary transparency: no invented speckled/dithered marks.
    """
    with Image.open(io.BytesIO(payload)) as original:
        ink=original.convert('L').point(lambda value: int(value<192), mode='P')
        ink.putpalette([255,255,255,0,0,0])
        output=io.BytesIO()
        ink.save(output,'GIF',transparency=0,optimize=False)
    return output.getvalue()


def distribution_maps(root, plants, manifest, media):
    """Explicit map allocations, kept separate from photographs and hero selection."""
    if manifest.get('version') != 1: raise ValueError('Unsupported distribution map manifest')
    plant_ids={p['id'] for p in plants}; rows=[]; seen=set()
    for item in manifest['images']:
        if item['plantId'] is None: continue
        if item['plantId'] not in plant_ids: raise ValueError('Unknown map account: '+item['plantId'])
        src=(root/item['outputPath']).resolve()
        if not src.is_relative_to(root.resolve()) or not src.is_file(): raise ValueError('Invalid map path: '+str(src))
        payload=src.read_bytes(); digest=hashlib.sha256(payload).hexdigest()
        if digest!=item['sha256']: raise ValueError('Map changed; review required: '+item['outputPath'])
        key='map-'+hashlib.sha256((item['plantId']+'|'+item['reference']).encode()).hexdigest()[:20]
        if key in seen: raise ValueError('Duplicate map allocation: '+item['reference'])
        seen.add(key)
        display=transparent_map_gif(payload)
        image=media/'maps'/(digest[:20]+'.gif'); image.parent.mkdir(parents=True,exist_ok=True)
        if image.exists() and image.read_bytes()!=display: raise ValueError('Existing map asset differs: '+str(image))
        if not image.exists(): image.write_bytes(display)
        rows.append(dict(id=key,plant_id=item['plantId'],image='/media/maps/'+image.name,
            caption=item['caption'],source_label=item['sourceLabel'],published=item.get('published',True),sort_order=item.get('sortOrder',0),
            source_evidence=item.get('sourceEvidence') or f"{manifest['sourcePath']}; SHA256 {manifest['sourceSha256']}; PDF page {item['pdfPage']}; object {item['objectIndex']}; {item['reason']}",
            created_at=1790204400,updated_at=None,deleted_at=None))
    return rows


def q(v):
    if v is None:return 'NULL'
    if isinstance(v,bool):return '1' if v else '0'
    if isinstance(v,(int,float)):return str(v)
    return "'"+v.replace("'","''")+"'"
def insert(table,row):return f'INSERT OR IGNORE INTO {table} ('+','.join(row)+') VALUES ('+','.join(q(v) for v in row.values())+');'

def reviewed_media_updates(photos, decisions, maps, standalone):
    """Apply reviewed media corrections once, preserving owner captions and choices."""
    statements=[]; byid={p['id']:p for p in photos}
    for allocation in decisions.get('photoAllocations',[]):
        old=allocation.get('replacesSha256')
        if not old:continue
        photo=byid[allocation['photoId']]
        note='; Reviewed crop correction: '+allocation['path']+'; SHA256 '+allocation['sha256']
        statements.append('UPDATE plant_photos SET image='+q(photo['image'])+',thumbnail='+q(photo['thumbnail'])+
            ',source_evidence=source_evidence||'+q(note)+' WHERE id='+q(photo['id'])+
            ' AND image='+q('/media/'+old[:20]+'-large.webp')+' AND thumbnail='+q('/media/'+old[:20]+'-thumb.webp')+';')
    for item in decisions.get('reclassifiedMapPhotos',[]):
        marker='[classified as distribution map]'
        statements.append('UPDATE plant_photos SET published=0,source_evidence=source_evidence||'+q('; '+marker+' '+item['reason'])+
            ' WHERE id='+q(item['photoId'])+' AND instr(source_evidence,'+q(marker)+')=0;')
    # Convert only the reviewed PNG URL; preserve owner replacements and all metadata.
    for item in maps:
        if item['image'].endswith('.gif'):
            old=item['image'][:-4]+'.png'
            statements.append('UPDATE plant_maps SET image='+q(item['image'])+' WHERE id='+q(item['id'])+' AND image='+q(old)+';')
    map_byid={m['id']:m for m in maps}
    for item in standalone.get('supersedesBookMaps',[]):
        key='map-'+hashlib.sha256((item['plantId']+'|'+item['reference']).encode()).hexdigest()[:20]
        old=map_byid[key];marker='[superseded by 2026 standalone map]'
        statements.append('UPDATE plant_maps SET published=0,source_evidence=source_evidence||'+q('; '+marker)+
            ' WHERE id='+q(key)+' AND image='+q(old['image'])+' AND instr(source_evidence,'+q(marker)+')=0;')
    for photo in photos:
        if photo['photographer'] not in ('','Photographer not recorded'):
            statements.append('UPDATE plant_photos SET photographer='+q(photo['photographer'])+' WHERE id='+q(photo['id'])+
                " AND photographer='Photographer not recorded';")
    return '\n'.join(statements)


def reference_material(root, media):
    """Import the complete accepted glossary and source lists, retaining editorial gaps."""
    path=next(root.rglob('4. NPNA 2026 REF, BIB, GLOSS, FAM LIST, ENDEM LIST, INDEX.docx'))
    ps=paragraphs(path); digest=hashlib.sha256(path.read_bytes()).hexdigest()
    bib=ps.index('BIBLIOGRAPHY'); glossary_start=ps.index('GLOSSARY'); glossary_end=ps.index('FAMILY LIST')
    evidence=lambda i:f'{path.relative_to(root)}; SHA256 {digest}; accepted paragraph {i}'
    common=dict(published=True,created_at=1790204400,updated_at=None,deleted_at=None)
    references=[]; glossary=[]; issues=[]
    for i in range(1,bib):
        if not ps[i]:continue
        match=re.fullmatch(r'(\d+)\.\s*(.+)',ps[i])
        if not match:raise ValueError('Unparsed usage reference: '+ps[i])
        number=int(match[1]); key='usage-'+str(number)
        references.append(dict(id=key,source_key=key,kind='usage',number=number,citation=match[2],aliases='',
                               source_label='John Brock · 2026 source collection',source_evidence=evidence(i),sort_order=number,**common))
    numbers={r['number'] for r in references}
    if len(numbers)!=len(references):raise ValueError('Duplicate usage reference numbers')
    for number in range(1,max(numbers)+1):
        if number not in numbers:issues.append(dict(kind='missing-numbered-reference',number=number))
    for i in range(bib+1,glossary_start):
        if not ps[i]:continue
        # Preserve paragraph boundaries: some source entries are merged or incomplete.
        key='bibliography-'+hashlib.sha256(ps[i].encode()).hexdigest()[:16]
        aliases=[]
        for marker,terms in [
            ('Flora NT online:', ['Flora NT']),
            ('Flora of Australia, various', ['Flora of Australia']),
            ('Flora of Australia online:', ['Flora of Australia online']),
            ('Flora of the Darwin Region Vol. 2', ['Flora of the Darwin Region']),
            ('An-Me Arri-Ngun', ['An-Me Arri-Ngun, The Food We Eat']),
            ('Plants of Cape York, the compact guide', ['Plants of Cape York The Compact Guide']),
            ('Native Plants for Northern Australian Gardens', ['Native Plants for Northern Australian Gardens']),
            ('Mangroves of the Northern Territory, Australia, Identification', ['Mangroves of the NT Australia. Identification and Traditional Use']),
        ]:
            if marker in ps[i]:aliases+=terms
        references.append(dict(id=key,source_key=key,kind='bibliography',number=0,citation=ps[i],aliases='|'.join(aliases),
                               source_label='John Brock · 2026 bibliography',source_evidence=evidence(i),sort_order=i,**common))
        if ('Darwin.Midgley' in ps[i] or ps[i].endswith(' and') or ps[i].count('Byrnes, N. B.')>1):
            issues.append(dict(kind='bibliography-editorial-review',paragraph=i,text=ps[i]))
    plurals={
        'Anther':'Anthers','Apex':'Apices','Aril':'Arils','Axil':'Axils','Berry':'Berries','Blade':'Blades',
        'Bulb':'Bulbs','Bract':'Bracts','Buttress':'Buttresses','Calyx':'Calyces','Capsule':'Capsules','Carpel':'Carpels',
        'Cladode':'Cladodes','Compound leaf':'Compound leaves','Cone':'Cones','Drupe':'Drupes','Epiphyte':'Epiphytes',
        'Family':'Families','Female flower':'Female flowers','Filament':'Filaments','Flower':'Flowers','Follicle':'Follicles',
        'Frond':'Fronds','Fruit':'Fruits','Funicle':'Funicles','Genus':'Genera','Geophyte':'Geophytes','Gland':'Glands',
        'Habit':'Habits','Habitat':'Habitats','Head':'Heads','Herb':'Herbs','Inflorescence':'Inflorescences',
        'Leaflet':'Leaflets','Legume':'Legumes','Lignotuber':'Lignotubers','Lobe':'Lobes','Male flower':'Male flowers',
        'Midrib':'Midribs','Nut':'Nuts','Panicle':'Panicles','Parasite':'Parasites','Peaflower':'Peaflowers',
        'Perennial':'Perennials','Petal':'Petals','Phyllode':'Phyllodes','Pistil':'Pistils','Pod':'Pods','Raceme':'Racemes',
        'Rhizome':'Rhizomes','Rosette':'Rosettes','Sepal':'Sepals','Shrub':'Shrubs','Spike':'Spikes','Stamen':'Stamens',
        'Stem':'Stems','Style':'Styles','Stigma':'Stigmas','Tree':'Trees','Tuber':'Tubers','Umbel':'Umbels','Whorl':'Whorls'}
    alternatives={'Alluvium':['alluvial'],'Blade':['lamina','laminae'],'Bi-pinnate':['bipinnate'],
                  'Midrib':['mid-vein','midvein','mid-veins','midveins'],'Subspecies':['subsp.','ssp.'],
                  'Peaflower':['pea flower','pea flowers'],'Domatia':['domatium']}
    for i in range(glossary_start+1,glossary_end):
        if not ps[i]:continue
        label,separator,definition=ps[i].partition(':')
        if not separator or not definition.strip():raise ValueError('Unparsed glossary entry: '+ps[i])
        term=re.sub(r'\s*\([^)]*\)','',label).strip()
        aliases=([plurals[term]] if term in plurals else [])+alternatives.get(term,[])
        glossary.append(dict(id='glossary-'+slug(term),term=term,aliases='|'.join(aliases),definition=definition.strip(),illustration='',
                             source_label='John Brock · 2026 glossary',source_evidence=evidence(i),sort_order=len(glossary),**common))
    if len({g['term'].lower() for g in glossary})!=len(glossary):raise ValueError('Duplicate glossary term')
    # The DOCX embeds the dioecious-flower illustration immediately after that definition.
    # The original image and its source credit (in the definition) remain intact.
    with ZipFile(path) as archive:
        xml=ET.fromstring(archive.read('word/document.xml')); paras=xml.findall('.//'+W+'body/'+W+'p')
        rels={r.attrib['Id']:r.attrib for r in ET.fromstring(archive.read('word/_rels/document.xml.rels'))}
        current=None
        by_evidence={g['source_evidence']:g for g in glossary}
        for i in range(glossary_start+1,glossary_end):
            if evidence(i) in by_evidence:current=by_evidence[evidence(i)]
            for blip in paras[i].iter('{http://schemas.openxmlformats.org/drawingml/2006/main}blip'):
                relation=rels[blip.attrib['{http://schemas.openxmlformats.org/officeDocument/2006/relationships}embed']]
                if not current or relation.get('TargetMode')=='External':raise ValueError('Unallocated glossary illustration')
                target=relation['Target']
                if not re.fullmatch(r'media/[\w.-]+',target):raise ValueError('Unexpected illustration path')
                payload=archive.read('word/'+target);key=hashlib.sha256(payload).hexdigest()[:20]
                out=media/'glossary'/(key+Path(target).suffix.lower());out.parent.mkdir(parents=True,exist_ok=True)
                if out.exists() and out.read_bytes()!=payload:raise ValueError('Glossary image changed')
                if not out.exists():out.write_bytes(payload)
                current['illustration']='/media/glossary/'+out.name
                current['source_evidence']+='; embedded '+target
    illustrations=Path(__file__).resolve().parents[1]/'data/glossary-illustrations.json'
    if illustrations.exists():
        manifest=json.loads(illustrations.read_text())
        for item in manifest['images']:
            original=(root/item['outputPath']).resolve()
            if not original.is_relative_to(root.resolve()):raise ValueError('Invalid glossary illustration path')
            payload=original.read_bytes();digest=hashlib.sha256(payload).hexdigest()
            if digest!=item['sha256']:raise ValueError('Glossary illustration changed')
            out=media/'glossary'/(digest[:20]+'.png');out.parent.mkdir(parents=True,exist_ok=True)
            if out.exists() and out.read_bytes()!=payload:raise ValueError('Existing glossary asset differs')
            if not out.exists():out.write_bytes(payload)
            entry=next(g for g in glossary if g['term']==item['term'])
            entry['illustration']='/media/glossary/'+out.name
            entry['source_label']+='; illustration: 2022 edition, p. '+str(item['printedPage'])
            entry['source_evidence']+='; '+manifest['sourcePath']+'; PDF SHA256 '+manifest['sourceSha256']+'; illustration SHA256 '+digest
    return glossary,references,issues


def main():
    ap=argparse.ArgumentParser();ap.add_argument('--source',type=Path,default=Path.home()/'Desktop/brocky')
    ap.add_argument('--accept-source-change',action='store_true',help='Only use after reviewing data/source-change-report.json')
    args=ap.parse_args();root=args.source.resolve();app=Path(__file__).resolve().parents[1]
    data=app/'data';data.mkdir(exist_ok=True)
    source_files=[next(root.rglob(pattern)) for pattern in ['3. NPNA 2026 PLANT DESCRIPTIONS.docx','2.B NPNA 2026 TABLE OF PLANT ATTRIBUTES A.A.rtf','4. NPNA 2026 REF, BIB, GLOSS, FAM LIST, ENDEM LIST, INDEX.docx']]
    fingerprint={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in source_files}
    decisions=json.loads((data/'editorial-decisions.json').read_text()) if (data/'editorial-decisions.json').exists() else {}
    plants,issues,report=extract(root,decisions.get('accountIds',{}));candidates,photo_issues=photo_candidates(root,plants,decisions);issues+=photo_issues
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
        chosen=sorted((c for c in candidates if c['plantId']==p['id']),key=lambda c:(c['priority'],c['path']))
        for n,c in enumerate(chosen):
            src=root/c['path'];digest=hashlib.sha256(src.read_bytes()).hexdigest();key=digest[:20]
            try:
                outputs=[(width,media/f'{key}-{label}.webp') for width,label in [(640,'thumb'),(1600,'large')]]
                if any(not out.exists() for _,out in outputs):
                    with Image.open(src) as original:
                        im=ImageOps.exif_transpose(original).convert('RGB')
                        for width,out in outputs:
                            if out.exists(): continue
                            copy=im.copy();copy.thumbnail((width,width));copy.save(out,'WEBP',quality=83)
            except Exception as e:
                issues.append(dict(kind='image-decode',path=c['path'],error=str(e)));continue
            photos.append(dict(id=c.get('photoId') or 'photo-'+key,plant_id=p['id'],image=f'/media/{key}-large.webp',thumbnail=f'/media/{key}-thumb.webp',
                caption=c.get('caption',p['scientific_name']),photographer=c['credit'],sort_order=n,published=True,
                source_evidence=f"{c['path']}; SHA256 {digest}; {c['evidence']}; editorial image review pending",
                created_at=1790118000,updated_at=None,deleted_at=None))
    owners=collections.defaultdict(set)
    for photo in photos:owners[photo['id']].add(photo['plant_id'])
    conflicts={key for key,ids in owners.items() if len(ids)>1}
    if conflicts:issues.extend(dict(kind='same-image-multiple-accounts',photoId=key,plantIds=sorted(owners[key])) for key in conflicts)
    photos=[p for p in photos if p['id'] not in conflicts]
    map_manifest=data/'book-distribution-maps.json'
    maps=distribution_maps(root,plants,json.loads(map_manifest.read_text()),media) if map_manifest.exists() else []
    standalone_path=data/'standalone-distribution-maps.json'
    standalone=json.loads(standalone_path.read_text()) if standalone_path.exists() else {'version':1,'images':[]}
    maps+=distribution_maps(root,plants,standalone,media)
    glossary,references,reference_issues=reference_material(root,media)
    issues+=reference_issues
    report.update(glossaryTerms=len(glossary),numberedReferences=sum(r['kind']=='usage' for r in references),bibliographyParagraphs=sum(r['kind']=='bibliography' for r in references))
    report.update(distributionMaps=len(maps),publishedMaps=sum(m['published'] for m in maps),plantsWithMaps=len({m['plant_id'] for m in maps if m['published']}),photos=len(photos),plantsWithPhotos=len({p['plant_id'] for p in photos}),families=len({p['family'] for p in plants}),
                  genera=len({p['genus'] for p in plants}),photoCandidates=len(candidates),issues=issues,
                  reviewStatus='Local prototype from accepted manuscript text. Final-proof comparison and editorial photo/credit review pending.')
    lock.write_text(json.dumps(fingerprint,indent=2)+'\n')
    (data/'catalogue-source.json').write_text(json.dumps(dict(plants=plants,photos=photos,maps=maps,glossary=glossary,references=references),ensure_ascii=False,indent=2))
    (data/'photo-candidates.json').write_text(json.dumps(candidates,ensure_ascii=False,indent=2))
    (data/'import-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    (data/'import.sql').write_text('-- Insert records once, then apply conditional reviewed media corrections; preserve unrelated admin edits and deletions.\nPRAGMA foreign_keys=ON;\n'+
        '\n'.join(insert('plants',p) for p in plants)+'\n'+'\n'.join(insert('plant_photos',p) for p in photos)+'\n'+'\n'.join(insert('plant_maps',m) for m in maps)+'\n'+reviewed_media_updates(photos,decisions,maps,standalone)+'\n'+'\n'.join(insert('glossary_terms',g) for g in glossary)+'\n'+'\n'.join(insert('source_references',r) for r in references)+'\n')
    print(json.dumps({k:v for k,v in report.items() if k!='issues'},indent=2));print(f'Editorial issues: {len(issues)} (private data/import-report.json)')

if __name__=='__main__':main()
