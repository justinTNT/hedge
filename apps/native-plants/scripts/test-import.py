import importlib.util, unittest, tempfile
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

if __name__=='__main__':unittest.main()
