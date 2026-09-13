#!/bin/bash
# Validate hand-written SQL (app + composed modules) against BOTH the fresh
# schema.sql and the migrated schema (replayed migrations), and assert the two
# agree. microblog's DB is built via the d1 migrations-apply flow, so the
# fresh-vs-migrated equivalence is a real invariant here.
# The shared checker lives in packages/hedge/tools (see articles/check-sql.sh).
set -e
cd "$(dirname "$0")"
exec python3 ../../packages/hedge/tools/check_sql.py \
    --migrations 'migrations/*.sql' \
    --allow-extra-migrated comments:guest_id   # pre-0007 column, kept for archaeology
