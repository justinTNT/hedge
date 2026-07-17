#!/bin/bash
# Validate every hand-written SQL statement against BOTH schema truths:
#   fresh    — a database created from the Gen-emitted schema.sql
#   migrated — a database built by replaying migrations/ in order
# A statement must EXPLAIN-prepare cleanly on both, so code can never
# stray onto a column that exists in only one world. Also asserts the two
# schemas agree (column-name sets per table, modulo a small allowlist of
# deliberate legacy divergences).
set -e
cd "$(dirname "$0")"

# 1. Lint: raw SQL strings may only live in src/Server/Sql.fs
if grep -rn 'prepare("' src/Server --include="*.fs" | grep -v "generated/"; then
    echo "FAIL: inline SQL outside Sql.fs (statements belong in src/Server/Sql.fs)"
    exit 1
fi

python3 - <<'EOF'
import re, sqlite3, sys, glob

def build(path_sqls):
    con = sqlite3.connect(":memory:")
    for p in path_sqls:
        con.executescript(open(p).read())
    return con

fresh = build(["schema.sql"])
migrated = build(sorted(glob.glob("migrations/*.sql")))

# 2. Extract named statements from Sql.fs
src = open("src/Server/Sql.fs").read()
stmts = re.findall(r'let (\w+) =\s*(?:"""(.*?)"""|"([^"\n]*)")', src, re.DOTALL)
stmts = [(name, tq if tq else sq) for name, tq, sq in stmts]
if not stmts:
    print("FAIL: no statements extracted from Sql.fs"); sys.exit(1)

# 3. EXPLAIN-prepare each statement against both schemas
failures = 0
for name, sql in stmts:
    for world, con in [("fresh", fresh), ("migrated", migrated)]:
        try:
            con.execute("EXPLAIN " + sql, tuple(None for _ in range(sql.count("?"))))
        except sqlite3.Error as e:
            print(f"FAIL [{world}] Sql.{name}: {e}")
            failures += 1

# 4. Schema equivalence: table + column-name sets, with a legacy allowlist.
#    (name-presence only: notnull/default divergences are documented and
#    invisible to prepare-checking anyway)
ALLOWED_EXTRA_MIGRATED = {("comments", "guest_id")}  # pre-0007 column, kept for archaeology

def tables(con):
    rows = con.execute(
        "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'"
    ).fetchall()
    return {r[0] for r in rows}

tf, tm = tables(fresh), tables(migrated)
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
print(f"check-sql: {len(stmts)} statements OK against fresh + migrated; schemas agree")
EOF
