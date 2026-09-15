#!/usr/bin/env bash
# Type-check every app's Server + Client (via `dotnet build`, same as test.sh).
# The safety net for framework refactors that touch app code (Admin dispatcher,
# EventHub/live-events transport, ...). Run before and after each such change.
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
fail=0
for app in microblog articles music archive basewatch; do
  for proj in Server Client; do
    f="$ROOT/apps/$app/src/$proj/$proj.fsproj"
    [ -f "$f" ] || continue
    printf '%-12s %-7s ... ' "$app" "$proj"
    if out=$(dotnet build "$f" -clp:ErrorsOnly -nologo 2>&1); then
      echo ok
    else
      echo FAILED; echo "$out" | tail -25; fail=1
    fi
  done
done
# The microblog browser extension is real F# (Fable-compiled) but isn't a Server/Client
# project, so it slipped this net — a framework/gen change could silently break its build.
# Type-check it too (dotnet build, same as above).
extf="$ROOT/apps/microblog/extension/Extension.fsproj"
if [ -f "$extf" ]; then
  printf '%-12s %-7s ... ' "microblog" "ext"
  if out=$(dotnet build "$extf" -clp:ErrorsOnly -nologo 2>&1); then
    echo ok
  else
    echo FAILED; echo "$out" | tail -25; fail=1
  fi
fi
[ $fail -eq 0 ] && echo "ALL GREEN" || { echo "BUILD FAILURES (see above)"; exit 1; }
