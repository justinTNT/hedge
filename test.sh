#!/usr/bin/env bash
set -e

ROOT="$(cd "$(dirname "$0")" && pwd)"

echo "=== Step 1: Gen stability (every app) ==="
# The generated files are committed, so they ARE the snapshot: re-running gen must
# reproduce them byte-for-byte. If not, either the generator regressed or the
# committed output is stale (how the soft-delete drift on archive/basewatch/music
# hid — the check used to run for microblog only). Every app with a generator is
# checked, so committed generated code can't silently lag the generator.
cd "$ROOT"
for app in microblog articles archive basewatch music; do
    [ -f "apps/$app/src/Gen/Gen.fsproj" ] || continue
    ( cd "apps/$app" && npm run gen >/dev/null 2>&1 )
    # git diff considers only tracked files, so an app's untracked generated output
    # (e.g. archive's dead Client/generated, gitignored) is correctly ignored.
    paths="apps/$app/src/Codecs/generated apps/$app/src/Server/generated apps/$app/src/Client/generated apps/$app/schema.sql"
    if ! git diff --quiet -- $paths; then
        echo "!!! FAIL: $app gen output differs from committed (generator regression, or"
        echo "    stale committed gen). Review and commit:"
        git --no-pager diff --stat -- $paths
        exit 1
    fi
    echo "--- $app gen matches committed ---"
done

# Content modules own their generated surface (packages/modules/<m>/generated), committed
# once. Re-run the module-emit pass (from a host that composes it) and diff, so the
# committed surface can't silently lag the generator either.
echo ""
echo "=== Step 1a: Module surfaces (module-owned generated) ==="
run_module_surface() { # <host-app> <module-path>
    ( cd "$ROOT/apps/$1" && dotnet run --project src/Gen/Gen.fsproj -- module "$2" >/dev/null 2>&1 )
}
run_module_surface microblog ../../packages/modules/blog
run_module_surface articles ../../packages/modules/articles
if ! git diff --quiet -- packages/modules/blog/generated packages/modules/articles/generated; then
    echo "!!! FAIL: a module's generated surface differs from committed. Regenerate + commit:"
    git --no-pager diff --stat -- packages/modules/blog/generated packages/modules/articles/generated
    exit 1
fi
echo "--- module surfaces match committed ---"

# Per-HEDGE_SITE glue is committed too (a site's deployed Routes/AdminGen/schema is a
# reviewable, gen-stable artifact — no more regenerate-at-deploy-then-restore dance).
# ndct is the only site with its own manifest today. Its gen writes only *.ndct.* and
# does not touch the default (justat superset), so no restore is needed after.
echo ""
echo "=== Step 1c: Per-site glue (ndct) ==="
( cd "$ROOT/apps/articles" && HEDGE_SITE=ndct npm run gen >/dev/null 2>&1 )
ndct_paths="apps/articles/src/Server/generated/Routes.ndct.fs apps/articles/src/Server/generated/AdminGen.ndct.fs apps/articles/schema.ndct.sql"
if ! git diff --quiet -- $ndct_paths; then
    echo "!!! FAIL: articles ndct glue differs from committed. Regenerate + commit:"
    git --no-pager diff --stat -- $ndct_paths
    exit 1
fi
echo "--- articles ndct glue matches committed ---"

echo ""
echo "=== Step 1b: Microblog golden model (SQL + build) ==="
cd "$ROOT/apps/microblog"
./check-sql.sh
dotnet build src/Server/Server.fsproj
dotnet build src/Client/Client.fsproj
echo "--- Microblog OK ---"

# Articles composes two modules (Articles + Blog); check its SQL too.
cd "$ROOT/apps/articles"
./check-sql.sh
echo "--- Articles SQL OK ---"

