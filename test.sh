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
run_module_surface microblog ../../packages/modules/alerts   # C6: admin+cron module (0 endpoints)
if ! git diff --quiet -- packages/modules/blog/generated packages/modules/articles/generated packages/modules/alerts/generated; then
    echo "!!! FAIL: a module's generated surface differs from committed. Regenerate + commit:"
    git --no-pager diff --stat -- packages/modules/blog/generated packages/modules/articles/generated packages/modules/alerts/generated
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

# C6: idealist is microblog's per-site superset (blog + alerts). Its gen writes only *.idealist.*
# and schema.idealist.sql — never the default (blog-only) files, so the 6 other tenants are untouched.
echo ""
echo "=== Step 1c2: Per-site glue (idealist) + blog schema untouched ==="
( cd "$ROOT/apps/microblog" && HEDGE_SITE=idealist npm run gen >/dev/null 2>&1 )
idealist_paths="apps/microblog/src/Server/generated/Routes.idealist.fs apps/microblog/src/Server/generated/AdminGen.idealist.fs apps/microblog/schema.idealist.sql"
if ! git diff --quiet -- $idealist_paths; then
    echo "!!! FAIL: microblog idealist glue differs from committed. Regenerate + commit:"
    git --no-pager diff --stat -- $idealist_paths
    exit 1
fi
# Promotion dedup is the module-owned alerts_promotions table, so composing alerts must NOT stamp
# alerts_* tables or origin_entry_key onto the DEFAULT (blog-only) microblog schema — every non-
# idealist tenant's schema stays byte-identical.
if grep -qE "alerts_|origin_entry_key" "$ROOT/apps/microblog/schema.sql"; then
    echo "!!! FAIL: default microblog schema.sql contains alerts_/origin_entry_key (must be blog-only)"
    exit 1
fi
echo "--- microblog idealist glue matches committed; default schema blog-only ---"

echo ""
echo "=== Step 1b: Microblog golden model (SQL + build) ==="
cd "$ROOT/apps/microblog"
./check-sql.sh
dotnet build src/Server/Server.fsproj
dotnet build src/Client/Client.fsproj
echo "--- Microblog OK ---"

# C6 (reviewer B P1): Fable's compile cache does NOT invalidate on a HEDGE_SITE change, so switching
# compositions in a shared build dir without clearing it can ship the wrong module set (e.g. a tenant
# deploy reusing idealist's cached source list — alerts admin + cron). Every microblog deploy script
# runs clean:site; verify the whole round trip here via the REAL Fable path: build the idealist
# composition (blog + alerts — also its compile check), then build the DEFAULT exactly as a tenant
# deploy does (clean:site first), and assert no alerts wiring leaked into the default server.
echo "--- C6 composition cache-safety (idealist -> clean -> default, via Fable) ---"
HEDGE_SITE=idealist npm run build:server >/dev/null 2>&1 \
    || { echo "!!! FAIL: idealist server (blog + alerts) did not compile"; exit 1; }
npm run clean:site >/dev/null 2>&1
HEDGE_SITE= npm run build:server >/dev/null 2>&1 \
    || { echo "!!! FAIL: default microblog server did not compile"; exit 1; }
if grep -rq "alert_sources" dist/server 2>/dev/null; then
    echo "!!! FAIL: default microblog server carries alerts wiring after an idealist build —"
    echo "    Fable cache leak across compositions; a deploy must run clean:site (it does)."
    exit 1
fi
npm run clean:site >/dev/null 2>&1
echo "--- Microblog idealist compiles; default composition alerts-free after switch ---"

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
echo "=== Step 1e2: Signed guest-cookie envelope (crypto contract) ==="
# The signed guest credential (Hedge.GuestCookie) is the security core of the
# signed-guest-cookies work. Fable-compile the fixture and run it under node so it
# exercises the REAL WebCrypto sign/verify path: tamper, expiry, wrong audience/key,
# key rotation/retirement, malformed-signed (no legacy fallback), legacy, and bounds
# all behave as specified. A regression in the envelope contract fails here.
cd "$ROOT"
GC_OUT="$ROOT/test/GuestCookie/dist"
rm -rf "$GC_OUT"
dotnet fable test/GuestCookie/GuestCookie.fsproj -o "$GC_OUT" >/dev/null 2>&1
if ! node "$GC_OUT/Program.js" | grep -q "guest-cookie:.*OK"; then
    echo "!!! FAIL: signed guest-cookie envelope — sign/verify/tamper/expiry/audience/key contract regressed."
    node "$GC_OUT/Program.js" || true
    rm -rf "$GC_OUT"
    exit 1
