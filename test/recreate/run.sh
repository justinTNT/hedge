#!/usr/bin/env bash
# P3 regression gate — populated parent+children table recreate must COMMIT.
#
# SQLite's canonical table rebuild (copy -> DROP -> RENAME) orphans referencing rows
# mid-transaction; the classic fix is `PRAGMA foreign_keys=OFF` toggled OUTSIDE the
# transaction, which D1 forbids inside its implicit migration txn. Deferring the check
# is not enough on its own: a self-FK left pointing at the dropped table, or a populated
# parent with child rows, still fails at COMMIT even when `PRAGMA foreign_key_check` is
# clean. generateRecreateTableSql handles this by rewriting self-FKs to <t>_new during
# the rebuild window and stashing/recreating the whole parent+children cluster.
#
# This applies the generator's ACTUAL output (via `Gen -- emit-recreate <Type>`) to a
# real SQLite database built from the committed schema, asserting:
#   1. a self-FK-only rebuild (ItemComment) commits with rows retained;
#   2. a populated parent-with-children rebuild (Item + its comments/item_tags) commits
#      with rows retained, INCLUDING out-of-order self-referencing child rows;
#   3. FKs are still enforced after the rebuild;
#   4. an invalid-reference control still FAILS at COMMIT (the rebuild is not laundering
#      away real violations).
#
# SQLite-only (sqlite3 CLI). NOT a remote-D1 assertion — remote migrations are never
# auto-applied; this gates the generated SQL's shape before a human reviews + applies it.
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/../.." && pwd)"
APP="$ROOT/apps/articles"
WORK="$(mktemp -d)"
trap 'rm -rf "$WORK"' EXIT

emit() { # <DisplayName> -> recreate SQL on stdout
    ( cd "$APP" && dotnet run --project src/Gen/Gen.fsproj -- emit-recreate "$1" 2>/dev/null )
}

# Seed the three blog tables + their FK parents. `orphan=1` adds a child row whose
# parent_id references a missing comment (an invalid reference planted with FKs off).
seed() { # <db> <orphan?>
    local db="$1" orphan="${2:-0}"
    sqlite3 "$db" < "$APP/schema.sql"
    sqlite3 "$db" <<SQL
PRAGMA foreign_keys=OFF;
INSERT INTO guests (id, session_id, created_at) VALUES ('g1','sess1',1);
INSERT INTO identities (id, guest_id, provider, provider_user_id, name, picture, created_at)
    VALUES ('id1','g1','local','ext1','Ann','',1);
INSERT INTO blog_items (id,title,owner_comment,article_date,created_at,view_count)
    VALUES ('i1','Hello','oc',1,1,0);
INSERT INTO blog_tags (id,name,created_at) VALUES ('t1','news',1);
-- child comment rows stored OUT OF ORDER (a reply before its parent) + a 3-level chain,
-- the worst case for the rebuild's INSERT ... SELECT ordering.
INSERT INTO blog_comments (id,item_id,identity_id,parent_id,author,content,removed,created_at)
    VALUES ('c2','i1','id1','c1','Ann','reply',0,2);
INSERT INTO blog_comments (id,item_id,identity_id,parent_id,author,content,removed,created_at)
    VALUES ('c3','i1','id1','c2','Ann','reply2',0,3);
INSERT INTO blog_comments (id,item_id,identity_id,parent_id,author,content,removed,created_at)
    VALUES ('c1','i1','id1',NULL,'Ann','root',0,1);
INSERT INTO blog_item_tags (id,item_id,tag_id) VALUES ('x1','i1','t1');
$( [ "$orphan" = 1 ] && echo "INSERT INTO blog_comments (id,item_id,identity_id,parent_id,author,content,removed,created_at) VALUES ('c9','i1','id1','MISSING','Ann','orphan',0,9);" )
PRAGMA foreign_keys=ON;
SQL
}

# Apply the recreate SQL inside one transaction with FKs enabled (mirrors D1's implicit
# migration txn). Returns success/failure of the COMMIT.
apply() { # <db> <sql-file>
    local db="$1" sql="$2"
    { echo "PRAGMA foreign_keys=ON;"; echo "BEGIN;"; cat "$sql"; echo "COMMIT;"; } \
        | sqlite3 --bail "$db" >/dev/null 2>"$WORK/err"
}

RC_A="$WORK/recreate_itemcomment.sql"
RC_B="$WORK/recreate_item.sql"
emit ItemComment > "$RC_A"
emit Item        > "$RC_B"

fail() { echo "!!! FAIL: $1"; [ -s "$WORK/err" ] && sed 's/^/    /' "$WORK/err"; exit 1; }

# --- 1. self-FK-only rebuild (ItemComment) commits, rows retained ---
seed "$WORK/a.db"
apply "$WORK/a.db" "$RC_A" || fail "self-FK rebuild (ItemComment) did not COMMIT"
[ "$(sqlite3 "$WORK/a.db" 'SELECT count(*) FROM blog_comments')" = 3 ] \
    || fail "self-FK rebuild lost comment rows"
[ "$(sqlite3 "$WORK/a.db" "SELECT parent_id FROM blog_comments WHERE id='c3'")" = c2 ] \
    || fail "self-FK rebuild lost the parent_id chain"
echo "--- self-FK rebuild (ItemComment): COMMIT, 3 rows + chain retained ---"

# --- 2. parent+children cluster rebuild (Item) commits, rows retained ---
seed "$WORK/b.db"
apply "$WORK/b.db" "$RC_B" || fail "parent+children rebuild (Item) did not COMMIT"
for pair in "blog_items 1" "blog_comments 3" "blog_item_tags 1"; do
    set -- $pair
    [ "$(sqlite3 "$WORK/b.db" "SELECT count(*) FROM $1")" = "$2" ] \
        || fail "parent+children rebuild changed $1 row count (expected $2)"
done
[ "$(sqlite3 "$WORK/b.db" "SELECT parent_id FROM blog_comments WHERE id='c2'")" = c1 ] \
    || fail "parent+children rebuild lost a self-referencing child row"
echo "--- parent+children rebuild (Item): COMMIT, items/comments/item_tags retained ---"

# --- 3. FKs still enforced after the rebuild (not silently dropped) ---
if sqlite3 "$WORK/b.db" "PRAGMA foreign_keys=ON; INSERT INTO blog_comments (id,item_id,identity_id,parent_id,author,content,removed,created_at) VALUES ('bad','i1','id1','NOPE','A','x',0,1);" >/dev/null 2>&1; then
    fail "post-rebuild self-FK is NOT enforced (bad parent_id was accepted)"
fi
if sqlite3 "$WORK/b.db" "PRAGMA foreign_keys=ON; INSERT INTO blog_item_tags (id,item_id,tag_id) VALUES ('bad','NOPE','t1');" >/dev/null 2>&1; then
    fail "post-rebuild cross-FK is NOT enforced (bad item_id was accepted)"
fi
echo "--- FKs enforced after rebuild (bad parent_id + bad item_id both rejected) ---"

# --- 4. invalid-reference control: the rebuild must still FAIL at COMMIT ---
seed "$WORK/c.db" 1
if apply "$WORK/c.db" "$RC_B"; then
    fail "orphan control COMMITTED — the rebuild is laundering a real FK violation"
fi
grep -qi "foreign key" "$WORK/err" || fail "orphan control failed for the wrong reason (expected a FOREIGN KEY error)"
echo "--- invalid-reference control: rebuild correctly FAILED at COMMIT ---"

echo "recreate: all P3 checks passed (SQLite-only; remote D1 not exercised)."
