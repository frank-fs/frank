---
name: discipline
description: |
  Run Holzmann "Power of Ten" discipline review on changed code. Checks nesting depth,
  loop bounds, function size, preconditions, mutable state, side effect surfacing, and
  indirection depth. Use after writing code, before committing, or as part of PR review.
---

# Discipline Review

Enforce Holzmann's "Power of Ten" rules (adapted for F#) on changed code.

## Process

1. Determine the diff scope:
   ```bash
   git diff --name-only -- '*.fs'
   git diff --cached --name-only -- '*.fs'
   ```
   If on a branch: `git diff main...HEAD --name-only -- '*.fs'`
   If no changes found, ask the user which files to review.

2. Dispatch the `code-discipline` agent on the changed files.

3. Present findings grouped by severity:
   - **HARD violations** — must fix before merge
   - **WARN** — should fix, user decides

4. For HARD violations, offer to fix them (with user approval).

## When to Use

- After writing code, before committing
- As part of `/expert-review` (dispatch alongside other experts)
- When reviewing AI-generated code (which routinely violates these rules)
- When the user asks "does this follow the discipline rules?"

## Rules Checked

| # | Rule | What it catches |
|---|------|----------------|
| 9 | Keep It Linear | Nesting > 2 levels deep |
| 10 | Bound Every Loop | Unbounded recursion, retry, poll |
| 11 | One Function, One Job | Functions > 60 lines, "and" descriptions |
| 12 | State Your Assumptions | Missing precondition checks at boundaries |
| 13 | Narrow Your State | Module-level mutable, distant state |
| 14 | Surface Your Side Effects | I/O hidden behind pure-looking names |
| 15 | One Layer of Indirection | >1 layer of dispatch to reach actual work |
