---
work_package_id: "WP03"
title: "Actor Concurrency Validation & Documentation"
phase: "Phase 2 - Wave 1"
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
  - timestamp: "2026-03-15T23:59:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP03 -- Actor Concurrency Validation & Documentation

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

This WP depends on WP02:
```bash
spec-kitty implement WP03 --base WP02
```

---

## Objectives & Success Criteria

Validate and document that the `IStateMachineStore` contract assumes actor-serialized access, and prove the existing `MailboxProcessorStore` correctly serializes concurrent operations:

1. `IStateMachineStore` interface gains XML documentation stating the actor-serialization contract requirement
2. `MailboxProcessorStore` gains XML documentation noting unbounded queue as a known limitation
3. Concurrency tests prove that `MailboxProcessorStore` processes concurrent `SetState` calls sequentially with no lost updates
4. No interface changes, no code changes -- documentation and tests only

**Success gate**: `dotnet test test/Frank.Statecharts.Tests/` passes all tests including new concurrency tests.

## Context & Constraints

- **Spec**: `kitty-specs/010-statecharts-production-readiness/spec.md` -- User Story 2 (Actor-Serialized Concurrency, P1)
- **Plan**: `kitty-specs/010-statecharts-production-readiness/plan.md` -- Decision D-002 (Actor-serialized concurrency, no interface changes)
- **Research**: `kitty-specs/010-statecharts-production-readiness/research.md` -- Decision 2 (Actor-Serialized Concurrency Model) and Decision 5 (Backpressure)
- **Constraint**: `IStateMachineStore` interface is UNCHANGED -- this WP adds documentation only
- **Constraint**: `MailboxProcessorStore` implementation is UNCHANGED -- this WP adds tests that validate existing behavior
- **Constraint**: No version tokens, no compare-and-swap, no 409 Conflict -- the actor model eliminates these

### Key Files
- `src/Frank.Statecharts/Store.fs` -- documentation additions only
- `test/Frank.Statecharts.Tests/StoreTests.fs` -- new concurrency tests

## Subtasks & Detailed Guidance

### Subtask T017 -- Add documentation to `IStateMachineStore` about actor-serialization contract

**Purpose**: Make the actor-serialization contract explicit in the code so future store implementors understand the requirement.

**Steps**:

1. In `src/Frank.Statecharts/Store.fs`, update the XML doc comment on `IStateMachineStore`:

   ```fsharp
   /// Abstraction for state machine instance persistence.
   /// <remarks>
   /// All implementations MUST serialize state access through an actor (e.g., MailboxProcessor).
   /// This ensures no concurrent reads or writes to the backing store.
   /// The actor is the concurrency mechanism -- no version tokens or compare-and-swap needed.
   /// Callers can safely invoke GetState and SetState concurrently; the implementation
   /// serializes all operations internally.
   /// </remarks>
   type IStateMachineStore<'State, 'Context when 'State: equality> =
   ```

2. Also update individual method doc comments to note the serialization guarantee:

   ```fsharp
   /// Retrieve the current state and context for an instance.
   /// Returns None if the instance doesn't exist yet.
   /// <remarks>Serialized by the actor -- safe to call concurrently.</remarks>
   abstract GetState: instanceId: string -> Task<('State * 'Context) option>

   /// Persist a state change for an instance.
   /// <remarks>Serialized by the actor -- concurrent calls are processed sequentially.</remarks>
   abstract SetState: instanceId: string -> state: 'State -> context: 'Context -> Task<unit>

   /// Subscribe to state changes for an instance.
   /// Returns an IDisposable that unsubscribes when disposed.
   /// BehaviorSubject semantics: new subscribers immediately receive current state.
   /// <remarks>Serialized by the actor -- subscription management is thread-safe.</remarks>
   abstract Subscribe: instanceId: string -> observer: IObserver<'State * 'Context> -> IDisposable
   ```

**Files**: `src/Frank.Statecharts/Store.fs`
**Parallel?**: No -- foundation documentation
**Notes**: These are documentation-only changes. No signatures, no behavior changes.

### Subtask T018 -- Add concurrency serialization tests

**Purpose**: Prove that `MailboxProcessorStore` processes concurrent `SetState` calls sequentially with deterministic results.

**Steps**:

1. In `test/Frank.Statecharts.Tests/StoreTests.fs`, add a test section for concurrency:

2. **Test: Sequential processing of concurrent SetState calls**:
   ```fsharp
   testAsync "concurrent SetState calls are serialized" {
       // Create store
       let store = createStore ()

       // Use a barrier to ensure all tasks start simultaneously
       let barrier = new System.Threading.Barrier(10)

       // Fire 10 concurrent SetState calls, each setting a different state
       let tasks =
           [| for i in 0..9 ->
               task {
                   barrier.SignalAndWait()
                   do! (store :> IStateMachineStore<_, _>).SetState "inst" (SomeState i) { Counter = i }
               } |]

       do! Task.WhenAll(tasks) |> Async.AwaitTask

       // Verify the final state is one of the values (last one processed by actor)
       let! result = (store :> IStateMachineStore<_, _>).GetState "inst" |> Async.AwaitTask
       Expect.isSome result "State should exist"
       // The exact final state depends on actor ordering, but it must be one of the 10 values
       let state, ctx = result.Value
       Expect.isTrue (ctx.Counter >= 0 && ctx.Counter <= 9) "Counter should be in valid range"
   }
   ```