echo ""
echo "=== Step 1e: Admin schema JSON boundary (SchemaCodec round-trip) ==="
# The admin /api/admin/types handler encodes every registered schema via
# SchemaCodec.encodeTypeSchema and the client decodes it. A DU case (e.g. a FieldAttr)
# added without a SchemaCodec case throws at that boundary — the EditableDate
# regression. FS0025-as-error catches the encode gap at build; this exercises the
# actual Fable/JS runtime (real Thoth) end-to-end, catching decode gaps too.
cd "$ROOT"
RT_OUT="$ROOT/test/SchemaRoundtrip/dist"
rm -rf "$RT_OUT"
dotnet fable test/SchemaRoundtrip/SchemaRoundtrip.fsproj -o "$RT_OUT" >/dev/null 2>&1
if ! node "$RT_OUT/Program.js" | grep -q "schema-roundtrip:.*OK"; then
    echo "!!! FAIL: SchemaCodec round-trip failed at the admin schema JSON boundary."
    node "$RT_OUT/Program.js" || true
    rm -rf "$RT_OUT"
    exit 1
fi
rm -rf "$RT_OUT"
echo "--- SchemaCodec round-trip OK ---"

echo ""
echo "=== Step 1f: Populated table recreate (P3) ==="
# A schema change that forces a table rebuild (a column type/drop or any FK change)
# emits copy -> DROP -> RENAME. On a populated DB with self-FKs (comments.parent_id) or
# child tables, that used to fail at COMMIT even with defer_foreign_keys — the classic
# foreign_keys=OFF fix is unavailable inside D1's implicit migration txn. This applies
# the generator's ACTUAL recreate SQL to a real SQLite DB and asserts it commits with
# rows retained, while an invalid-reference control still fails at commit.
cd "$ROOT"
if command -v sqlite3 >/dev/null 2>&1; then
    ./test/recreate/run.sh
    echo "--- Populated table recreate OK ---"
else
    echo "--- SKIP: sqlite3 not on PATH (P3 recreate check needs it) ---"
fi

echo ""
echo "=== Step 2: Scaffold pipeline ==="
cd "$ROOT"
rm -rf "$ROOT/apps/_test-app"
dotnet run --project packages/hedge/src/Gen/Scaffold.fsproj -- _test-app

# Scaffold snapshot: the scaffold's hand-written template strings drift silently
# (music shipped a broken admin update this way — a golden-model fix never
# back-ported to the template). Diff the emitted templates against the checked-in
# baseline. If a change is intentional, refresh it:
#   scripts/scaffold-snapshot.sh apps/_test-app > test/expected-scaffold.txt
if ! "$ROOT/scripts/scaffold-snapshot.sh" "$ROOT/apps/_test-app" \
        | diff -u "$ROOT/test/expected-scaffold.txt" - ; then
    echo "!!! FAIL: scaffold templates drifted from test/expected-scaffold.txt (diff above)."
    echo "    If intentional: scripts/scaffold-snapshot.sh apps/_test-app > test/expected-scaffold.txt"
    rm -rf "$ROOT/apps/_test-app"
    exit 1
fi
echo "--- scaffold matches expected snapshot ---"

cd "$ROOT/apps/_test-app"
dotnet run --project src/Gen/Gen.fsproj
dotnet build src/Server/Server.fsproj
dotnet build src/Client/Client.fsproj

# Production build: the snapshot only locks template TEXT, so a scaffold that emits
# an incoherent prod build (missing admin/site step, unresolvable rich-text deps,
# no vite outDir) still passed. Actually run the full build and assert it assembled
# a shippable _site (index + admin + bundled assets + copied runtime files).
npm install
npm run build
for f in _site/index.html _site/admin.html _site/lib/guest-session.js _site/admin.css _site/public/styles.css; do
    [ -f "$ROOT/apps/_test-app/$f" ] || { echo "!!! FAIL: scaffold prod build missing $f"; exit 1; }
done
ls "$ROOT/apps/_test-app/_site/assets"/main-*.js  >/dev/null 2>&1 || { echo "!!! FAIL: no client bundle in _site/assets"; exit 1; }
ls "$ROOT/apps/_test-app/_site/assets"/admin-*.js >/dev/null 2>&1 || { echo "!!! FAIL: no admin bundle in _site/assets"; exit 1; }
echo "--- Scaffold OK (prod build assembles a shippable _site) ---"

echo ""
echo "=== Cleanup ==="
rm -rf "$ROOT/apps/_test-app"

echo ""
echo "All tests passed."
