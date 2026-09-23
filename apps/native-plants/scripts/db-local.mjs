import { readFileSync, writeFileSync, mkdirSync } from 'node:fs';
import { execFileSync } from 'node:child_process';

const mode = process.argv[2];
if (!['init', 'seed'].includes(mode)) throw new Error('Use init or seed');
mkdirSync('.wrangler', { recursive: true });
const file = mode === 'init' ? '.wrangler/schema-init.sql' : 'data/import.sql';
if (mode === 'init') {
  const schema = readFileSync('schema.sql', 'utf8')
    .replaceAll('CREATE TABLE ', 'CREATE TABLE IF NOT EXISTS ')
    .replaceAll('CREATE UNIQUE INDEX ', 'CREATE UNIQUE INDEX IF NOT EXISTS ')
    .replaceAll('CREATE INDEX ', 'CREATE INDEX IF NOT EXISTS ');
  writeFileSync(file, schema);
}
try {
  execFileSync('wrangler', ['d1', 'execute', 'native-plants-db', '--local', '--file', file], { encoding: 'utf8', stdio: 'pipe' });
  console.log(mode === 'init' ? 'Local schema ready (existing data preserved).' : 'Local seed applied (existing records and edits preserved).');
} catch (error) {
  console.error(error.stdout?.toString(), error.stderr?.toString());
  process.exitCode = 1;
}
