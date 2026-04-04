---
name: milestone-execute
description: |
  Use when implementing a milestone with multiple issues. Thesis-first approach: write
  failing E2E tests before issues, expert-review acceptance criteria before implementation,
  use dedicated terminal sessions (not subagents), and verify E2E yourself before merging.
  Includes review cycle that creates follow-up issues when expert review surfaces new gaps.
---

# Milestone Execute

Thesis-first execution for milestones. The E2E test is the spec. The issue describes
the thesis, not the implementation. Dedicated terminal sessions, not subagents.

## Why This Process Exists

Four iteration cycles produced checkbox-complete implementations that failed expert
review because the underlying thesis was unproven. Root cause: issues described
symptoms ("add CE operation X") and agents fixed symptoms without proving the thesis.
Subagents optimize for the prompt — if the prompt says "add X," they add X. They
don't verify the thesis holds end-to-end.

## Prerequisites

- Milestone exists with open issues (or findings to convert into issues)
- Each issue has thesis-first acceptance criteria (not implementation checkboxes)
- E2E test script exists that fails before implementation

## Process

### Phase 0: Write the Failing E2E Test

Before creating any issues:

1. Write `test-e2e.sh` (or equivalent) with concrete HTTP request/response pairs
   that prove the thesis
2. Include **negative tests** — remove a crutch (flat transition, hardcoded value)
   and prove the real mechanism works
3. Include **falsifiable tests** — tests where the correct output cannot be produced
   without the underlying mechanism being correct
4. Run the test. It MUST fail. If it passes, the test isn't testing the thesis.

The E2E test IS the spec. Issues are "make these test lines green."

**Next step:** Present the failing E2E test to the user for review.

### Phase 1: Create Thesis-First Issues

For each issue, follow the Issue Template below. Key rules:

- Put thesis-level acceptance criteria in the issue body VERBATIM
- Do NOT translate them into implementation instructions
- The agent figures out the implementation by making the acceptance tests pass
- Include dependencies on other issues so wave planning is explicit

**Next step:** Present each issue draft to the user for review before creating
on GitHub. Then expert-review the acceptance criteria (not code) before
implementation begins.

### Phase 2: Expert Review Acceptance Criteria

Before any implementation:

1. Dispatch 2-4 relevant experts to review the ACCEPTANCE CRITERIA (not code)
2. Each expert checks: "If these tests pass, does the thesis hold?"
3. Experts identify tests that can be faked (shortcut to correct output without
   correct mechanism) and propose falsifiable alternatives
4. Revise acceptance criteria based on expert feedback

**Next step:** Present expert feedback and revised criteria to the user.

### Phase 3: Wave Planning

Group issues into dependency waves:

```bash
gh issue list --milestone "<name>" --state open --json number,title,labels
```

- Wave N+1 depends on Wave N
- Issues within a wave have zero shared files
- Merge order: fewest shared-file changes first

**Next step:** Present wave plan for user approval.

### Phase 3.5: Design Exploration (library design issues only)

Some issues require creative design work — new APIs, new abstractions, new
middleware operations. These cannot be solved by constraints alone. An agent
implements designs; it does not create them.

**When to use this phase:** The issue's thesis is about library/API design
(not wiring, fixing, or extending an existing API). Ask: "Does the agent
need to invent a new abstraction, or use an existing one?" If invent → do
this phase.

1. **Explore the current architecture** in a research-only session. Map the
   relevant code paths, types, and assumptions. Do NOT implement anything.
2. **Present findings** to the user — structured summary with file paths,
   line numbers, and design assumptions that conflict with the thesis.
3. **Iterate on the design** with the user. Propose the new API surface,
   get feedback, revise. The design is done when the user approves it.
4. **Write the design into the issue** as context — not as implementation
   instructions, but as "the middleware API should support X, Y, Z"
   alongside the architectural constraints and anti-shortcuts.

The design becomes input to the issue. The agent implements the approved
design and proves it works via the acceptance tests.

**Why this phase exists:** #250 failed 5 attempts because the issue had
a library design thesis but no design — only desired output. Agents found
shortcuts every time. Architectural constraints prevent wrong approaches
(90%) but don't guarantee the right approach (55%). Working out the design
first raises that to ~85%.

**Next step:** Present design exploration findings and proposed API to user.

### Phase 4: Implementation via Dedicated Terminal Sessions

Boris's approach: use dedicated terminal sessions in worktrees, NOT subagents.

#### Preparation (orchestrating session does this)

For each issue in the current wave, the orchestrating session:

