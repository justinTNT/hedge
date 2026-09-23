import hashlib, importlib.util, json, unittest, tempfile
from unittest.mock import patch
from pathlib import Path
from xml.etree import ElementTree as ET

spec=importlib.util.spec_from_file_location('source',Path(__file__).with_name('import-source.py'))
source=importlib.util.module_from_spec(spec);spec.loader.exec_module(source)

class ImportTests(unittest.TestCase):
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
                 patch.object(source,'extract',return_value=([plant],[],{})), \
                 patch('sys.argv',['import-source.py','--source',str(root)]), patch('builtins.print'):
                source.main()
            catalogue=json.loads((data/'catalogue-source.json').read_text())
            self.assertEqual(catalogue['plants'],[plant])
            self.assertEqual([p['caption'] for p in catalogue['photos']],[name,'Acacia example'])
            self.assertEqual([p['plant_id'] for p in catalogue['photos']],['one','one'])
            self.assertIn("'Acacia example','Fixture photographer'",(data/'import.sql').read_text())
            self.assertIn('subspecies remains unverified',catalogue['photos'][1]['source_evidence'])

if __name__=='__main__':unittest.main()