fi
rm -rf "$GC_OUT"
echo "--- Signed guest-cookie envelope OK ---"

echo ""
echo "=== Step 1e3: Guest-session policy (migration decisions) ==="
# The shared guest-session policy (Content.Server.GuestSession) decides accept/renew/upgrade/reject/
# bootstrap over the signed envelope + injected DB lookups. Fable→node with a real signing config +
# fake lookups exercises the migration rules: signed accept/renew, eligible-anonymous legacy bridge
# upgrade, linked-legacy reject (re-login), ineligible/cutover/past-window/missing/expired reject,
# bootstrap mint, and OAuth adoption. A policy regression fails here.
cd "$ROOT"
GS_OUT="$ROOT/test/GuestSession/dist"
rm -rf "$GS_OUT"
dotnet fable test/GuestSession/GuestSession.fsproj -o "$GS_OUT" >/dev/null 2>&1
if ! node "$GS_OUT/Program.js" | grep -q "guest-session:.*OK"; then
    echo "!!! FAIL: guest-session policy — a migration/authorization decision regressed."
    node "$GS_OUT/Program.js" || true
    rm -rf "$GS_OUT"
    exit 1
fi
rm -rf "$GS_OUT"
echo "--- Guest-session policy OK ---"

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
node --test "$ROOT/test/guest-session-runtime.test.mjs"
bash "$ROOT/test/BoundaryFixtures/run.sh"

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
# The microblog extension (Fable-compiled F#) also consumes the framework + blog codecs;
# the split-gen migration once silently broke its build because nothing here compiled it.
dotnet build apps/microblog/extension/Extension.fsproj >/dev/null 2>&1 \
    || { echo "!!! FAIL: microblog extension build (Popup + blog codecs)"; exit 1; }
echo "--- client build matrix OK (articles default, microblog, articles ndct, extension) ---"
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
echo "=== Step 1i: C3 HostProbe (content-module decoupling) ==="
# A minimal host that composes BOTH content modules' server layers (via their own .server.props
# + the shared content-server contract) with NO Server.Env / Server.Identity. If it compiles,
# the modules are decoupled from any app environment (the C3 exit check). This guards the
# decoupling: re-adding an app-env dependency to a module handler fails the build here.
cd "$ROOT"
dotnet build test/HostProbe/HostProbe.fsproj -v q >/dev/null 2>&1 \
    || { echo "!!! FAIL: HostProbe did not compile — a content module now depends on an app environment (Server.Env/Server.Identity)"; exit 1; }
echo "--- HostProbe OK (both modules compile with no Server.Env/Server.Identity) ---"

echo ""
echo "=== Step 1j: CP-A reordered-completion fixtures (request identity) ==="
# The C1 fix (2872794) removed the shell's activation staleness filter; the review found five
# race regressions because a child route/id match does NOT establish request identity. CP-A
# re-established it inside each content module (LoadGen read-generation + DraftRev draft-revision
# + invalidateInFlight). These fixtures drive the REAL module `update` functions with reordered /
# stale / late completions (compiled exactly as an app composes them) and assert each race is
# resolved — a stale read/failure is dropped, a late comment success can't erase a newer draft,
# /new doesn't strand behind a spinner. If the gen/rev contract regresses, a fixture fails here.
# Output lands under apps/articles/ so the compiled client's bare `react` import resolves from
# that app's node_modules (the fixtures link the full module client, which references Feliz/React).
cd "$ROOT"
RF_OUT="$ROOT/apps/articles/.reorder-fixtures"
rm -rf "$RF_OUT"
dotnet fable test/ReorderFixtures/ReorderFixtures.fsproj -o "$RF_OUT" >/dev/null 2>&1
cp test/ReorderFixtures/run.mjs "$RF_OUT/run.mjs"
if ! node "$RF_OUT/run.mjs" | grep -q "reorder-fixtures:.*OK"; then
    echo "!!! FAIL: CP-A reorder fixtures — a request-identity race regressed."
    node "$RF_OUT/run.mjs" || true
    rm -rf "$RF_OUT"
    exit 1
fi
rm -rf "$RF_OUT"
echo "--- CP-A reorder fixtures OK (stale/late completions handled; drafts preserved) ---"

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
