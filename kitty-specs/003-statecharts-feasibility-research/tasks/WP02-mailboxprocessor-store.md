---
work_package_id: "WP02"
subtasks:
  - "T006"
  - "T007"
  - "T008"
  - "T009"
title: "MailboxProcessor Store Implementation"
phase: "Phase 1 - Foundation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs: []
history:
  - timestamp: "2026-03-06T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- MailboxProcessor Store Implementation

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP02 --base WP01
```

Depends on WP01 (core types and `IStateMachineStore` interface).

---

## Objectives & Success Criteria

- Implement `MailboxProcessorStore<'S,'C>` as the default `IStateMachineStore` implementation
- Store can create instances, get/set state, subscribe to changes, and dispose cleanly
- BehaviorSubject semantics: new subscribers immediately receive current state
- Proper `IDisposable` implementation with `Stop` message draining pending operations
- DI registration helper: `IServiceCollection.AddStateMachineStore<'S,'C>()`
- All store tests pass including concurrency tests

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/003-statecharts-feasibility-research/plan.md` -- DD-04 (Provenance hooks via observable)
- `kitty-specs/003-statecharts-feasibility-research/data-model.md` -- `IStateMachineStore`, `StateMachineInstance` entities

**Reference implementation**:
- `../tic-tac-toe/src/TicTacToe.Engine/Engine.fs` -- `GameImpl` MailboxProcessor pattern

**Key constraints**:
- State instances keyed by string `instanceId` (derived from route parameters)
- MailboxProcessor serializes access per store -- no explicit locking needed
- Must handle concurrent requests to different instances efficiently
- `Subscribe` returns `IDisposable` -- disposal discipline required
- No external dependencies beyond ASP.NET Core built-ins

---

## Subtasks & Detailed Guidance

### Subtask T006 -- Implement `MailboxProcessorStore<'S,'C>`

**Purpose**: Create the default in-memory state store with MailboxProcessor-based serialized access.

**Steps**:
1. Open `src/Frank.Statecharts/Store.fs` (created in WP01)
2. Add the implementation below the `IStateMachineStore` interface
3. Define internal message types:

```fsharp
type private StoreMessage<'State, 'Context when 'State : equality> =
    | GetState of instanceId: string * replyChannel: AsyncReplyChannel<('State * 'Context) option>
    | SetState of instanceId: string * state: 'State * context: 'Context * replyChannel: AsyncReplyChannel<unit>
    | Subscribe of instanceId: string * observer: IObserver<'State * 'Context> * replyChannel: AsyncReplyChannel<IDisposable>
    | Unsubscribe of instanceId: string * observer: IObserver<'State * 'Context>
    | Stop of replyChannel: AsyncReplyChannel<unit>
```

4. Implement the MailboxProcessor loop:

```fsharp
type MailboxProcessorStore<'State, 'Context when 'State : equality>() =
    let mutable disposed = false

    let agent = MailboxProcessor<StoreMessage<'State, 'Context>>.Start(fun inbox ->
        let mutable instances = Map.empty<string, 'State * 'Context>
        let mutable subscribers = Map.empty<string, IObserver<'State * 'Context> list>

        let rec loop () = async {
            let! msg = inbox.Receive()
            match msg with
            | GetState(id, reply) ->
                reply.Reply(Map.tryFind id instances)
                return! loop()

            | SetState(id, state, ctx, reply) ->
                instances <- Map.add id (state, ctx) instances
                // Notify subscribers for this instance
                match Map.tryFind id subscribers with
                | Some observers ->
                    for obs in observers do
                        try obs.OnNext(state, ctx) with _ -> ()
                | None -> ()
                reply.Reply(())
                return! loop()

            | Subscribe(id, observer, reply) ->
                let current = Map.tryFind id subscribers |> Option.defaultValue []
                subscribers <- Map.add id (observer :: current) subscribers
                // BehaviorSubject: emit current state immediately
                match Map.tryFind id instances with
                | Some state -> try observer.OnNext(state) with _ -> ()
                | None -> ()
                let disposable = { new IDisposable with
                    member _.Dispose() = inbox.Post(Unsubscribe(id, observer)) }
                reply.Reply(disposable)
                return! loop()

            | Unsubscribe(id, observer) ->
                match Map.tryFind id subscribers with
                | Some observers ->
                    let filtered = observers |> List.filter (fun o -> not (obj.ReferenceEquals(o, observer)))
                    subscribers <- Map.add id filtered subscribers
                | None -> ()
                return! loop()

            | Stop reply ->
                // Notify all subscribers of completion
                for KeyValue(_, observers) in subscribers do
                    for obs in observers do
                        try obs.OnCompleted() with _ -> ()
                subscribers <- Map.empty
                instances <- Map.empty
                reply.Reply(())
                // Do NOT recurse -- agent stops
        }
        loop())

    // ... interface implementation follows
```

**Files**: `src/Frank.Statecharts/Store.fs`
**Notes**:
- The MailboxProcessor naturally serializes all access -- no locks needed
- Subscriber notification happens inline (synchronous) since it's just calling `OnNext`
- Error handling on observer notification: catch and ignore to prevent one bad subscriber from breaking the store
- `Stop` does NOT recurse, so the agent loop terminates after processing all pending messages

### Subtask T007 -- Implement `IDisposable` on store

**Purpose**: Ensure clean resource disposal following the constitution's disposal discipline.

**Steps**:
1. Implement `IDisposable` on `MailboxProcessorStore`:

```fsharp
    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                agent.PostAndReply(Stop)
```

