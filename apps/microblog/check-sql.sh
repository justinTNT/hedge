#!/bin/bash
# Validate every hand-written SQL statement against BOTH schema truths:
#   fresh    — a database created from the Gen-emitted schema.sql
#   migrated — a database built by replaying migrations/ in order
# A statement must EXPLAIN-prepare cleanly on both, so code can never
# stray onto a column that exists in only one world. Also asserts the two
# schemas agree (column-name sets per table, modulo a small allowlist of
# deliberate legacy divergences).
#
# Scope covers BOTH the app's own src/Server/Sql.fs (identity + darwin glue,
# plain-literal SQL) AND the SQL of every content module this app composes
# (from gen-modules.json). Module SQL is Tables-driven —
# `sprintf "... %s ..." Tables.item` — so it's resolved through the generated
# Server.Db.Tables constants before EXPLAIN, exactly as it runs at runtime.
set -e
cd "$(dirname "$0")"

# 1. Lint: raw SQL strings may only live in src/Server/Sql.fs (app + modules).
#    The module dirs are added in the python block below (it knows the composition);
#    here we cover the app's own server code.
if grep -rn 'prepare("' src/Server --include="*.fs" | grep -v "generated/"; then
    echo "FAIL: inline SQL outside Sql.fs (statements belong in src/Server/Sql.fs)"
    exit 1
fi

python3 - <<'EOF'
import re, sqlite3, sys, glob, json, os

def build(path_sqls):
    con = sqlite3.connect(":memory:")
    for p in path_sqls:
        con.executescript(open(p).read())
    return con

fresh = build(["schema.sql"])
migrated = build(sorted(glob.glob("migrations/*.sql")))

# Resolve the generated table-name constants (Server.Db.Tables) so module SQL,
# which is written table-agnostic as `sprintf "... %s ..." Tables.item`, can be
# expanded to the concrete names this app composes (e.g. Tables.item=blog_items).
def parse_tables(path):
    src = open(path).read()
    m = re.search(r'module Tables\s*=\s*((?:\n\s+let \w+\s*=\s*"[^"]*")+)', src)
    if not m:
        return {}
    return dict(re.findall(r'let (\w+)\s*=\s*"([^"]*)"', m.group(1)))

tables = parse_tables("src/Server/generated/Db.fs")

# Which content modules does this app compose? gen-modules.json lists them; a
# real content module has a tablePrefix (the root "Models" module has "").
modules = [m for m in json.load(open("gen-modules.json")) if m.get("tablePrefix")]
module_dirs = [f"../../packages/modules/{m['namespace'].lower()}" for m in modules]

failures = 0

# 1b. Lint the composed module server code for inline SQL too (must live in Sql.fs).
for d in module_dirs:
    server = os.path.join(d, "src/Server")
    for root, _, files in os.walk(server):
        if "generated" in root:
            continue
        for f in files:
            if f.endswith(".fs") and f != "Sql.fs":
                if 'prepare("' in open(os.path.join(root, f)).read():
                    print(f"FAIL: inline SQL in {os.path.join(root, f)} (belongs in the module's Sql.fs)")
                    failures += 1

# 2. Extract named statements.
#    - App Sql.fs: plain string literals ("""...""" or "...").
#    - Module Sql.fs: sprintf "template" Tables.a Tables.b ..., resolved via `tables`.
def extract_plain(path, label):
    src = open(path).read()
    pairs = re.findall(r'let (\w+) =\s*(?:"""(.*?)"""|"([^"\n]*)")', src, re.DOTALL)
    return [(f"{label}.{n}", tq if tq else sq) for n, tq, sq in pairs]

def extract_module(path, label):
    src = open(path).read()
    out = []
    for m in re.finditer(r'let (\w+)\s*=\s*sprintf\s+"([^"\n]*)"([^\n]*)', src):
        name, template, argstr = m.group(1), m.group(2), m.group(3)
        args = re.findall(r'Tables\.(\w+)', argstr)
        if template.count("%s") != len(args):
            print(f"FAIL: {label}.{name}: {template.count('%s')} %s but {len(args)} Tables args")
            out.append((f"{label}.{name}", None)); continue
        sql = template
        ok = True
        for a in args:
            if a not in tables:
                print(f"FAIL: {label}.{name}: unknown Tables.{a} (not in generated Db.fs)")
                ok = False; break
            sql = sql.replace("%s", tables[a], 1)
        out.append((f"{label}.{name}", sql if ok else None))
    # A module could also carry plain-literal statements (none today, but stay honest).
    out += [(n, s) for (n, s) in extract_plain(path, label)]
    return out

stmts = extract_plain("src/Server/Sql.fs", "Sql")
for m, d in zip(modules, module_dirs):
    stmts += extract_module(os.path.join(d, "src/Server/Sql.fs"), f"{m['namespace']}.Sql")

if not stmts:
    print("FAIL: no statements extracted"); sys.exit(1)

# 3. EXPLAIN-prepare each statement against both schemas.
for name, sql in stmts:
    if sql is None:          # extraction already failed and reported above
        failures += 1; continue
    for world, con in [("fresh", fresh), ("migrated", migrated)]:
        try:
            con.execute("EXPLAIN " + sql, tuple(None for _ in range(sql.count("?"))))
        except sqlite3.Error as e:
            print(f"FAIL [{world}] {name}: {e}")
            failures += 1

# 4. Schema equivalence: table + column-name sets, with a legacy allowlist.
#    (name-presence only: notnull/default divergences are documented and
#    invisible to prepare-checking anyway)
ALLOWED_EXTRA_MIGRATED = {("comments", "guest_id")}  # pre-0007 column, kept for archaeology

def tables_of(con):
    rows = con.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"
    ).fetchall()
    return {r[0] for r in rows}

tf, tm = tables_of(fresh), tables_of(migrated)
for missing in tf - tm:
    print(f"FAIL: table {missing} in schema.sql but not produced by migrations"); failures += 1
for extra in tm - tf:
    print(f"FAIL: table {extra} produced by migrations but absent from schema.sql"); failures += 1

def cols(con, table):
    return {r[1] for r in con.execute(f"PRAGMA table_info({table})")}

for t in sorted(tf & tm):
    cf, cm = cols(fresh, t), cols(migrated, t)
    for c in cf - cm:
        print(f"FAIL: {t}.{c} in schema.sql but not in migrated schema"); failures += 1
    for c in cm - cf:
        if (t, c) not in ALLOWED_EXTRA_MIGRATED:
            print(f"FAIL: {t}.{c} in migrated schema but not in schema.sql"); failures += 1

if failures:
    print(f"check-sql: {failures} failure(s)"); sys.exit(1)
mods = ", ".join(m["namespace"] for m in modules) or "none"
print(f"check-sql: {len(stmts)} statements OK against fresh + migrated (app + modules: {mods}); schemas agree")
EOF
