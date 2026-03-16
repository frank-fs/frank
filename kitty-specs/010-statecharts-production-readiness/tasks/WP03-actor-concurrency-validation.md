---
work_package_id: WP03
title: Actor Concurrency Validation & Documentation
lane: "planned"
dependencies: [WP02]
base_branch: 010-statecharts-production-readiness-WP01
base_commit: 7d5b7cdd35e1b13fb514c7646148835ae04c087f
created_at: '2026-03-16T04:03:04.598916+00:00'
subtasks:
- T017
- T018
- T019
- T020
phase: Phase 2 - Parallel Streams
assignee: ''
agent: "claude-opus-reviewer"
shell_pid: "3584"
review_status: "has_feedback"
reviewed_by: "Ryan Riley"
review_feedback_file: "/private/tmp/wp03-review-feedback.md"
history:
- timestamp: '2026-03-16T00:05:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-003
- FR-004
---

# Work Package Prompt: WP03 -- Actor Concurrency Validation & Documentation

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

**Reviewed by**: Ryan Riley
**Status**: ❌ Changes Requested
**Date**: 2026-03-16
**Feedback file**: `/private/tmp/wp03-review-feedback.md`

## Review Feedback for WP03 -- Actor Concurrency Validation & Documentation

### Reviewer: claude-opus-reviewer
### Date: 2026-03-16

---

### Overall Assessment: CHANGES REQUESTED

The documentation work (T017, T020) is excellent -- thorough, accurate, and well-structured XML docs on `IStateMachineStore`, its members, `StoreMessage`, and `MailboxProcessorStore`. The interface signature and implementation are confirmed unchanged (documentation-only changes). Three of the four new tests are solid.

However, there is one flaky test that must be fixed before approval.

---

### Issue 1: Flaky test -- "Interleaved GetState and SetState on same instance produce no torn reads" (MUST FIX)

**File**: `test/Frank.Statecharts.Tests/StoreTests.fs`, lines 337-364
**Severity**: Bug -- causes intermittent test failures (~50% failure rate in testing)

**Root cause**: The test sets an initial state of `("Initial", 0)` before the interleaved loop, but the assertion always expects `sprintf "State-%d" ctx`. When a read operation returns the initial state where `ctx = 0`, the assertion expects `"State-0"` but gets `"Initial"`, causing the test to fail.

The write operations in the loop start at `i = 0`, producing `("State-0", 0)`. But the initial `SetState "inst1" "Initial" 0` creates a state where `ctx = 0` maps to `"Initial"`, not `"State-0"`. If any read is processed before write `i=0`, it sees `("Initial", 0)` and the consistency check fails because `sprintf "State-%d" 0` = `"State-0"` != `"Initial"`.

**Fix**: Change the initial SetState to use the same naming convention as the loop:

```fsharp
// Before (line 342):
do! iface.SetState "inst1" "Initial" 0 |> Async.AwaitTask

// After:
do! iface.SetState "inst1" "State-0" 0 |> Async.AwaitTask
```

Alternatively, change the initial state context to `-1` so it never collides with the loop's naming:

```fsharp
do! iface.SetState "inst1" "Initial" -1 |> Async.AwaitTask
```

And then adjust the assertion to handle the initial state case:

```fsharp
let expectedState = if ctx = -1 then "Initial" else sprintf "State-%d" ctx
```

The simplest fix is the first option (`"State-0"` with context `0`).

---

### Dependency Check

- **WP03 declares dependency on WP02**: WP02 is still in `planned` lane (not started). However, after code review, WP03 does NOT actually depend on WP02's changes. The implementation only adds XML docs to `Store.fs` and concurrency tests to `StoreTests.fs` -- neither references Guard DU types from WP02. The declared dependency appears to be overly conservative. This is not a blocker but should be noted for the dependency graph.
- **WP05 depends on WP03**: WP05 is in `planned` lane, no rebase concern.

---

### Subtask-Level Review

| Subtask | Status | Notes |
|---------|--------|-------|
| T017 | PASS | XML docs on `IStateMachineStore`, its members, and `StoreMessage` are accurate and comprehensive |
| T018 | NEEDS FIX | "Concurrent SetState to same instance are serialized" test is fine, but "Interleaved GetState and SetState" has the flaky bug described above |
| T019 | PASS | "All state changes are observed (no lost updates via subscriber)" test is correct and reliable |
| T020 | PASS | Backpressure documentation on `MailboxProcessorStore` is well-written with actionable mitigation guidance |

---

### Additional Observations (non-blocking)

1. The "Subscriber notifications preserve sequential consistency" test uses sequential `SetState` calls rather than concurrent ones (intentionally), which is a good complementary test to the concurrent subscriber test in T019.

