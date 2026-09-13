#!/usr/bin/env python3
"""Validate an app's hand-written SQL against its schema truth(s).

Shared across apps (each has a thin check-sql.sh wrapper). It covers BOTH the
app's own src/Server/Sql.fs (identity + app glue, plain-literal SQL) AND the SQL
of every content module the app composes (from gen-modules.json). Module SQL is
Tables-driven — `sprintf "... %s ..." Tables.item` — so it is resolved through
the generated Server.Db.Tables constants before EXPLAIN, exactly as it runs.

Every statement must EXPLAIN-prepare cleanly on the fresh schema (schema.sql).
With --migrations, a second "migrated" schema is built by replaying the
migrations in order and every statement must prepare on it too, and the two
schemas must agree on table + column-name sets (that equivalence only holds for
apps whose DB is actually built by replaying migrations from scratch).

Usage (from the app directory):
  python3 <hedge>/tools/check_sql.py [--migrations 'migrations/*.sql'] \
      [--schema schema.sql] [--allow-extra-migrated table:col ...]
"""
import argparse, re, sqlite3, sys, glob, json, os


def build(path_sqls):
    con = sqlite3.connect(":memory:")
    for p in path_sqls:
        con.executescript(open(p).read())
    return con


def parse_tables(path):
    """Resolve the generated Server.Db.Tables constants (Tables.item -> blog_items)."""
    if not os.path.exists(path):
        return {}
    src = open(path).read()
    m = re.search(r'module Tables\s*=\s*((?:\n\s+let \w+\s*=\s*"[^"]*")+)', src)
    return dict(re.findall(r'let (\w+)\s*=\s*"([^"]*)"', m.group(1))) if m else {}


def extract_plain(path, label):
    """Plain string-literal statements: let name = "..." | \"\"\"...\"\"\"."""
    src = open(path).read()
    pairs = re.findall(r'let (\w+) =\s*(?:"""(.*?)"""|"([^"\n]*)")', src, re.DOTALL)
    return [(f"{label}.{n}", tq if tq else sq) for n, tq, sq in pairs]


def extract_module(path, label, tables):
    """Module statements: sprintf "template" Tables.a Tables.b ..., %s-resolved."""
    src = open(path).read()
    out = []
    for m in re.finditer(r'let (\w+)\s*=\s*sprintf\s+"([^"\n]*)"([^\n]*)', src):
        name, template, argstr = m.group(1), m.group(2), m.group(3)
        args = re.findall(r'Tables\.(\w+)', argstr)
        if template.count("%s") != len(args):
            print(f"FAIL: {label}.{name}: {template.count('%s')} %s but {len(args)} Tables args")
            out.append((f"{label}.{name}", None)); continue
        sql, ok = template, True
        for a in args:
            if a not in tables:
                print(f"FAIL: {label}.{name}: unknown Tables.{a} (not in generated Db.fs)")
                ok = False; break
            sql = sql.replace("%s", tables[a], 1)
        out.append((f"{label}.{name}", sql if ok else None))
    out += extract_plain(path, label)  # a module could also carry plain literals
    return out


