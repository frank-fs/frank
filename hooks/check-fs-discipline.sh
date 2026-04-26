#!/usr/bin/env bash
# Claude Code PostToolUse hook: scans .fs files written by Edit/Write for F#
# discipline violations (constitution rule 7, Holzmann rule 13).
#
# Non-blocking: prints warnings to stderr and exits 0. Legacy violations in a
# file should not halt unrelated edits — the warning is the surfacing mechanism.
#
# Patterns flagged:
#   - `with _ ->`               silent exception swallow (constitution 7)
#   - `^let mutable `           module-level mutable binding (Holzmann 13)

set -euo pipefail

file="${TOOL_INPUT_FILE_PATH:-}"

case "$file" in
    *.fs) ;;
    *) exit 0 ;;
esac

[ -f "$file" ] || exit 0

warnings=""

if grep -nE 'with[[:space:]]+_[[:space:]]*->' "$file" >/dev/null 2>&1; then
    hits=$(grep -nE 'with[[:space:]]+_[[:space:]]*->' "$file")
    warnings="${warnings}silent exception swallow (with _ ->) — log via ILogger (constitution rule 7):
${hits}
"
fi

if grep -nE '^let[[:space:]]+mutable[[:space:]]' "$file" >/dev/null 2>&1; then
    hits=$(grep -nE '^let[[:space:]]+mutable[[:space:]]' "$file")
    warnings="${warnings}module-level mutable — pass dependencies explicitly (Holzmann rule 13):
${hits}
"
fi

if [ -n "$warnings" ]; then
    printf 'F# discipline warnings in %s:\n%s\n' "$file" "$warnings" >&2
fi

exit 0
