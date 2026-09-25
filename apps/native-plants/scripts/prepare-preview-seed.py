"""Prepare a one-time, catalogue-only snapshot; never copies visitor data."""
from pathlib import Path
import argparse, json, sqlite3

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('database', type=Path, help='Existing local Native Plants SQLite file')
args = parser.parse_args()
app = Path(__file__).resolve().parents[1]
destination = app / '.local/preview-initial.sql'
if destination.exists():
    raise SystemExit('Preview seed already exists; keep it as the deployment record. Do not reseed a live preview.')
source = sqlite3.connect(args.database.resolve().as_uri() + '?mode=ro', uri=True)
snapshot = sqlite3.connect(':memory:')
source.backup(snapshot)
source.close()
catalogue = sqlite3.connect(':memory:')
catalogue.executescript((app / 'schema.sql').read_text())
tables = ('plants', 'plant_photos', 'plant_maps', 'glossary_terms', 'source_references')
counts = {}
for table in tables:
    rows = snapshot.execute(f'SELECT * FROM {table}').fetchall()
    source_columns = [c[1] for c in snapshot.execute(f'PRAGMA table_info({table})')]
    target_columns = [c[1] for c in catalogue.execute(f'PRAGMA table_info({table})')]
    assert source_columns == target_columns, f'Schema mismatch: {table}'
    placeholders = ','.join('?' for _ in target_columns)
    catalogue.executemany(f'INSERT INTO {table} VALUES ({placeholders})', rows)
    counts[table] = len(rows)
assert not catalogue.execute('PRAGMA foreign_key_check').fetchall()
# D1 owns the transaction. sqlite3's BEGIN/COMMIT wrappers are not accepted there.
dump = list(catalogue.iterdump())
# Create all tables before inserts, then parents before children. A deferred FK
# still requires the referenced table to exist when D1 prepares each statement.
statements = [(app / 'schema.sql').read_text()]
for table in tables:
    statements.extend(s for s in dump if s.startswith(f'INSERT INTO "{table}"'))
destination.parent.mkdir(mode=0o700, exist_ok=True)
destination.write_text('PRAGMA defer_foreign_keys=ON;\n' + '\n'.join(statements) + '\n')
destination.chmod(0o600)
(destination.parent / 'preview-catalogue-counts.json').write_text(json.dumps(counts, indent=2) + '\n')
print(json.dumps(counts))
print('Prepared catalogue-only seed; all identity and contribution tables are empty.')