1. **Creates the worktree:**
   ```bash
   git worktree add .claude/worktrees/{name} -b feature/{issue}-{short-name} master
   ```

2. **Writes PROMPT.md in the worktree root** with the full session prompt.
   IMPORTANT: Write PROMPT.md AFTER posting any addendum comments (architectural
   constraints, anti-shortcuts). Include both the issue body AND all comments.

   ```bash
   gh issue view {number} --json body --jq '.body' > .claude/worktrees/{name}/PROMPT.md
   gh api repos/{owner}/{repo}/issues/{number}/comments --jq '.[].body' >> .claude/worktrees/{name}/PROMPT.md
   ```

   PROMPT.md contains:
   - The GitHub issue body (thesis + acceptance tests) — fetched via `gh issue view`
   - All issue comments (including architectural constraints addenda)
   - The E2E test instructions
   - TDD instruction: write failing tests for each acceptance criterion FIRST,
     then implement to make them pass
   - Verification instruction: run E2E test, do not claim done without evidence

   Template for PROMPT.md content:
   ```markdown
   # Issue #{number}: {title}

   {Full issue body from GitHub — paste verbatim}

   ---

   ## Instructions

   Make the acceptance tests in the issue above pass.

   1. Read the ENTIRE issue — thesis, architectural constraints, anti-shortcuts,
      implementation sequence, and acceptance tests are ALL part of the spec
   2. Follow the implementation sequence if one is provided — do not skip phases
   3. Respect architectural constraints — if the issue says the solution must be
      in the library, do not hand-code it in the application
   4. Check anti-shortcuts before claiming done — if your implementation matches
      a listed anti-shortcut, it is wrong regardless of test results
   5. Follow TDD (`superpowers:test-driven-development`): write a failing test
      for each acceptance criterion FIRST, then implement to make it pass
   6. Run `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln` and
      `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"`
      to verify nothing is broken
   7. Run the E2E test if one exists
   8. Do not claim done without build + test evidence in your output
   ```

3. **Presents the start command** for each session:
   ```bash
   cd .claude/worktrees/{name} && claude --name "{issue-short-name}"
   ```
   The user opens the session, then pastes the content of PROMPT.md as
   the first message (or reads it with `/read PROMPT.md`).

#### Why terminal sessions over subagents

- Full permissions (can run servers, curl, E2E tests)
- Persistent context (no context window pressure)
- User can observe progress in real-time
- User can intervene and redirect
- No "agent said success" trust problem — you see the output

#### TDD integration (`superpowers:test-driven-development`)

- Each acceptance criterion becomes a failing test BEFORE implementation
- Red → Green → Refactor cycle for each criterion
- E2E test is the outer loop; unit/integration tests are the inner loop
- The red step is mandatory — if the test passes before implementation,
  the test isn't testing the right thing

**Next step:** Create worktrees, write PROMPT.md files, and present the
start commands for each terminal session to the user.

### Phase 5: Verification (User Runs E2E)

After each terminal session claims completion:

1. **User runs the E2E test** — not the agent, not a subagent, the USER
2. Every acceptance criterion must pass with observable evidence
3. If any test fails, the issue is not done — send the failure back to the
   terminal session
4. Only after E2E passes: run `/simplify` and `/expert-review` on the diff

CRITICAL: Do not trust agent self-reports. Do not trust "all tests pass"
without seeing the output yourself. Run the test. Read the output.

**Next step:** Present E2E results to the user. If all pass, proceed to
review. If any fail, go back to Phase 4.

### Phase 6: Review and Follow-Up Issue Creation

After E2E passes, run `/expert-review` on the completed work.

#### Triage expert findings into three buckets:

1. **Blocking** — thesis is not proven despite E2E passing (test has a gap)
   - Fix before merge. Update the E2E test to cover the gap, then return
     to Phase 4.

2. **In-scope follow-up** — real gap but separable from this issue
   - Create a new issue using the Issue Template below
   - Add to the current milestone
   - Add dependency on the current issue (merges first)
   - Include the expert finding as the "Current problem" section

3. **Out-of-scope** — valid concern but belongs to a future milestone
   - Create issue with the `future` label
   - Do NOT add to current milestone
   - Note in the current PR body: "Deferred: #{new-issue} — {rationale}"

#### For each new follow-up issue:

- Write it using the Issue Template (thesis → problem → definition → solution
  → acceptance tests → sources)
- The acceptance test must be an HTTP exchange, not a code inspection
- Add to the wave plan — does it fit in the current wave or the next?
- Present to the user before creating on GitHub

CRITICAL: Never defer findings without user consent. Never close an issue
when expert review surfaces unaddressed gaps. The review cycle is:
E2E passes → expert review → create follow-up issues → user approves
merge → merge.

**Next step:** Present expert findings with triage recommendations and
any new issue drafts to the user.

### Phase 7: PR and Merge

For each completed issue:

1. Create PR with per-requirement accounting (every acceptance test → PASS
   with evidence from the E2E output)
2. PR body must list any follow-up issues created during review
3. Wait for CI
4. Merge in dependency order (fewest shared files first)
5. `git pull` to update master before next wave

**Next step:** After all issues in a wave merge, proceed to next wave
(Phase 4) or to completion (Phase 8).

### Phase 8: Completion

After all waves merge:

1. Verify `gh issue list --milestone "<name>" --state open` returns only
   follow-up issues (not original issues)
2. Run the FULL E2E test suite against merged master
3. Run `/retrospective` to capture session learnings
4. Update memory with milestone completion status

**Next step:** Present completion status and any open follow-up issues
to the user.

---

## Issue Template

```markdown
## Thesis