2. The existing `concurrencyTests` test list (pre-WP03) already tests 100 concurrent operations across 10 instances. The new `actorSerializationTests` correctly focuses on same-instance contention, which is the novel contribution.

3. All documentation accurately describes the actor-serialization contract and backpressure limitation as specified in the WP03 prompt.


## Implementation Command

Depends on WP02 -- branch from WP02:

```bash
spec-kitty implement WP03 --base WP02
```

---

## Objectives & Success Criteria

Validate and document that the existing `IStateMachineStore` contract assumes actor-serialized access. Add concurrency tests proving the `MailboxProcessorStore` serializes concurrent state operations correctly. Document the backpressure limitation.

**Success Criteria**:
1. `IStateMachineStore` interface and `MailboxProcessorStore` class have XML documentation describing the actor-serialization contract
2. Concurrency tests prove sequential processing of concurrent operations (no lost updates)
3. `MailboxProcessor` backpressure is documented as a known limitation
4. No source code changes to `IStateMachineStore` or `MailboxProcessorStore` (documentation only)
5. All tests pass

## Context & Constraints

- **Spec**: `/kitty-specs/010-statecharts-production-readiness/spec.md` -- User Story 2 (Actor-Serialized Concurrency)
- **Plan**: `/kitty-specs/010-statecharts-production-readiness/plan.md` -- Decision D-002
- **Research**: `/kitty-specs/010-statecharts-production-readiness/research.md` -- Decision 2 (Actor-Serialized Concurrency Model), Decision 5 (Backpressure)
- **Data Model**: `/kitty-specs/010-statecharts-production-readiness/data-model.md` -- IStateMachineStore (UNCHANGED), MailboxProcessorStore (UNCHANGED)

**Key Insight**: The `IStateMachineStore` interface is NOT modified. The actor-serialization requirement is a documented contract, not an interface-level enforcement. The `MailboxProcessorStore` already implements this correctly. This WP validates existing behavior and adds documentation.

**Parallel with WP04**: This WP can run in parallel with WP04 (SQLite store) since they touch independent concerns. WP03 validates existing `MailboxProcessorStore`; WP04 creates a new project.

## Subtasks & Detailed Guidance

### Subtask T017 -- Document actor-serialization contract on IStateMachineStore

- **Purpose**: Make the actor-serialization requirement explicit in the interface documentation so future store implementors know they must serialize access through an actor.
- **Files**: `src/Frank.Statecharts/Store.fs`
- **Steps**:
  1. Add XML doc comments to `IStateMachineStore`:
     ```fsharp
     /// <summary>
     /// Abstraction for state machine instance persistence.
     /// </summary>
     /// <remarks>
     /// <para>
     /// All implementations MUST serialize state access through an actor (e.g., <c>MailboxProcessor</c>).
     /// This ensures concurrent requests to the same instance are processed sequentially,
     /// preventing lost updates without requiring optimistic concurrency tokens.
     /// </para>
     /// <para>
     /// The actor is the sole accessor of the backing store. External code never reads or writes
     /// the backing store directly -- all operations go through <c>GetState</c>/<c>SetState</c>.
     /// </para>
     /// <para>
     /// For durable implementations (e.g., SQLite), persistence operations occur inside the actor loop.
     /// The actor wraps the backing store, not the other way around.
     /// </para>
     /// </remarks>
     type IStateMachineStore<'State, 'Context when 'State: equality> =
     ```

  2. Add XML docs to each interface member emphasizing actor serialization.
  3. Add XML docs to `StoreMessage` DU explaining it is private to the actor.

- **Notes**: These are documentation-only changes. The interface signature is unchanged.

### Subtask T018 -- Add concurrency serialization tests

- **Purpose**: Prove that the `MailboxProcessorStore` processes concurrent operations sequentially.
- **Files**: `test/Frank.Statecharts.Tests/StoreTests.fs`
- **Steps**:
  1. Add a test that fires multiple concurrent `SetState` calls to the SAME instance and verifies the final state reflects sequential processing:
     ```fsharp
     testAsync "Concurrent SetState to same instance are serialized" {
         let store, _ = makeStore ()
         use _s = store :> IDisposable
         let iface = store :> IStateMachineStore<string, int>

         // Fire 20 concurrent SetState operations to the same instance
         let completionOrder = System.Collections.Concurrent.ConcurrentBag<int>()
         let ops =
             [| for i in 0..19 ->
                    async {
                        do! iface.SetState "same-instance" (sprintf "State-%d" i) i |> Async.AwaitTask
                        completionOrder.Add(i)
                    } |]

         do! Async.Parallel ops |> Async.Ignore

         // All 20 operations should have completed
         Expect.equal completionOrder.Count 20 "all operations should complete"

         // The final state should be one of the 20 values (the last one processed)
         let! result = iface.GetState("same-instance") |> Async.AwaitTask
         Expect.isSome result "should have state after concurrent writes"
     }
     ```

  2. Add a test that interleaves `GetState` and `SetState` on the same instance and verifies no torn reads occur.

