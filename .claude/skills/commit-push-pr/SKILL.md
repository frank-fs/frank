---
name: commit-push-pr
description: Commit staged changes, push branch, and create a PR with full pre-flight verification.
---

Streamlined commit → push → PR workflow. Run the full verification sequence first, then ship.

## Pre-flight

1. Confirm we are NOT on master: `git rev-parse --abbrev-ref HEAD`
   - If on master, STOP and tell the user to create a worktree.

2. Run build + test verification:
   ```
   DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln
   DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"
   DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Tests/
   dotnet fantomas --check src/
   ```

3. If any step fails, fix it before proceeding.

## Ship

1. `git status` — review changes
2. `git diff --staged` and `git diff` — review what's going in
3. `git log --oneline master..HEAD` — review commit history
4. Stage and commit with a descriptive message
5. Push: `git push -u origin HEAD`
6. Create PR with `gh pr create`:
   - Title: short, under 70 chars
   - Body: must include `Closes #XX` if there's a related issue
   - Body: must enumerate all issue requirements with status
   - Use the standard PR template format

## Rules

- Never force push
- Never merge without explicit approval
- PR body must enumerate all issue requirements with status per CLAUDE.md workflow rules