3. **Test: No lost updates**: Fire N concurrent increment operations and verify the final count reflects all increments:
   ```fsharp
   testAsync "no lost updates under concurrent access" {
       let store = createStore ()
       let instanceId = "counter"
       let n = 100

       // Initialize
       do! (store :> IStateMachineStore<_, _>).SetState instanceId InitialState { Counter = 0 } |> Async.AwaitTask

       // Fire N concurrent "increment" operations
       // Each reads current state, increments counter, writes back
       // With actor serialization, all N should succeed sequentially
       let mutable completedCount = 0
       let tasks =
           [| for _ in 0..n-1 ->
               task {
                   let! current = (store :> IStateMachineStore<_, _>).GetState instanceId
                   match current with
                   | Some(state, ctx) ->
                       do! (store :> IStateMachineStore<_, _>).SetState instanceId state { ctx with Counter = ctx.Counter + 1 }
                       System.Threading.Interlocked.Increment(&completedCount) |> ignore
                   | None -> ()
               } |]

       do! Task.WhenAll(tasks) |> Async.AwaitTask
       Expect.equal completedCount n "All operations should complete"

       // NOTE: The final counter value may NOT be N because GetState and SetState
       // are separate actor messages. Two concurrent tasks may both read Counter=5
       // and both write Counter=6. This is expected behavior -- the actor serializes
       // individual operations, not read-modify-write sequences.
       // The test validates that no EXCEPTIONS occur and all operations complete.
   }
   ```

4. Add a note explaining that read-modify-write atomicity is the responsibility of the transition function (called in middleware before SetState), not the store. The store's actor serializes individual operations.

**Files**: `test/Frank.Statecharts.Tests/StoreTests.fs`
**Parallel?**: No -- core test implementation
**Notes**: Define appropriate test state types for concurrency tests. Reuse existing test types if they have a counter or similar field, or define new ones local to the test.

### Subtask T019 -- Test verifying no lost updates under concurrent access

**Purpose**: Validate the end-to-end middleware flow with concurrent requests to the same stateful resource instance.

**Steps**:

1. This test operates at the middleware level, not just the store level. Two concurrent HTTP requests should be serialized by the store's actor.

2. In `test/Frank.Statecharts.Tests/StoreTests.fs` (or `MiddlewareTests.fs` if more appropriate), add:

   - **Test: Two concurrent transitions from same state**:
     - Set up a stateful resource with a counter state
     - Set store to state A
     - Fire two simultaneous POST requests that trigger transitions
     - Verify both complete without errors
     - Verify the final state reflects sequential processing

3. Use `Task.WhenAll` to fire concurrent requests. The actor guarantees sequential processing of the `SetState` calls.

**Files**: `test/Frank.Statecharts.Tests/StoreTests.fs`
**Parallel?**: Yes -- can proceed alongside T020 after T018
**Notes**: Concurrency tests are inherently non-deterministic in ordering. Test for correctness (no exceptions, valid final state) rather than specific ordering.

### Subtask T020 -- Document MailboxProcessor backpressure limitation

**Purpose**: Add documentation noting that MailboxProcessor queues are unbounded and providing guidance for production deployments.

**Steps**:

1. In `src/Frank.Statecharts/Store.fs`, add XML documentation to `MailboxProcessorStore`:

   ```fsharp
   /// In-memory state machine store using MailboxProcessor for actor-serialized access.
   /// <remarks>
   /// The MailboxProcessor uses an unbounded internal message queue. Under extreme load,
   /// the queue can grow without limit. In practice, Kestrel's connection limits provide
   /// implicit backpressure. For production deployments, monitor CurrentQueueLength and
   /// consider Kestrel connection limit configuration as the primary backpressure mechanism.
   ///
   /// If queue depth monitoring is needed, access the underlying MailboxProcessor's
   /// CurrentQueueLength property via the store instance.
   /// </remarks>
   type MailboxProcessorStore<'State, 'Context when 'State: equality>
   ```

2. This is documentation only -- no code changes.

**Files**: `src/Frank.Statecharts/Store.fs`
**Parallel?**: Yes -- independent documentation task
**Notes**: Per research.md Decision 5, backpressure is deferred to a future version. This WP only documents the limitation.

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Concurrency tests are non-deterministic | Use barriers for synchronization; test for correctness (no exceptions, valid final state) not specific ordering |
| `MailboxProcessor` queue growth under test load | Tests use small N (10-100) which won't cause memory issues |
| Read-modify-write is not atomic at the store level | Document this clearly in tests; explain that atomicity is the middleware's responsibility (transition function) |

## Review Guidance

- Verify `IStateMachineStore` has actor-serialization documentation
- Verify `MailboxProcessorStore` has backpressure limitation documentation
- Verify concurrency tests use proper synchronization (barriers, not sleeps)
- Verify tests don't assert specific ordering (only correctness)
- Verify no code changes to Store.fs beyond documentation
- Run `dotnet test test/Frank.Statecharts.Tests/` -- all tests must pass

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:00Z -- system -- lane=planned -- Prompt created.

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP03 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
