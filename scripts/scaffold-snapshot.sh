#!/usr/bin/env bash
# Emit a normalized content snapshot of an app's hand-written (template) files —
# excluding generated code and build artifacts. Used by test.sh to catch drift in
# the scaffold's template strings (the class of bug that shipped music's broken
# admin update: a golden-model fix never back-ported to the scaffold template).
#
#   scripts/scaffold-snapshot.sh apps/_test-app            # print snapshot
#   scripts/scaffold-snapshot.sh apps/_test-app > test/expected-scaffold.txt  # update baseline
set -e
app="$1"
cd "$app"
find . -type f \
  ! -path '*/bin/*' ! -path '*/obj/*' ! -path './dist/*' ! -path './_site/*' \
  ! -path './node_modules/*' ! -path './src/*/generated/*' ! -path './lib/rich-text/*' \
  ! -name 'schema.sql' ! -name 'package-lock.json' \
  | LC_ALL=C sort | while read -r f; do
    printf '=== %s ===\n' "${f#./}"
    cat "$f"
    printf '\n'
  done
