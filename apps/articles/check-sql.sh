#!/bin/bash
# Validate hand-written SQL (app + composed modules: Articles + Blog) against the
# fresh schema.sql. Unlike microblog, articles' DB was seeded directly from
# schema.sql and its migrations/ are manual, incremental patches (0001 is
# additive against a live DB; 0002 renames) — NOT a from-scratch replay — so
# there is no migrated-schema equivalence to assert; schema.sql is the truth.
# The committed schema.sql / generated Tables are the justat superset (articles +
# blog), which is the right validation target for both justat and ndct.
# The shared checker lives in packages/hedge/tools (see microblog/check-sql.sh).
set -e
cd "$(dirname "$0")"
exec python3 ../../packages/hedge/tools/check_sql.py
