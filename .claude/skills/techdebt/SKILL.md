---
name: techdebt
description: Use when the user wants to find, catalog, or address technical debt. Also use when starting a cleanup sprint, looking for code quality improvements, or asking "what needs cleaning up?"
---

# Tech Debt Sweep

Systematically find technical debt across code and GitHub, then propose actionable fixes.

## Process

### 1. Gather from GitHub

```bash
# Issues labeled as tech debt, cleanup, refactor, or similar
gh issue list --label "tech-debt,cleanup,refactor,chore" --state open --json number,title,labels,milestone
# Also check for stale PRs
gh pr list --state open --json number,title,createdAt,labels
```

Search for TODO/FIXME/HACK in code:
```bash
grep -rn "TODO\|FIXME\|HACK\|XXX\|WORKAROUND" src/ test/ --include="*.fs" --include="*.fsproj"
```

### 2. Scan for code smells

Check for common patterns using project-appropriate tools:

- **Duplicated logic** — same function in 2+ modules (Constitution VIII violation)
- **Missing disposal** — `IDisposable` without `use` binding (Constitution VI violation)
- **Silent exception swallowing** — bare `with _ ->` in request paths (Constitution VII violation)
- **Dead code** — unused opens, unreferenced files, commented-out blocks
- **Dependency drift** — outdated NuGet packages, version mismatches across projects
- **Test gaps** — public modules without corresponding test files
- **Stale worktrees** — `git worktree list` showing abandoned branches

### 3. Categorize findings

| Priority | Category | Example |
|----------|----------|---------|
| **P0** | Constitution violations | Missing `use`, silent swallowing, duplicated logic |
| **P1** | Existing GitHub issues | Tagged tech-debt issues in current milestone |
| **P2** | Code smells | TODOs, dead code, test gaps |
| **P3** | Housekeeping | Stale branches, dependency drift, formatting |

### 4. Present findings

Output a table sorted by priority:

```
## Tech Debt Report — [date]

### P0: Constitution Violations (fix immediately)
| # | File:Line | Issue | Constitution |
|---|-----------|-------|-------------|
| 1 | src/Foo.fs:42 | StreamReader not disposed | VI |

### P1: Open GitHub Issues
| # | Issue | Title | Milestone |
|---|-------|-------|-----------|

### P2: Code Smells
| # | File:Line | Issue |
|---|-----------|-------|

### P3: Housekeeping
| # | Item | Action |
|---|------|--------|

**Quick wins** (< 30 min each): [list items that are fast to fix]
**Needs planning** (> 30 min): [list items that need a spec or plan]
```

### 5. Act on approval

For approved items:
- **P0**: Fix in current session, commit immediately
- **P1**: Link to existing issue, update with findings
- **P2**: File new GitHub issues for items needing separate work, fix quick wins now
- **P3**: Fix in a single cleanup commit

## What NOT to do

- Don't refactor working code just because it's "not ideal"
- Don't add types, comments, or abstractions that aren't needed
- Don't file issues for things that are intentional design choices
- Don't count style preferences as tech debt
- Check CLAUDE.md and constitution before flagging — it might be by design
