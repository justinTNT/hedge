#!/bin/bash
# Validate the hand-written SQL (Server/Sql.fs + the composed identity module) against BOTH the fresh
# schema.sql and the migrated schema (replayed migrations), and assert the two agree. The shared checker
# lives in packages/hedge/tools.
set -e
cd "$(dirname "$0")"
exec python3 ../../packages/hedge/tools/check_sql.py --migrations 'migrations/*.sql'