def lint_inline_sql(server_dir, failures):
    """Raw SQL strings must live in Sql.fs, not scattered through server code."""
    for root, _, files in os.walk(server_dir):
        if "generated" in root:
            continue
        for f in files:
            if f.endswith(".fs") and f != "Sql.fs":
                p = os.path.join(root, f)
                if 'prepare("' in open(p).read():
                    print(f"FAIL: inline SQL in {p} (belongs in Sql.fs)")
                    failures[0] += 1


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--schema", default="schema.sql")
    ap.add_argument("--migrations", default=None, help="glob; when set, also check migrated schema + equivalence")
    ap.add_argument("--allow-extra-migrated", action="append", default=[], help="table:col permitted only in migrated schema")
    args = ap.parse_args()

    failures = [0]  # boxed so helpers can mutate

    fresh = build([args.schema])
    worlds = [("fresh", fresh)]
    migrated = None
    if args.migrations:
        migrated = build(sorted(glob.glob(args.migrations)))
        worlds.append(("migrated", migrated))

    # Composed content modules, from gen-modules.json. Two manifest shapes:
    #   {"module": "../../packages/modules/blog"}  -> read its module.json for the namespace
    #   {"namespace": "...", "tablePrefix": "..."}  -> legacy inline entry
    # (an {"identity": true} / prefix-less entry is the shared base, not a content module)
    modules = []  # each: (namespace, module_dir)
    for m in json.load(open("gen-modules.json")):
        if m.get("module"):
            d = m["module"]
            ns = json.load(open(os.path.join(d, "module.json"))).get("namespace", "")
            modules.append((ns, d))
        elif m.get("tablePrefix"):
            modules.append((m["namespace"], f"../../packages/modules/{m['namespace'].lower()}"))
    module_dirs = [d for _, d in modules]

    # Resolve Tables.* names: the app's Server.Db (identity) + each owned module's own
    # Db surface (Blog.Db.Tables etc., now committed with the module, not in the app).
    tables = parse_tables("src/Server/generated/Db.fs")
    for _, d in modules:
        tables.update(parse_tables(os.path.join(d, "generated/Db.fs")))

    # Lint app + composed-module server code for inline SQL.
    lint_inline_sql("src/Server", failures)
    for d in module_dirs:
        lint_inline_sql(os.path.join(d, "src/Server"), failures)

    # Extract statements: app plain literals + each module's Tables-resolved SQL.
    stmts = extract_plain("src/Server/Sql.fs", "Sql")
    for ns, d in modules:
        stmts += extract_module(os.path.join(d, "src/Server/Sql.fs"), f"{ns}.Sql", tables)
    if not stmts:
        print("FAIL: no statements extracted"); sys.exit(1)

    # EXPLAIN-prepare each statement against every schema world.
    for name, sql in stmts:
        if sql is None:      # extraction already failed + reported
            failures[0] += 1; continue
        for world, con in worlds:
            try:
                con.execute("EXPLAIN " + sql, tuple(None for _ in range(sql.count("?"))))
            except sqlite3.Error as e:
                print(f"FAIL [{world}] {name}: {e}")
                failures[0] += 1

    # Schema equivalence (only meaningful when migrations build the DB from scratch).
    if migrated is not None:
        allow = {tuple(x.split(":", 1)) for x in args.allow_extra_migrated}

        def tbls(con):
            return {r[0] for r in con.execute(
                "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'")}

        tf, tm = tbls(fresh), tbls(migrated)
        for missing in tf - tm:
            print(f"FAIL: table {missing} in schema.sql but not produced by migrations"); failures[0] += 1
        for extra in tm - tf:
            print(f"FAIL: table {extra} produced by migrations but absent from schema.sql"); failures[0] += 1

        def cols(con, t):
            return {r[1] for r in con.execute(f"PRAGMA table_info({t})")}

        for t in sorted(tf & tm):
            cf, cm = cols(fresh, t), cols(migrated, t)
            for c in cf - cm:
                print(f"FAIL: {t}.{c} in schema.sql but not in migrated schema"); failures[0] += 1
            for c in cm - cf:
                if (t, c) not in allow:
                    print(f"FAIL: {t}.{c} in migrated schema but not in schema.sql"); failures[0] += 1

    if failures[0]:
        print(f"check-sql: {failures[0]} failure(s)"); sys.exit(1)
    mods = ", ".join(ns for ns, _ in modules) or "none"
    scope = "fresh + migrated" if migrated is not None else "fresh"
    print(f"check-sql: {len(stmts)} statements OK against {scope} (app + modules: {mods})"
          + ("; schemas agree" if migrated is not None else ""))


if __name__ == "__main__":
    main()
