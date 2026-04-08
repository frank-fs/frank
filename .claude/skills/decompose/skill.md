---
name: decompose
description: |
  Break an issue into tasks so small and precise that each has exactly one correct
  implementation. Refuses to launch execution until the plan contains actual code
  shapes, verified file paths, and scope locks. Use before any implementation session.
---

# Decompose

Correctness-first issue decomposition. The plan IS the code.

## Why This Exists

Agents take shortcuts when specs have gaps. "Add a tagless-final algebra" has
infinite implementations. "Add this 6-field record to Types.fs line 245" has one.
This skill closes the gap between issue description and executable spec by
decomposing until each task is mechanical.

## When to Use

Before ANY implementation session. Especially:
- Library design issues (new types, new abstractions, new APIs)
- Issues where the agent would need to "figure out" the approach
- Issues that failed a previous implementation attempt

## Process

### Phase 1: Explore

Research-only. Map the actual code that will change.

For each area the issue touches:
1. Read the actual types, functions, and modules involved
2. Record file paths and line numbers
3. Identify every construction/consumption site (like the 66 TransitionEdge sites)
4. Note existing patterns the implementation must follow

**Output:** A structured inventory of what exists, with verified locations.

**Gate:** Can you name every file that will change? If no, keep exploring.

### Phase 2: Design the Types

The types ARE the plan. In F#, if the types are right, the implementation
has very few degrees of freedom.

For each new or modified type:
1. Write the actual F# type definition (not pseudocode)
2. Show the before/after for modified types
3. Verify it compiles in your head against existing consumers

**Output:** Concrete type definitions in actual F#.

**Gate:** Could someone implement this with ONLY the type definitions and
file locations? If the types don't sufficiently constrain the implementation,
add architectural constraints or anti-shortcuts.

### Phase 3: Decompose into Tasks

Break the work into ordered tasks. Each task:

1. **Names exactly which files are modified** (the scope lock)
2. **Shows the before/after code shape** (actual F#, not description)
3. **Has a checkpoint** (what to verify before proceeding to next task)
4. **Has one correct implementation** — if an agent could take a shortcut,
   the task is too big. Decompose further.

Task template:

```
### Task N: {verb} {what} in {where}

**Files:** {allowlist — only these files may be modified}

**Before:**
{actual current code from the file}

**After:**
{actual target code — the exact F# that should exist}

**Checkpoint:** {command to run + expected output}

**Scope lock:** Do NOT modify any file not listed above.
```

### Phase 4: Shortcut Audit

For each task, ask: "Could an agent produce correct-looking output
without following this plan?" If yes:

- The task is too vague — add the concrete before/after code
- The scope is too broad — narrow the file allowlist
- There's a faster wrong path — add it as an anti-shortcut

This phase is MANDATORY. Skip it and the session will fail.

### Phase 5: Present for Approval

Present the full task list to the user. The user verifies:
- Do the type definitions look right?
- Does the file list match their mental model?
- Are the checkpoints meaningful?

**The user decides when the plan is ready. Not the orchestrator.**

### Phase 6: Execute (one task at a time)

For each task:
1. Create or reuse a worktree
2. Write PROMPT.md containing ONLY that task (not the whole issue)
3. Include the scope lock, before/after code, and checkpoint
4. Launch the session
5. User verifies the checkpoint before proceeding to next task

If a task fails: the task spec has a gap. Return to Phase 3 and
refine THAT task. Do not retry with the same spec.

## Readiness Checklist

Before claiming a decomposition is ready, every item must be true:

- [ ] Every file to be modified is named with current line numbers
- [ ] Every new/modified type is shown as actual F# (not pseudocode)
- [ ] Every task has a scope lock (file allowlist)
- [ ] Every task has a before/after code shape
- [ ] Every task has a verification checkpoint
- [ ] The shortcut audit found no unaddressed gaps
- [ ] The user has approved the task list

If you cannot check a box, the plan is not ready.

## Anti-Patterns

| Anti-pattern | What happens | Fix |
|---|---|---|
| "The agent will figure out the record shape" | Agent invents a redesign | Write the actual record in the plan |
| Probability ratings as readiness signal | Orchestrator overestimates, user discovers late | Readiness checklist — falsifiable by user |
| Whole issue in one PROMPT.md | Agent has too much decision space | One task per PROMPT.md |
| "All design decisions are resolved" = ready | Decisions describe WHAT, not HOW | Types + file paths + before/after = ready |
| Skipping the shortcut audit | Agent finds the shortcut you didn't | Audit is mandatory, not optional |
