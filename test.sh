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
