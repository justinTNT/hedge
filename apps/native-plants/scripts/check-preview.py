"""Check the private deployment boundary and initial catalogue media before upload."""
from pathlib import Path
import sqlite3, tomllib

app = Path(__file__).resolve().parents[1]
config = tomllib.loads((app / 'wrangler.toml').read_text())['env']['preview']
assert config['main'] == 'worker-preview.js'
assert config['preview_urls'] is False
assert config['assets']['run_worker_first'] is True, 'Static assets must pass through authentication'
assert config['vars']['ENVIRONMENT'] != 'development', 'Remote session cookies must be Secure'
assert config['d1_databases'][0]['database_name'] == 'native-plants-preview-db'
assert config['r2_buckets'][0]['bucket_name'] == 'native-plants-preview-media'
seed = app / '.local/preview-initial.sql'
assert seed.exists(), 'Prepare the initial catalogue snapshot first; see PREVIEW.md'
db = sqlite3.connect(':memory:')
db.execute('PRAGMA foreign_keys=ON')
db.executescript(seed.read_text())
for table in ('guests', 'identities', 'grants', 'plant_notes', 'personal_plant_photos', 'plant_view_preferences', 'contribution_claims'):
    assert db.execute(f'SELECT COUNT(*) FROM {table}').fetchone()[0] == 0, f'Private seed data in {table}'
paths = set()
for table, columns in [('plant_photos', ('image', 'thumbnail')), ('plant_maps', ('image',)), ('glossary_terms', ('illustration',))]:
    for column in columns:
        paths.update(value for (value,) in db.execute(f'SELECT {column} FROM {table}') if value)
for value in sorted(paths):
    relative = Path(value.lstrip('/'))
    assert value.startswith('/media/') and '..' not in relative.parts, f'Unexpected media reference: {value}'
    for folder in ('public', '_site'):
        assert (app / folder / relative).is_file(), f'Missing {folder} media: {value}'
files = [p for p in (app / '_site').rglob('*') if p.is_file()]
assert files and len(files) < 20000
for file in files:
    assert file.stat().st_size <= 25 * 1024 * 1024, f'Asset too large: {file}'
    assert file.suffix not in ('.sql', '.sqlite', '.toml', '.map'), f'Unexpected deploy asset: {file}'
    assert not any(part.startswith('.') for part in file.relative_to(app / '_site').parts), f'Hidden deploy asset: {file}'
print(f'Preview checked: {len(paths)} referenced media paths; {len(files)} assets; empty user tables; all routes gated.')