{What must be true for Frank's thesis to hold in this area}

## Current problem

{What happens today — walk through the request lifecycle showing the gap}

## Definition: "{key term}"

{What the key term means, stated so an external observer can verify
from HTTP responses alone}

## Proposed solution

{High-level approach — NOT file:line instructions}

## Architectural constraints

{Where the solution must live and what boundaries it must respect.
These are NOT implementation instructions — they constrain the
approach without prescribing specific code. Required for library
design issues; optional for wiring/fix issues.}

- The solution MUST be in {library/middleware}, not {application/sample}
- Application code MUST only use {public API surface}
- Application code MUST NOT directly reference {internal types}

## Implementation sequence

{Ordered phases that prevent skipping to the end. Each phase has
a verifiable checkpoint. These describe WHAT to build in what
order, not HOW to build it.}

1. {Library/API change} — checkpoint: {how to verify this phase}
2. {Tests proving the API works} — checkpoint: {tests pass}
3. {Application uses the API} — checkpoint: {compiles using only public API}
4. {E2E acceptance tests} — checkpoint: {HTTP exchanges pass}

## Anti-shortcuts

{Known failure modes from previous attempts. Explicit "do NOT"
with explanation of why it produces correct output but wrong
design. Omit for first-attempt issues — populate after a failed
attempt so the next agent doesn't repeat the mistake.}

- Do NOT {shortcut} — produces correct output because {why} but
  wrong design because {why}

## Acceptance tests

Each test is verified by test-e2e.sh. The issue is not done until every
test produces the specified response.

### 1. {Test name}

```
{HTTP method} {URL} → {expected status}
{Response body / header assertion}
```

{Why this test is falsifiable — what mechanism must be correct for it to pass}

### 2. {Test name}
...

## Dependencies

- Depends on: #{issue} — {why this must land first}
- Blocks: #{issue} — {why that issue needs this}

## Expert sources

- **{Expert}**: {finding summary}
```

---

## Anti-Patterns

| Anti-pattern | Why it fails | Correct approach |
|--------------|-------------|------------------|
| Implementation instructions in issues | Agent follows recipe without understanding thesis | Thesis + acceptance tests + architectural constraints + anti-shortcuts |
| HTTP-only acceptance for library design issues | Agent finds shortest path to green output, bypassing the library | Add architectural constraints on where solution lives; do Phase 3.5 design exploration first |
| Retrying same spec after failure | Same spec produces same shortcut every time | After first failure, fix the spec — the spec has a loophole, not the agent |
| Subagents for implementation | Can't run servers, limited permissions, trust problem | Dedicated terminal sessions |
| Agent self-reports as verification | "All tests pass" without evidence | User runs E2E, reads output |
| Expert review after implementation only | Finds same gaps again | Expert review acceptance criteria BEFORE implementation, then review code AFTER |
| Sample app as separate issue | Framework issues close without proving thesis | Sample's E2E test IS the acceptance criteria |
| Checkbox acceptance criteria | Green checkboxes, unproven thesis | HTTP request/response pairs |
| Closing issues when review finds gaps | Gaps ship as "done" | Create follow-up issues, get user approval before merge |
| Deferring findings without consent | Scope quietly shrinks | Present all findings, user decides what's blocking vs follow-up |

## Decision Log

Maintain during execution:

| Decision | Rationale | Impact |
|----------|-----------|--------|
| {what} | {why} | {scope} |
