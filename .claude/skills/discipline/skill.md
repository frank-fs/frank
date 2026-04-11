---
name: discipline
description: |
  Run Holzmann "Power of Ten" discipline review on changed code. Checks nesting depth,
  loop bounds, function size, preconditions, mutable state, side effect surfacing, and
  indirection depth. Use after writing code, before committing, or as part of PR review.
  Outputs a weighted letter grade (A-F) with stoplight color.
---

# Discipline Review

Enforce Holzmann's "Power of Ten" rules (adapted for F#) on changed code.

## Arguments

- `--scope <mode>` — What to review. Options: `diff` (default), `branch`, `pr`, `full`, or a file/directory path.
- `--voice <name>` — Output persona. Options: `mission-control` (default), `stephen-fry`, `neil-degrasse-tyson`, `david-attenborough`, `gordon-ramsay`.

## Scope Modes

| Scope | Files reviewed | Resolved via | When to use |
|-------|---------------|-------------|-------------|
| `diff` (default) | Unstaged + staged changes | `git diff --name-only -- '*.fs'` + `git diff --cached --name-only -- '*.fs'` | After writing code, before committing |
| `branch` | All changes since branching from main | `git diff main...HEAD --name-only -- '*.fs'` | Before creating a PR |
| `pr` | Files changed in current PR | `gh pr diff --name-only` filtered to `*.fs` | Reviewing an open PR |
| `full` | All source files in the project | `find src/ test/ sample/ -name '*.fs'` | Baseline audit, tech debt sweep |
| `<path>` | Specific file or directory | Direct path, glob if directory | Targeted review |

For `full` scope on large codebases: warn the user this may take a while, then proceed.

## Process

1. Parse arguments. Extract `--scope` (default: `diff`) and `--voice` (default: `mission-control`).

2. Resolve the file list based on scope:
   - `diff`: Run `git diff --name-only -- '*.fs'` and `git diff --cached --name-only -- '*.fs'`. Combine results.
   - `branch`: Run `git diff main...HEAD --name-only -- '*.fs'`.
   - `pr`: Run `gh pr diff --name-only`, filter to `*.fs` files.
   - `full`: Run `find src/ test/ sample/ -name '*.fs'` (adjust paths if project structure differs).
   - `<path>`: If a file, use directly. If a directory, find all `*.fs` files within it.

3. If no files found, ask the user which files to review.

4. Dispatch the `code-discipline` agent on the resolved file list.
   Include in the agent prompt: `Voice: <voice-name>` and `Scope: <scope>` so the agent has context.

5. The agent returns findings with a grade summary. Present it directly.

6. For Critical or High findings (automatic F), offer to fix them (with user approval).

## Grading

| Grade | Range | Stoplight | Meaning |
|-------|-------|-----------|---------|
| A | 93-100 | 🟢 Green | Ship it |
| A- | 90-92 | 🟢 Green | Clean |
| B+ | 87-89 | 🟡 Yellow | Minor polish needed |
| B | 83-86 | 🟡 Yellow | Acceptable |
| B- | 80-82 | 🟡 Yellow | Review recommended |
| C+ | 77-79 | 🟠 Orange | Needs work |
| C | 73-76 | 🟠 Orange | Significant issues |
| C- | 70-72 | 🟠 Orange | Borderline |
| D | 60-69 | 🟠 Orange | Do not merge |
| F | 0-59 | 🔴 Red | Failing |

## Severity Weights

| Severity | Rules | Per finding | Auto-fail? |
|----------|-------|-------------|------------|
| Critical | 13, 14 | Score = 0 | 1 = instant F |
| High | 9, 10, 12 | Score = 50 | 1 = instant F |
| Medium | 11, 15 | -8 | 6 = F |
| Low | — | -3 | 14 = F |

## Rules Checked

| # | Rule | Severity | What it catches |
|---|------|----------|----------------|
| 9 | Keep It Linear | High | Nesting > 2 levels deep |
| 10 | Bound Every Loop | High | Unbounded recursion, retry, poll |
| 11 | One Function, One Job | Medium | Functions > 60 lines, "and" descriptions |
| 12 | State Your Assumptions | High | Missing precondition checks at boundaries |
| 13 | Narrow Your State | Critical | Module-level mutable, distant state |
| 14 | Surface Your Side Effects | Critical | I/O hidden behind pure-looking names |
| 15 | One Layer of Indirection | Medium | >1 layer of dispatch to reach actual work |

## When to Use

- After writing code, before committing
- As part of `/expert-review` (dispatch alongside other experts)
- When reviewing AI-generated code (which routinely violates these rules)
- When the user asks "does this follow the discipline rules?"