- **Notes**: Existing tests in `StoreTests.concurrencyTests` already test 100 concurrent operations, but they distribute across 10 instances. The new tests focus on same-instance contention to prove serialization.

### Subtask T019 -- Test verifying no lost updates

- **Purpose**: Explicitly verify the "no lost updates" property under concurrent access to the same instance.
- **Files**: `test/Frank.Statecharts.Tests/StoreTests.fs`
- **Parallel**: Yes
- **Steps**:
  1. Add a test using a notification subscriber to count all state changes:
     ```fsharp
     testAsync "All state changes are observed (no lost updates via subscriber)" {
         let store, _ = makeStore ()
         use _s = store :> IDisposable
         let iface = store :> IStateMachineStore<string, int>

         let received = System.Collections.Concurrent.ConcurrentBag<string * int>()
         let observer =
             { new IObserver<string * int> with
                 member _.OnNext(v) = received.Add(v)
                 member _.OnError(_) = ()
                 member _.OnCompleted() = () }

         let sub = iface.Subscribe "tracked" observer

         // 50 concurrent SetState calls
         let ops =
             [| for i in 1..50 ->
                    async { do! iface.SetState "tracked" (sprintf "S%d" i) i |> Async.AwaitTask } |]

         do! Async.Parallel ops |> Async.Ignore

         sub.Dispose()

         // Every SetState should have triggered a subscriber notification
         Expect.equal received.Count 50 "subscriber should receive all 50 state changes"
     }
     ```

  2. This test proves that no state changes are lost -- each `SetState` produces exactly one subscriber notification because the actor processes them sequentially.

### Subtask T020 -- Document MailboxProcessor backpressure limitation

- **Purpose**: Document that `MailboxProcessor` queues are unbounded and note mitigation strategies.
- **Files**: `src/Frank.Statecharts/Store.fs`
- **Parallel**: Yes
- **Steps**:
  1. Add XML doc comment to `MailboxProcessorStore`:
     ```fsharp
     /// <summary>
     /// In-memory <see cref="IStateMachineStore{TState, TContext}"/> backed by a <c>MailboxProcessor</c>.
     /// </summary>
     /// <remarks>
     /// <para>
     /// All state operations are serialized through the <c>MailboxProcessor</c> agent.
     /// Concurrent requests are queued and processed sequentially.
     /// </para>
     /// <para>
     /// <strong>Known limitation</strong>: The <c>MailboxProcessor</c> message queue is unbounded.
     /// Under extreme load, the queue can grow without limit. In practice, the HTTP server
     /// (Kestrel) provides implicit backpressure via connection limits.
     /// Monitor <c>CurrentQueueLength</c> in production for queue depth visibility.
     /// For bounded queue behavior, consider a custom store implementation with
     /// <c>inbox.CurrentQueueLength</c> checks.
     /// </para>
     /// </remarks>
     type MailboxProcessorStore<'State, 'Context when 'State: equality>
     ```

- **Notes**: This is documentation-only. No code behavior changes.

## Risks & Mitigations

1. **Non-deterministic concurrency tests**: Use sufficient iteration counts (20-50 operations) and verify aggregate properties (all operations complete, subscriber count matches) rather than exact ordering.

2. **Test timing**: The `MailboxProcessor` processes messages asynchronously. Use `PostAndAsyncReply` (which blocks until the reply is received) to ensure operations have completed before asserting.

3. **Existing concurrency tests**: `StoreTests.fs` already has `concurrencyTests`. The new tests complement these by focusing on serialization proof (subscriber notification counts, same-instance contention) rather than just "doesn't crash".

## Review Guidance

- Verify `IStateMachineStore` interface signature is UNCHANGED (only XML docs added)
- Verify `MailboxProcessorStore` implementation is UNCHANGED (only XML docs added)
- Verify concurrency tests prove serialization (subscriber count matches operation count)
- Verify backpressure is documented as a known limitation with mitigation guidance
- Run `dotnet test` to confirm all tests pass

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T00:05:00Z -- system -- lane=planned -- Prompt generated via /spec-kitty.tasks

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP03 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-16T04:03:04Z – claude-opus-4-6 – shell_pid=98858 – lane=doing – Assigned agent via workflow command
- 2026-03-16T04:15:11Z – claude-opus-4-6 – shell_pid=98858 – lane=for_review – Moved to for_review
- 2026-03-16T04:15:48Z – claude-opus-reviewer – shell_pid=3584 – lane=doing – Started review via workflow command
- 2026-03-16T04:20:47Z – claude-opus-reviewer – shell_pid=3584 – lane=planned – Moved to planned
