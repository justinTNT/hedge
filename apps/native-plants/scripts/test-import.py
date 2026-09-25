import hashlib, importlib.util, json, unittest, tempfile, sqlite3
from unittest.mock import patch
from pathlib import Path
from xml.etree import ElementTree as ET

spec=importlib.util.spec_from_file_location('source',Path(__file__).with_name('import-source.py'))
source=importlib.util.module_from_spec(spec);spec.loader.exec_module(source)

class ImportTests(unittest.TestCase):
    def test_reviewed_media_updates_preserve_owner_edits_and_are_repeatable(self):
        db=sqlite3.connect(':memory:');self.addCleanup(db.close)
        db.executescript("CREATE TABLE plant_photos(id TEXT,image TEXT,thumbnail TEXT,source_evidence TEXT,published INT,photographer TEXT,caption TEXT,sort_order INT,deleted_at INT); CREATE TABLE plant_maps(id TEXT,image TEXT,source_evidence TEXT,published INT);")
        old='a'*64
        photos=[dict(id='photo-1',image='/new.webp',thumbnail='/new-thumb.webp',photographer='Kym Brennan'),
                dict(id='photo-2',image='/new.webp',thumbnail='/new-thumb.webp',photographer='Kym Brennan')]
        decisions=dict(photoAllocations=[dict(photoId=p['id'],replacesSha256=old,path='trimmed.png',sha256='b'*64) for p in photos],
                       reclassifiedMapPhotos=[dict(photoId='map-photo',reason='Reviewed map')])
        db.execute('INSERT INTO plant_photos VALUES(?,?,?,?,?,?,?,?,?)',('photo-1','/media/'+old[:20]+'-large.webp','/media/'+old[:20]+'-thumb.webp','source',0,'Photographer not recorded','Owner caption',99,123))
        db.execute('INSERT INTO plant_photos VALUES(?,?,?,?,?,?,?,?,?)',('photo-2','/owner.webp','/owner-thumb.webp','owner',1,'Owner credit','Custom image',1,None))
        db.execute('INSERT INTO plant_photos VALUES(?,?,?,?,?,?,?,?,?)',('map-photo','/map.webp','/map.webp','source',1,'','Map',1,None))
        ref=dict(plantId='one',reference='old-map')
        key='map-'+hashlib.sha256(b'one|old-map').hexdigest()[:20]
        db.execute('INSERT INTO plant_maps VALUES(?,?,?,?)',(key,'/old.png','map source',1))
        sql=source.reviewed_media_updates(photos,decisions,[dict(id=key,image='/old.png')],dict(supersedesBookMaps=[ref]))
        db.executescript(sql)
        row=db.execute('SELECT image,thumbnail,published,photographer,caption,sort_order,deleted_at FROM plant_photos WHERE id="photo-1"').fetchone()
        self.assertEqual(row,('/new.webp','/new-thumb.webp',0,'Kym Brennan','Owner caption',99,123))
        self.assertEqual(db.execute('SELECT image,photographer FROM plant_photos WHERE id="photo-2"').fetchone(),('/owner.webp','Owner credit'))
        self.assertEqual(db.execute('SELECT published FROM plant_maps').fetchone(),(0,))
        self.assertEqual(db.execute('SELECT published FROM plant_photos WHERE id="map-photo"').fetchone(),(0,))
        db.execute('UPDATE plant_maps SET published=1')
        db.execute('UPDATE plant_photos SET published=1 WHERE id="map-photo"')
        before=list(db.iterdump());db.executescript(sql)
        self.assertEqual(list(db.iterdump()),before)

    def test_contributor_initials_require_filename_boundaries(self):
        for code,name in source.CREDITS.items():
            for filename in [f'Acacia example.{code}415.JPG',f'Acacia example_{code}.jpg',f'{code}-Acacia example.jpg']:
                with self.subTest(filename=filename):self.assertEqual(source.photo_credit(filename),name)
        for filename in ['Acacia example.JPG','Acacia JRSunknown.JPG','ExampleKB.JPG']:
            self.assertEqual(source.photo_credit(filename),'Photographer not recorded')

    def test_reference_source_preserves_all_entries_and_reports_number_gaps(self):
        from zipfile import ZipFile
        from xml.sax.saxutils import escape
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);path=root/'4. NPNA 2026 REF, BIB, GLOSS, FAM LIST, ENDEM LIST, INDEX.docx'
            paragraphs=['REFERENCES for ABORIGINAL PLANT USAGE','1. First citation','3. Third citation','BIBLIOGRAPHY','Author. Complete entry.','GLOSSARY','Compound leaf: A leaf made of leaflets.','Glaucous: A bluish bloom.','FAMILY LIST']
            xml='<w:document xmlns:w="'+source.W[1:-1]+'"><w:body>'+''.join('<w:p><w:r><w:t>'+escape(p)+'</w:t></w:r></w:p>' for p in paragraphs)+'</w:body></w:document>'
            with ZipFile(path,'w') as archive:
                archive.writestr('word/document.xml',xml)
                archive.writestr('word/_rels/document.xml.rels','<Relationships/>')
            with patch.object(source,'__file__',str(root/'app/scripts/import-source.py')):
                glossary,refs,issues=source.reference_material(root,root/'media')
            self.assertEqual([g['term'] for g in glossary],['Compound leaf','Glaucous'])
            self.assertEqual(glossary[0]['aliases'],'Compound leaves')
            self.assertEqual([r['number'] for r in refs],[1,3,0])
            self.assertEqual(refs[2]['citation'],'Author. Complete entry.')
            self.assertIn(dict(kind='missing-numbered-reference',number=2),issues)

    def test_accepted_revision_omits_deleted_and_moved_from(self):
        xml='<w:p xmlns:w="'+source.W[1:-1]+'"><w:r><w:t>Current </w:t></w:r><w:del><w:r><w:t>old</w:t></w:r></w:del><w:ins><w:r><w:t>new</w:t></w:r></w:ins><w:moveFrom><w:r><w:t>wrong</w:t></w:r></w:moveFrom></w:p>'
        self.assertEqual(source.accepted(ET.fromstring(xml)),'Current new')
    def test_inline_and_repeated_sections_retain_qualifiers(self):
        sections=source.sections('Habit: small fern. Spores: periodic. Notes: first. Notes: second. Fruiting: not rec.')
        self.assertEqual(sections['Spores'],'periodic.');self.assertEqual(sections['Notes'],'first. second.');self.assertEqual(sections['Fruiting'],'not rec.')
    def test_unknown_codes_are_issues_not_invented_values(self):
        issues=[];self.assertEqual(source.codes('TR, UNKNOWN',source.FORMS,issues,'Test','forms'),'Tree');self.assertEqual(issues[0]['unknown'],['UNKNOWN'])
    def test_common_name_is_not_a_scientific_heading(self):
        self.assertFalse(source.scientific_heading('Baobab or Boab'))
        self.assertTrue(source.scientific_heading('Adansonia gregorii'))
        self.assertTrue(source.scientific_heading('Grevillea sp. Magela Creek'))
    def test_names_and_quotes_are_stable_and_sql_escaped(self):
        self.assertEqual(source.identity('Acacia  example'),source.identity('acacia example'))
        self.assertEqual(source.q("John's plant"),"'John''s plant'")
        self.assertIn('INSERT OR IGNORE',source.insert('plants',{'id':'existing'}))
    def test_conflicting_photo_filename_is_held(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);folder=root/'D. PLANT DESCRIPTIONS GENERA A-Z'/'AA'/'Acacia alleniana'/'PICK';folder.mkdir(parents=True)
            (folder/'1. Acacia latescens.JPG').write_bytes(b'not opened during matching')
            found,issues=source.photo_candidates(root,[{'id':'one','scientific_name':'Acacia alleniana'},{'id':'two','scientific_name':'Acacia latescens'}])
            self.assertEqual(found,[]);self.assertEqual(issues[0]['kind'],'photo-label-conflict-or-shortened')

    def photo_fixture(self,root,label,filename):
        path=root/'D. PLANT DESCRIPTIONS GENERA A-Z'/'AA'/label/'PICK'/filename
        path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(b'fixture photo')
        return path

    def test_rank_punctuation_matches_without_discarding_the_rank(self):
        for rank in ('subsp.','var.','sp.'):
            with self.subTest(rank=rank), tempfile.TemporaryDirectory() as tmp:
                root=Path(tmp);name=f'Acacia example {rank} minor'
                self.photo_fixture(root,name,f'1. {name}.RD415.JPG')
                self.photo_fixture(root,name,'2. Acacia example.JPG')
                self.photo_fixture(root,name,f'3. Acacia example {rank} major.JPG')
                found,issues=source.photo_candidates(root,[{'id':'one','scientific_name':name}])
                self.assertEqual(len(found),1);self.assertEqual(found[0]['credit'],'Russell Dempster')
                self.assertEqual(len(issues),2)

    def test_unmatched_curated_folder_is_reported_until_alias_is_reviewed(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);label='Abelmoschus moschatus subsp. tuberosa';name='Abelmoschus moschatus subsp. tuberosus'
            path=self.photo_fixture(root,label,f'1. {label}.JPG');plants=[{'id':'one','scientific_name':name}]
            found,issues=source.photo_candidates(root,plants)
            self.assertEqual(found,[]);self.assertEqual(issues[0]['kind'],'photo-folder-unmatched')
            self.assertEqual(issues[0]['photos'],[str(path.relative_to(root))])
            found,issues=source.photo_candidates(root,plants,{'photoNameAliases':{label:{'plant':name,'reason':'Reviewed spelling variant'}}})
            self.assertEqual(issues,[]);self.assertEqual(found[0]['plantId'],'one')
            self.assertIn('Reviewed spelling variant',found[0]['evidence'])

    def test_filename_prefixes_and_multiple_taxa_are_not_matches(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);name='Acacia example'
            self.photo_fixture(root,name,'1. Acacia exampleana.JPG')
            self.photo_fixture(root,name,'2. Acacia example and Acacia other.JPG')
            found,issues=source.photo_candidates(root,[{'id':'one','scientific_name':name},{'id':'two','scientific_name':'Acacia other'}])
            self.assertEqual(found,[])
            self.assertEqual({i['kind'] for i in issues},{'photo-label-conflict-or-shortened','photo-multiple-taxa'})

    def test_reviewed_allocation_removes_only_its_path_from_unmatched_folder_report(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp);name='Acacia example'
            reviewed=self.photo_fixture(root,'Acacia typo','1. Acacia typo.JPG')
            pending=self.photo_fixture(root,'Acacia typo','2. Acacia typo.JPG')
            decision=dict(path=str(reviewed.relative_to(root)),sha256=hashlib.sha256(reviewed.read_bytes()).hexdigest(),
                          plant=name,include=True,credit='Fixture photographer',reason='Reviewed archive spelling')
            found,issues=source.photo_candidates(root,[{'id':'one','scientific_name':name}],{'photoAllocations':[decision]})
            self.assertEqual([c['path'] for c in found],[str(reviewed.relative_to(root))])
            self.assertEqual(issues[0]['photos'],[str(pending.relative_to(root))])
            pending.unlink()
            self.assertEqual(source.photo_candidates(root,[{'id':'one','scientific_name':name}],{'photoAllocations':[decision]})[1],[])

    def test_supplemental_photos_require_individual_unchanged_allocations(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)
            path=self.photo_fixture(root,'Acacia example','Acacia exampl typo.JPG')
            other=root/'Acacia example.JPG';other.write_bytes(b'unreviewed')
            plants=[{'id':'one','scientific_name':'Acacia example'}]
            decision=dict(path=str(path.relative_to(root)),sha256=hashlib.sha256(path.read_bytes()).hexdigest(),plant='Acacia example',
                          include=True,credit='Fixture photographer',reason='Reviewed individual filename')
            self.assertEqual(source.photo_candidates(root,plants)[0],[])
            found,issues=source.photo_candidates(root,plants,{'photoAllocations':[decision]})
            self.assertEqual(issues,[]);self.assertEqual([c['path'] for c in found],[str(path.relative_to(root))])
            self.assertEqual(found[0]['credit'],'Fixture photographer');self.assertIn(decision['reason'],found[0]['evidence'])
            decision['include']=False
            found,issues=source.photo_candidates(root,plants,{'photoAllocations':[decision]})
            self.assertEqual(found,[]);self.assertEqual(issues[0]['kind'],'editorial-photo-exclusion')
            path.write_bytes(b'replaced photo')
            with self.assertRaisesRegex(ValueError,'Photo allocation changed'):
                source.photo_candidates(root,plants,{'photoAllocations':[decision]})

    def test_species_caption_survives_import_without_changing_ranked_account(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)/'archive';app=Path(tmp)/'app';data=app/'data';data.mkdir(parents=True)
            name='Acacia example subsp. minor';plant={'id':'one','scientific_name':name,'family':'Fabaceae','genus':'Acacia'}
            exact=self.photo_fixture(root,name,'1. Acacia example subsp. minor.JPG')
            candidate=self.photo_fixture(root,name,'2. Acacia example.JPG')
            source.Image.new('RGB',(8,8),'green').save(exact)
            source.Image.new('RGB',(8,8),'yellow').save(candidate)
            for filename in ['3. NPNA 2026 PLANT DESCRIPTIONS.docx','2.B NPNA 2026 TABLE OF PLANT ATTRIBUTES A.A.rtf','4. NPNA 2026 REF, BIB, GLOSS, FAM LIST, ENDEM LIST, INDEX.docx']:
                (root/filename).write_bytes(b'fixture source fingerprint')
            decision=dict(path=str(candidate.relative_to(root)),sha256=hashlib.sha256(candidate.read_bytes()).hexdigest(),
                          plant=name,include=True,caption='Acacia example',credit='Fixture photographer',
                          reason='Species-level illustration approved; subspecies remains unverified')
            (data/'editorial-decisions.json').write_text(json.dumps({'photoAllocations':[decision]}))
            with patch.object(source,'__file__',str(app/'scripts/import-source.py')), \
                 patch.object(source,'extract',return_value=([plant],[],{})), patch.object(source,'reference_material',return_value=([],[],[])), \
                 patch('sys.argv',['import-source.py','--source',str(root)]), patch('builtins.print'):
                source.main()
            catalogue=json.loads((data/'catalogue-source.json').read_text())
            self.assertEqual(catalogue['plants'],[plant])
            self.assertEqual([p['caption'] for p in catalogue['photos']],[name,'Acacia example'])
            self.assertEqual([p['plant_id'] for p in catalogue['photos']],['one','one'])
            self.assertIn("'Acacia example','Fixture photographer'",(data/'import.sql').read_text())
            self.assertIn('subspecies remains unverified',catalogue['photos'][1]['source_evidence'])

    def test_maps_keep_published_taxon_and_skip_unallocated_candidates(self):
        with tempfile.TemporaryDirectory() as tmp:
            root=Path(tmp)/'archive';root.mkdir();media=Path(tmp)/'media'
            path=root/'map.png';source.Image.new('L',(20,30),255).save(path)
            item=dict(plantId='one',reference='p042-o226',outputPath='map.png',sha256=hashlib.sha256(path.read_bytes()).hexdigest(),
                      caption='Acacia example',sourceLabel='2022 edition, p. 83',pdfPage=42,objectIndex=226,reason='Reviewed species-level map')
            manifest=dict(version=1,sourcePath='proof.pdf',sourceSha256='proof-hash',images=[item,{**item,'plantId':None,'outputPath':'unallocated.png'}])
            maps=source.distribution_maps(root,[{'id':'one'}],manifest,media)
            self.assertEqual(len(maps),1);self.assertEqual(maps[0]['caption'],'Acacia example')
            self.assertTrue(maps[0]['image'].endswith('.gif'))
            with source.Image.open(media/'maps'/Path(maps[0]['image']).name) as display:
                self.assertEqual(display.size,(20,30))
                self.assertEqual(display.convert('RGBA').getextrema()[3],(0,0))
            self.assertEqual(source.distribution_maps(root,[{'id':'one'}],manifest,media),maps)
            with self.assertRaisesRegex(ValueError,'Unknown map account'):
                source.distribution_maps(root,[{'id':'different'}],manifest,media)
            path.write_bytes(b'changed map')
            with self.assertRaisesRegex(ValueError,'Map changed'):
                source.distribution_maps(root,[{'id':'one'}],manifest,media)

    def test_map_gif_keeps_ink_and_transparency_without_resizing_or_dithering(self):
        image=source.Image.new('L',(4,2));image.putdata([0,64,128,191,192,220,254,255])
        payload=source.io.BytesIO();image.save(payload,'PNG')
        with source.Image.open(source.io.BytesIO(source.transparent_map_gif(payload.getvalue()))) as result:
            self.assertEqual(result.format,'GIF');self.assertEqual(result.size,(4,2))
            rgba=result.convert('RGBA');pixels=[rgba.getpixel((x,y)) for y in range(2) for x in range(4)]
            self.assertEqual(pixels[:4],[(0,0,0,255)]*4)
            self.assertEqual([p[3] for p in pixels[4:]],[0]*4)

    def test_map_format_update_preserves_owner_images_and_metadata(self):
        db=sqlite3.connect(':memory:');self.addCleanup(db.close)
        db.execute('CREATE TABLE plant_maps(id TEXT,image TEXT,caption TEXT,source_evidence TEXT,published INT,sort_order INT,deleted_at INT)')
        db.executemany('INSERT INTO plant_maps VALUES(?,?,?,?,?,?,?)',[
            ('source','/media/maps/abc.png','Edited caption','evidence',0,17,123),
            ('owner','/owner-map.png','Owner image','owner evidence',1,2,None)])
        sql=source.reviewed_media_updates([],{},[dict(id='source',image='/media/maps/abc.gif'),dict(id='owner',image='/media/maps/def.gif')],{})
        db.executescript(sql)
        self.assertEqual(db.execute('SELECT * FROM plant_maps WHERE id="source"').fetchone(),('source','/media/maps/abc.gif','Edited caption','evidence',0,17,123))
        self.assertEqual(db.execute('SELECT * FROM plant_maps WHERE id="owner"').fetchone(),('owner','/owner-map.png','Owner image','owner evidence',1,2,None))
        before=list(db.iterdump());db.executescript(sql);self.assertEqual(list(db.iterdump()),before)

if __name__=='__main__':unittest.main()
