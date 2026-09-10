#!/usr/bin/env bash
set -e

ROOT="$(cd "$(dirname "$0")" && pwd)"

echo "=== Step 1: Microblog golden model ==="
cd "$ROOT/apps/microblog"
npm run gen

# Gen stability: the generated files are committed, so they ARE the snapshot.
# If re-running gen changes them, the generator regressed (or the committed
# output is stale) — fail loudly rather than let it drift.
GEN_PATHS="src/Codecs/generated src/Server/generated src/Client/generated schema.sql"
if ! git diff --quiet -- $GEN_PATHS; then
    echo "!!! FAIL: gen output differs from committed (generator regression, or"
    echo "    you have uncommitted gen changes). Review and commit:"
    git --no-pager diff --stat -- $GEN_PATHS
    exit 1
fi
echo "--- gen output matches committed ---"

./check-sql.sh
dotnet build src/Server/Server.fsproj
dotnet build src/Client/Client.fsproj
echo "--- Microblog OK ---"

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
echo "--- Scaffold OK ---"

echo ""
echo "=== Cleanup ==="
rm -rf "$ROOT/apps/_test-app"

echo ""
echo "All tests passed."
