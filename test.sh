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
echo "=== Step 1g: Unified shell Stage 0 (client matrix + host-context probe) ==="
# Stage 0 adds compatible hosting interfaces to the content modules (emptyHosted/
# enterHosted/updateHosted/contentView/withSession + idempotent disposal) plus a shared
# immutable HostContext, with standalone behaviour preserved. Assert every client
# composition still builds — articles default (now the Stage-1 shell + blog), microblog
# (blog), and articles ndct (articles standalone) — and that the context yields
# INDEPENDENT URL/ID contexts (articles at root, blog at /blog), the Stage 0 probe.
cd "$ROOT"
dotnet build apps/articles/src/Client/Client.fsproj >/dev/null 2>&1 \
    || { echo "!!! FAIL: articles default client build (articles+blog)"; exit 1; }
dotnet build apps/microblog/src/Client/Client.fsproj >/dev/null 2>&1 \
    || { echo "!!! FAIL: microblog client build (blog)"; exit 1; }
HEDGE_SITE=ndct dotnet build apps/articles/src/Client/Client.fsproj >/dev/null 2>&1 \
    || { echo "!!! FAIL: articles ndct client build (articles only)"; exit 1; }
echo "--- client build matrix OK (articles default, microblog, articles ndct) ---"
HC_OUT="$ROOT/test/HostContextProbe/dist"
rm -rf "$HC_OUT"
dotnet fable test/HostContextProbe/HostContextProbe.fsproj -o "$HC_OUT" >/dev/null 2>&1
if ! node "$HC_OUT/Program.js" | grep -q "host-context-probe:.*OK"; then
    echo "!!! FAIL: host-context probe (independent URL/ID contexts)."
    node "$HC_OUT/Program.js" || true
    rm -rf "$HC_OUT"
    exit 1
fi
rm -rf "$HC_OUT"
echo "--- host-context probe OK (independent URL/ID contexts) ---"

echo ""
echo "=== Step 1h: Unified shell Stage 2 (Justat shell production build) ==="
# The unified shell (Shell/Main.js) boots Justat's root and hosts BOTH content modules
# in one document — articles at /, blog at /blog (Stage 2 folded blog in; there is no
# separate blog bundle). ndct keeps the articles standalone entry. The shell's hosted
# module set must agree with the composition manifests + the fsproj conditional
# (Shell/Config.fs): default composes articles + blog; ndct composes articles only.
cd "$ROOT"
GM="apps/articles/gen-modules.json"
GMN="apps/articles/gen-modules.ndct.json"
grep -q '"module": "\.\./\.\./packages/modules/articles"' "$GM" && grep -q '"module": "\.\./\.\./packages/modules/blog"' "$GM" \
    || { echo "!!! FAIL: gen-modules.json no longer composes articles + blog (shell Config disagrees)"; exit 1; }
grep -q '"module": "\.\./\.\./packages/modules/blog"' "$GMN" \
    && { echo "!!! FAIL: gen-modules.ndct.json composes blog, but ndct is articles-standalone (no shell)"; exit 1; }
echo "--- shell module set agrees with gen-modules manifests (default articles+blog; ndct articles-only) ---"
# The production artifact must actually build (not just typecheck): Fable-compile the
# Justat client (shell) + admin, then assemble _site via vite. Assert index boots the
# shell client bundle and that NO separate blog bundle remains (blog is hosted in-shell).
# (Runs from the app dir; artifacts are gitignored and cleaned after.)
(
    cd "$ROOT/apps/articles" \
    && rm -rf dist _site \
    && npm run build:client >/dev/null 2>&1 \
    && npm run build:admin >/dev/null 2>&1 \
    && SITE_SLUG=justat SITE_TITLE="just@justat.at" SITE_LOGO="/public/justat.png" npm run build:site >/dev/null 2>&1
) || { echo "!!! FAIL: Justat shell production build did not assemble"; rm -rf "$ROOT/apps/articles/dist" "$ROOT/apps/articles/_site" "$ROOT/apps/articles/lib"; exit 1; }
ok=1
[ -f "$ROOT/apps/articles/dist/client/Shell/Main.js" ] || { echo "!!! FAIL: shell entry (dist/client/Shell/Main.js) not Fable-compiled"; ok=0; }
ls "$ROOT/apps/articles/_site/assets"/main-*.js >/dev/null 2>&1 || { echo "!!! FAIL: no shell client bundle in _site/assets"; ok=0; }
grep -q 'assets/main-' "$ROOT/apps/articles/_site/index.html" || { echo "!!! FAIL: _site/index.html does not bundle the client entry"; ok=0; }
[ -f "$ROOT/apps/articles/_site/blog.html" ] && { echo "!!! FAIL: _site/blog.html still present (Stage 2 folds blog into the shell)"; ok=0; }
rm -rf "$ROOT/apps/articles/dist" "$ROOT/apps/articles/_site" "$ROOT/apps/articles/lib"
[ "$ok" = 1 ] || exit 1
echo "--- Justat shell production build OK (index boots the shell; blog hosted in-shell) ---"

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
