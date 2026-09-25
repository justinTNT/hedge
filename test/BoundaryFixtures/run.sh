#!/usr/bin/env bash
set -euo pipefail
root=$(cd "$(dirname "$0")/../.." && pwd)
cd "$root"
dotnet run --project test/BoundaryFixtures/Generate.fsproj
dotnet fable test/BoundaryFixtures/Runtime.fsproj -o test/BoundaryFixtures/dist --noCache
node --test test/BoundaryFixtures/run.mjs
dotnet fable packages/hedge/src/Admin/Admin.fsproj -o apps/articles/dist/boundary-admin --noCache
node --test test/BoundaryFixtures/admin.test.mjs
