#!/usr/bin/env bash
set -e

ROOT="$(cd "$(dirname "$0")" && pwd)"

echo "=== Step 1: Microblog golden model ==="
cd "$ROOT/apps/microblog"
npm run gen
dotnet build src/Server/Server.fsproj
dotnet build src/Client/Client.fsproj
echo "--- Microblog OK ---"

echo ""
echo "=== Step 2: Scaffold pipeline ==="
cd "$ROOT"
dotnet run --project packages/hedge/src/Gen/Scaffold.fsproj -- _test-app
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
