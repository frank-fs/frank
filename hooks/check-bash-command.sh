#!/usr/bin/env bash
# Claude Code PreToolUse hook on the Bash tool. Reads the command from the
# TOOL_INPUT_COMMAND env var (set by Claude Code for Bash hooks) and:
#
#   - HARD BLOCK (exit 2)   --no-verify / --no-gpg-sign
#                           These bypass commit hooks / signing. The Frank
#                           workflow rule is: never skip; fix the underlying
#                           issue instead.
#
#   - CONFIRM GATE (exit 2 unless FRANK_ALLOW_GH_DESTRUCTIVE=1)
#                           gh issue close / gh release create
#                           These are externally visible to other contributors
#                           and to GitHub release consumers. Require explicit
#                           env-var opt-in per CLAUDE.md "never create issues,
#                           PRs, or take external actions while discussing".
#
#   - WARN (exit 0 + stderr) cd <path> && <command>
#                           Compound commands break pre-approved Bash()
#                           permission patterns. The fix is to run `cd <path>`
#                           as its own Bash call (working dir persists), then
#                           the standalone command in a second call.

set -euo pipefail

cmd="${TOOL_INPUT_COMMAND:-}"

[ -z "$cmd" ] && exit 0

if echo "$cmd" | grep -qE -- '--no-verify|--no-gpg-sign'; then
    echo "BLOCKED: --no-verify / --no-gpg-sign bypass commit hooks or signing." >&2
    echo "Frank rule: fix the underlying issue, do not skip safety guards." >&2
    exit 2
fi

if echo "$cmd" | grep -qE 'gh issue close|gh release create'; then
    if [ "${FRANK_ALLOW_GH_DESTRUCTIVE:-}" != "1" ]; then
        action=$(echo "$cmd" | grep -oE 'gh issue close|gh release create' | head -1)
        echo "BLOCKED: '$action' is externally visible to other contributors / release consumers." >&2
        echo "CLAUDE.md: never take external actions while discussing — confirm with the user first." >&2
        echo "If the user has confirmed, retry with FRANK_ALLOW_GH_DESTRUCTIVE=1 prefixed." >&2
        exit 2
    fi
fi

if echo "$cmd" | grep -qE '^[[:space:]]*cd [^&;|]+&&[[:space:]]'; then
    echo "WARNING: 'cd <path> && <cmd>' compound breaks pre-approved Bash() permission patterns." >&2
    echo "Run 'cd <path>' as its own Bash call (working directory persists), then run the standalone command." >&2
fi

exit 0
