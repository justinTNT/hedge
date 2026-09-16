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
# Type-check it first (dotnet build — a fast, clean F# error surface).
extf="$ROOT/apps/microblog/extension/Extension.fsproj"
if [ -f "$extf" ]; then
  printf '%-12s %-7s ... ' "microblog" "ext-tc"
  if out=$(dotnet build "$extf" -clp:ErrorsOnly -nologo 2>&1); then
    echo ok
  else
    echo FAILED; echo "$out" | tail -25; fail=1
  fi

  # Then a CLEAN Fable compile + esbuild bundle + package for each target, and verify the
  # emitted artifacts. The type-check above exercises neither Fable itself, esbuild
  # bundling, nor the packaged manifest/popup/background — all of which have broken before.
  # The Fable compile overwrites extension/fable_output, so committed JS is never a stale
  # input; sites.json is developer-specific and not required. Fable has exited 0 after a
  # restore/compile failure, so scan the output for errors too, not just the exit code.
  verify_ext() { # <label> <npm-script> <dist-subdir>
    local label="$1" script="$2" dist="$ROOT/apps/microblog/extension/$3" out rc missing=""
    printf '%-12s %-7s ... ' "microblog" "$label"
    out=$(cd "$ROOT/apps/microblog" && npm run "$script" 2>&1); rc=$?
    if [ $rc -ne 0 ] || printf '%s' "$out" | grep -qiE "error FS|Build FAILED|Compilation failed|restore failed|Cannot find|MSB[0-9]"; then
      echo FAILED; printf '%s\n' "$out" | tail -25; fail=1; return
    fi
    for f in manifest.json popup.js popup.html background.js; do
      [ -s "$dist/$f" ] || missing="$missing $f"
    done
    if [ -n "$missing" ]; then echo "FAILED (missing/empty:$missing)"; fail=1; return; fi
    echo ok
  }
  verify_ext "ext-chr" "build:extension" "dist-chrome"
  verify_ext "ext-ffx" "build:extension:firefox" "dist-firefox"
fi
[ $fail -eq 0 ] && echo "ALL GREEN" || { echo "BUILD FAILURES (see above)"; exit 1; }
