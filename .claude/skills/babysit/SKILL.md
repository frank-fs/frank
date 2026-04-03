---
name: babysit
description: Babysit open PRs — auto-rebase, fix CI, address review comments. Use with /loop 5m /babysit for hands-free PR management.
model: opus
---

Check all open PRs in this repo and shepherd them toward merge:

1. **List open PRs**: `gh pr list --state open --json number,title,headRefName,statusCheckRollup,reviewDecision`

2. **For each PR**, check in order:

   a. **CI status**: If checks are failing, read the failure logs (`gh pr checks <number>`), diagnose, and fix in the PR's branch worktree.

   b. **Merge conflicts**: If the PR has conflicts with master, rebase it:
      ```
      git worktree add .worktrees/rebase-<number> <branch>
      cd .worktrees/rebase-<number>
      git rebase master
      git push --force-with-lease
      ```

   c. **Review comments**: If there are unresolved review comments (`gh api repos/{owner}/{repo}/pulls/<number>/comments`), address them in a worktree and push.

   d. **Stale**: If the PR is >7 days old with no activity, report it.

3. **Report** a summary table:

| PR | Branch | CI | Conflicts | Reviews | Action Taken |
|----|--------|-----|-----------|---------|-------------|

Do NOT merge any PR. Only fix CI, rebase, and address comments. Merging requires explicit approval.