2. Add an `ObjectDisposedException` check to all interface methods:

```fsharp
    let ensureNotDisposed () =
        if disposed then raise (ObjectDisposedException(nameof MailboxProcessorStore))
```

**Files**: `src/Frank.Statecharts/Store.fs`
**Notes**:
- `PostAndReply(Stop)` blocks until the agent has completed cleanup (drains pending operations)
- The `disposed` flag is a simple boolean -- the MailboxProcessor serializes actual state access
- After disposal, any calls to `GetState`/`SetState`/`Subscribe` throw `ObjectDisposedException`

### Subtask T008 -- Implement `IStateMachineStore` interface

**Purpose**: Wire the MailboxProcessor to the `IStateMachineStore` interface with proper async/Task conversion.

**Steps**:
1. Implement the interface on `MailboxProcessorStore`:

```fsharp
    interface IStateMachineStore<'State, 'Context> with
        member _.GetState(instanceId) =
            ensureNotDisposed()
            agent.PostAndAsyncReply(fun reply -> GetState(instanceId, reply))
            |> Async.StartAsTask

        member _.SetState instanceId state context =
            ensureNotDisposed()
            agent.PostAndAsyncReply(fun reply -> SetState(instanceId, state, context, reply))
            |> Async.StartAsTask

        member _.Subscribe instanceId observer =
            ensureNotDisposed()
            agent.PostAndAsyncReply(fun reply -> Subscribe(instanceId, observer, reply))
            |> Async.RunSynchronously  // Subscribe is typically called at setup time
```

**Files**: `src/Frank.Statecharts/Store.fs`
**Notes**:
- `GetState` and `SetState` return `Task` via `Async.StartAsTask` for ASP.NET Core compatibility
- `Subscribe` uses `Async.RunSynchronously` since it's called during resource setup, not in the hot path
- Consider whether `Subscribe` should also be async (returning `Task<IDisposable>`) -- the interface from WP01 defines it as synchronous, which is simpler for DI setup

2. Add DI registration helper as a module-level function or extension:

```fsharp
[<AutoOpen>]
module StoreServiceCollectionExtensions =
    open Microsoft.Extensions.DependencyInjection

    type IServiceCollection with
        member services.AddStateMachineStore<'State, 'Context when 'State : equality>() =
            services.AddSingleton<IStateMachineStore<'State, 'Context>>(fun _ ->
                new MailboxProcessorStore<'State, 'Context>() :> _)
```

### Subtask T009 -- Create `StoreTests.fs`

**Purpose**: Validate store behavior including concurrency, lifecycle, and observable semantics.

**Steps**:
1. Create `test/Frank.Statecharts.Tests/StoreTests.fs`
2. Add to `.fsproj` `<Compile>` list (before `Program.fs`)
3. Write tests covering:

**a. Basic CRUD**:
```fsharp
test "GetState returns None for unknown instance" {
    use store = new MailboxProcessorStore<string, unit>() :> IStateMachineStore<_,_>
    let! result = store.GetState("unknown") |> Async.AwaitTask
    Expect.isNone result "should be None"
}

test "SetState then GetState returns stored value" {
    use store = new MailboxProcessorStore<string, int>() :> IStateMachineStore<_,_>
    do! store.SetState "game1" "Playing" 42 |> Async.AwaitTask
    let! result = store.GetState("game1") |> Async.AwaitTask
    Expect.equal result (Some("Playing", 42)) "should return stored state"
}
```

**b. BehaviorSubject semantics**:
- Subscribe to instance that already has state -> observer receives current state immediately
- Subscribe to instance with no state -> observer receives nothing until SetState

**c. Observable notification**:
- Subscribe, then SetState -> observer receives new state
- Multiple subscribers -> all receive notification
- Unsubscribe (dispose) -> no longer receives notifications

**d. Concurrency**:
- Fire 100 concurrent SetState operations -> final state is consistent
- Fire GetState while SetState is in progress -> no exceptions

**e. Disposal lifecycle**:
- Dispose store -> subscribers receive OnCompleted
- Dispose store -> subsequent GetState throws ObjectDisposedException
- Double dispose -> no exception (idempotent)

**Files**: `test/Frank.Statecharts.Tests/StoreTests.fs`
**Parallel?**: Yes -- can be scaffolded once T006 interface is defined.
**Validation**: `dotnet test test/Frank.Statecharts.Tests/` passes.

---

## Test Strategy

- Run `dotnet test test/Frank.Statecharts.Tests/` with all store tests passing
- Concurrency tests should use `Async.Parallel` with 100+ operations
- Verify no deadlocks by setting a test timeout (Expecto supports this)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| MailboxProcessor disposal timing | `PostAndReply(Stop)` blocks until drain completes |
| Observer exceptions breaking store | Wrap all `OnNext`/`OnCompleted` calls in try/catch |
| Memory leaks from forgotten subscriptions | `IDisposable` pattern; document that callers must `use` |
| `Async.RunSynchronously` on Subscribe | Only used at setup time, not in hot path; consider making async if needed |

---

## Review Guidance

- Verify `Stop` message drains all pending operations before completing
- Verify BehaviorSubject semantics (new subscriber gets current state)
- Verify observer error isolation (one bad subscriber doesn't break others)
- Verify `ObjectDisposedException` after disposal
- Verify DI registration creates singleton instance
- Run concurrency tests to confirm no deadlocks or data races

---

## Activity Log

- 2026-03-06T00:00:00Z -- system -- lane=planned -- Prompt created.
