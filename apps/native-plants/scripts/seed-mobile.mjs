// Build-time seed for the Android bundle: the catalogue and the bulk accounts exactly as the worker
// serves them, produced by the compiled worker over the local import, so the very first launch opens
// every account with no network. A stale seed is harmless: the first connection's revision poll
// replaces it. Usage: node scripts/seed-mobile.mjs <output dir>   (after build:server)
import { DatabaseSync } from 'node:sqlite';
import { readFileSync, mkdirSync, writeFileSync } from 'node:fs';
import { join } from 'node:path';
import worker from '../dist/server/Worker.js';

const out = process.argv[2];
if (!out) throw new Error('Usage: node scripts/seed-mobile.mjs <output dir>');

const db = new DatabaseSync(':memory:');
db.exec('PRAGMA foreign_keys=ON');
db.exec(readFileSync(new URL('../schema.sql', import.meta.url), 'utf8'));
db.exec(readFileSync(new URL('../data/import.sql', import.meta.url), 'utf8'));
const statement = (sql, values = []) => ({
  bind(...args) { return statement(sql, args); },
  async all() { return { results: db.prepare(sql).all(...values), success: true, meta: {} }; },
  async first() { return db.prepare(sql).get(...values); },
  async run() {
    const s = db.prepare(sql);
    if (s.columns().length) return { results: s.all(...values), success: true, meta: {} };
    const r = s.run(...values);
    return { results: [], success: true, meta: { changes: r.changes } };
  },
});
const env = {
  DB: { prepare: statement, async batch(statements) { db.exec('BEGIN'); try { const results = []; for (const s of statements) results.push(await s.run()); db.exec('COMMIT'); return results; } catch (e) { db.exec('ROLLBACK'); throw e; } } },
  ADMIN_KEY: 'seed-build', BLOBS: {}, ASSETS: { async fetch() { return new Response('Not found', { status: 404 }); } },
};
const call = path => worker.fetch(new Request('http://plants.test' + path), env, { waitUntil() {} });

const catalogue = await call('/api/plants/catalogue');
const accounts = await call('/api/plants/accounts');
if (catalogue.status !== 200 || accounts.status !== 200) throw new Error(`Seed responses failed: ${catalogue.status} / ${accounts.status}`);
const catalogueText = await catalogue.text(), accountsText = await accounts.text();
const parsed = { catalogue: JSON.parse(catalogueText), accounts: JSON.parse(accountsText) };
if (parsed.catalogue.revision !== parsed.accounts.revision) throw new Error('Seed catalogue and accounts disagree on the revision');
if (parsed.accounts.plants.length !== parsed.catalogue.plants.length) throw new Error('Seed accounts do not cover the catalogue');

mkdirSync(out, { recursive: true });
writeFileSync(join(out, 'catalogue.json'), catalogueText);
writeFileSync(join(out, 'accounts.json'), accountsText);
console.log(`Seed: ${parsed.catalogue.plants.length} accounts, revision ${parsed.catalogue.revision.slice(0, 12)}…, ${Math.round((catalogueText.length + accountsText.length) / 1024)} KB → ${out}`);
