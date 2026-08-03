#!/usr/bin/env bash
# Claude Code Stop hook: warns when a src/Frank.* package introduced on the
# current branch (relative to master) is missing a package README.md or a
# runnable sample under sample/.
#
# Scope: only packages that are NEW relative to master are checked — this
# intentionally does not flag pre-existing gaps (e.g. Frank.Analyzers,
# Frank.Auth currently have no sample/) since re-litigating those every
# session would be pure noise.
#
# Non-blocking: prints warnings to stderr and exits 0. This is a reminder,
# not a gate.

set -uo pipefail

repo_root=$(git rev-parse --show-toplevel 2>/dev/null) || exit 0
cd "$repo_root" || exit 0

branch=$(git rev-parse --abbrev-ref HEAD 2>/dev/null) || exit 0
case "$branch" in
    master|HEAD) exit 0 ;;
esac

# No meaningful base to diff against (e.g. fresh clone, differently-named
# default branch) — silently no-op rather than error.
git rev-parse master >/dev/null 2>&1 || exit 0

new_fsprojs=$(git diff --name-only --diff-filter=A master...HEAD -- 'src/Frank.*/*.fsproj' 2>/dev/null)
[ -z "$new_fsprojs" ] && exit 0

while IFS= read -r fsproj; do
    [ -z "$fsproj" ] && continue

    pkg_dir=$(dirname "$fsproj")      # e.g. src/Frank.Provenance
    pkg_name=$(basename "$pkg_dir")   # e.g. Frank.Provenance

    pkg_warnings=""
    if [ ! -f "$pkg_dir/README.md" ]; then
        pkg_warnings="${pkg_warnings}\n  - missing $pkg_dir/README.md"
    fi
    if [ ! -d "sample/${pkg_name}.Sample" ]; then
        pkg_warnings="${pkg_warnings}\n  - missing sample/${pkg_name}.Sample/ (no runnable sample demonstrating it works)"
    fi

    if [ -n "$pkg_warnings" ]; then
        printf 'check-new-package-deliverables: new package %s is missing required deliverables:%b\n' "$pkg_name" "$pkg_warnings" >&2
    fi
done <<< "$new_fsprojs"

exit 0
