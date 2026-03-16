---
work_package_id: "WP03"
title: "Actor Concurrency Validation & Documentation"
phase: "Phase 2 - Parallel Streams"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP02"]
requirement_refs:
  - "FR-003"
  - "FR-004"
subtasks:
  - "T017"
  - "T018"
  - "T019"
  - "T020"
history:
  - timestamp: "2026-03-16T00:05:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP03 -- Actor Concurrency Validation & Documentation

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

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
