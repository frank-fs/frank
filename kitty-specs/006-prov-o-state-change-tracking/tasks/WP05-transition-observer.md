---
work_package_id: WP05
title: TransitionObserver
lane: done
dependencies:
- WP01
subtasks: [T022, T023, T024, T025, T026]
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-002, FR-003, FR-004, FR-005, FR-006, FR-013]
---

# Work Package Prompt: WP05 -- TransitionObserver

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP05 --base WP02
```

Depends on WP01 (core types) and WP02 (store for appending records).

---

## Objectives & Success Criteria

**PREREQUISITE: TransitionEvent must be extended with InstanceId, ResourceUri, HttpMethod fields before this WP can proceed.** The actual `TransitionEvent<'State, 'Event, 'Context>` in Frank.Statecharts currently has fields: PreviousState, PreviousContext, NewState, NewContext, Event, Timestamp, User. It does NOT have InstanceId, ResourceUri, or HttpMethod.

- Implement `TransitionObserver` that receives `TransitionEvent` from Frank.Statecharts `onTransition` hooks
- Extract agent identity from `ClaimsPrincipal` (Person, SoftwareAgent, LlmAgent classification)
- Construct complete `ProvenanceRecord` from transition event data
- Append record to `IProvenanceStore` via fire-and-forget
- Handle errors resiliently: `OnError`, `ObjectDisposedException` logged but not propagated
- Observer continues processing events after errors (does not unsubscribe)
- Guard-blocked transitions do NOT produce provenance records (FR-013)

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/006-prov-o-state-change-tracking/research.md` -- Decision 4 (onTransition hook integration pattern)
- `kitty-specs/006-prov-o-state-change-tracking/data-model.md` -- ProvenanceRecord construction, Agent extraction rules
- `kitty-specs/006-prov-o-state-change-tracking/spec.md` -- FR-001 through FR-006, FR-012, FR-013

**Key constraints**:
- Frank.Statecharts may not have `TransitionEvent` type yet. Define the expected shape as an internal type or interface in Frank.Provenance; adapt when Statecharts is available.
- `TransitionEvent` expected fields (from research.md Decision 4):
  - `InstanceId: string` -- resource instance key
  - `ResourceUri: string` -- route template
  - `PreviousState: obj` -- boxed state (string representation used)
  - `NewState: obj` -- boxed state (string representation used)
  - `Event: obj` -- boxed triggering event
  - `Timestamp: DateTimeOffset`
  - `User: ClaimsPrincipal option` -- from HttpContext
  - `HttpMethod: string`
- Agent classification rules:
  1. `Some principal` with authenticated identity -> `Person(name, identifier)` from claims
  2. `Some principal` with `X-Agent-Type: llm` hint -> `LlmAgent(identifier, model)`
  3. `Some principal` without identity (anonymous) -> `SoftwareAgent("system")`
  4. `None` -> `SoftwareAgent("system")`
- `IProvenanceStore.Append` is fire-and-forget (no await needed)
- ILogger for all observability
- GUID-based IDs for all provenance entities

---

## Subtasks & Detailed Guidance

### Subtask T022 -- Create `TransitionObserver.fs` with IObserver implementation

**Purpose**: Create the observer that is injected per-resource via the `onTransition` CE operation on `StatefulResourceBuilder`. Note: `onTransition` is per-resource, not a global observable. There is no single global stream to subscribe to. The `ProvenanceSubscriptionManager` (WP07) must iterate over all registered stateful resource endpoints and inject this observer into each resource's `onTransition` callback.

**Steps**:
1. Create `src/Frank.Provenance/TransitionObserver.fs`
2. Define the `TransitionEvent` type (or reference from Frank.Statecharts if available):

```fsharp
namespace Frank.Provenance

open System
open System.Security.Claims
open Microsoft.Extensions.Logging

/// Represents a successful state transition event from Frank.Statecharts.
/// If Frank.Statecharts defines this type, replace with a reference.
type TransitionEvent = {
    InstanceId: string
    ResourceUri: string
    PreviousState: string
    NewState: string
    Event: string
    Timestamp: DateTimeOffset
    User: ClaimsPrincipal option
    HttpMethod: string
    Headers: Map<string, string>
}
```

3. Implement the observer class:

```fsharp
/// Observes state transition events and creates provenance records.
type TransitionObserver(store: IProvenanceStore, logger: ILogger<TransitionObserver>) =

    let createRecord (event: TransitionEvent) : ProvenanceRecord =
        // Delegate to agent extraction (T023) and record construction (T024)
        ...

    interface IObserver<TransitionEvent> with
        member _.OnNext(event) =
            try
                let record = createRecord event
                store.Append(record)
                logger.LogDebug("Provenance record created for {ResourceUri} transition {PreviousState} -> {NewState}",
                                event.ResourceUri, event.PreviousState, event.NewState)
            with ex ->
                logger.LogError(ex, "Failed to create provenance record for {ResourceUri}", event.ResourceUri)

        member _.OnError(error) =
            logger.LogWarning(error, "Transition observable reported error")

        member _.OnCompleted() =
            logger.LogInformation("Transition observable completed")
```

4. Add `TransitionObserver.fs` to `.fsproj` after `MailboxProcessorStore.fs` (or `Store.fs`):
   ```xml
   <Compile Include="TransitionObserver.fs" />
   ```

**Files**: `src/Frank.Provenance/TransitionObserver.fs`
**Notes**:
- The observer is a class (not a module) because it holds DI dependencies (`IProvenanceStore`, `ILogger`)
- `OnNext` uses try/with to ensure one failed record does not break subsequent events
- `OnError` logs but does not unsubscribe -- the observable source decides if it terminates
- `OnCompleted` logs at Information level (lifecycle event)
- The `TransitionEvent.Headers` map enables agent type discrimination via `X-Agent-Type` header (WP06 will use this)

### Subtask T023 -- Implement agent extraction from ClaimsPrincipal

**Purpose**: Classify the agent responsible for a state change based on available identity metadata.

**Steps**:
1. Add agent extraction logic to `TransitionObserver`:

```fsharp
module private AgentExtraction =

    let private systemAgentId = "urn:frank:agent:system"
    let private systemAgent = { Id = systemAgentId; AgentType = SoftwareAgent "system" }

    /// Extract agent identity from ClaimsPrincipal.
    let extractAgent (user: ClaimsPrincipal option) (headers: Map<string, string>) : ProvenanceAgent =
        match user with
        | None -> systemAgent
        | Some principal ->
            if principal.Identity = null || not principal.Identity.IsAuthenticated then
                systemAgent
            else
                // Check for LLM agent type header
                match headers |> Map.tryFind "X-Agent-Type" with
                | Some "llm" ->
                    let identifier =
                        match principal.FindFirst(ClaimTypes.NameIdentifier) with
                        | null -> "unknown-llm"
                        | claim -> claim.Value
                    let model = headers |> Map.tryFind "X-Agent-Model"
                    { Id = sprintf "urn:frank:agent:llm:%s" identifier
                      AgentType = LlmAgent(identifier, model) }
                | _ ->
                    // Authenticated human user
                    let name =
                        match principal.FindFirst(ClaimTypes.Name) with
                        | null -> "Unknown"
                        | claim -> claim.Value
                    let identifier =
                        match principal.FindFirst(ClaimTypes.NameIdentifier) with
                        | null ->
                            match principal.Identity.Name with
                            | null -> Guid.NewGuid().ToString()
                            | n -> n
                        | claim -> claim.Value
                    { Id = sprintf "urn:frank:agent:person:%s" identifier
                      AgentType = Person(name, identifier) }
```

**Files**: `src/Frank.Provenance/TransitionObserver.fs`
**Notes**:
- System agent uses a well-known URN: `urn:frank:agent:system`
- Person agent URN format: `urn:frank:agent:person:{identifier}`
- LLM agent URN format: `urn:frank:agent:llm:{identifier}`
- `ClaimTypes.Name` and `ClaimTypes.NameIdentifier` are the standard ASP.NET Core identity claims
- Null checks on `FindFirst` results are essential -- not all auth providers set all claims
- `X-Agent-Type: llm` is checked before the default Person path. This means an authenticated request WITH the LLM header produces an LlmAgent (not Person).
- When no identity is available at all (null Identity or not authenticated), fall back to system agent

### Subtask T024 -- Implement ProvenanceRecord construction from TransitionEvent

**Purpose**: Assemble a complete `ProvenanceRecord` from a transition event.

**Steps**:
1. Implement `createRecord` in `TransitionObserver`:

```fsharp
let private createRecord (event: TransitionEvent) : ProvenanceRecord =
    let agent = AgentExtraction.extractAgent event.User event.Headers
    let recordId = Guid.NewGuid().ToString()
    let activityId = sprintf "urn:frank:activity:%s" (Guid.NewGuid().ToString())
    let usedEntityId = sprintf "urn:frank:entity:%s" (Guid.NewGuid().ToString())
    let generatedEntityId = sprintf "urn:frank:entity:%s" (Guid.NewGuid().ToString())
    let now = event.Timestamp

    let activity = {
        Id = activityId
        HttpMethod = event.HttpMethod
        ResourceUri = event.ResourceUri
        EventName = event.Event
        PreviousState = event.PreviousState
        NewState = event.NewState
        StartedAt = now
        EndedAt = now  // Same as start for synchronous transitions
    }

    let usedEntity = {
        Id = usedEntityId
        ResourceUri = event.ResourceUri
        StateName = event.PreviousState
        CapturedAt = now
    }

    let generatedEntity = {
        Id = generatedEntityId
        ResourceUri = event.ResourceUri
        StateName = event.NewState
        CapturedAt = now
    }

    { Id = recordId
      ResourceUri = event.ResourceUri
      Agent = agent
      Activity = activity
      UsedEntity = usedEntity
      GeneratedEntity = generatedEntity
      RecordedAt = now }
```

**Files**: `src/Frank.Provenance/TransitionObserver.fs`
**Notes**:
- All IDs are GUID-based URNs (unique per entity instance)
- `StartedAt` and `EndedAt` are both set to the transition timestamp for synchronous transitions. For async transitions (future enhancement), `EndedAt` would be set later.
- `ResourceUri` is the route path (e.g., `/orders/42`), matching what the middleware will query
- `EventName` comes from `TransitionEvent.Event` (the triggering event's string representation)
- `PreviousState` and `NewState` are string representations (not boxed objects) per data-model.md

### Subtask T025 -- Implement resilient error handling

**Purpose**: Ensure the observer handles all error conditions gracefully without disrupting the transition pipeline.

**Steps**:
1. Handle `ObjectDisposedException` in `OnNext`:

```fsharp
member _.OnNext(event) =
    try
        let record = createRecord event
        store.Append(record)
        logger.LogDebug("Provenance record created for {ResourceUri} transition {PreviousState} -> {NewState}",
                        event.ResourceUri, event.PreviousState, event.NewState)
    with
    | :? ObjectDisposedException ->
        logger.LogWarning("Provenance store disposed during transition for {ResourceUri}", event.ResourceUri)
    | ex ->
        logger.LogError(ex, "Failed to create provenance record for {ResourceUri}", event.ResourceUri)
```

2. Ensure `OnError` does not propagate:
```fsharp
member _.OnError(error) =
    logger.LogWarning(error, "Transition observable reported error, provenance observer continuing")
```

3. The observer must NOT:
   - Throw exceptions that would propagate to the transition pipeline
   - Unsubscribe itself on error (the observable source controls subscription lifecycle)
   - Silently drop errors without logging (Constitution VII)

**Files**: `src/Frank.Provenance/TransitionObserver.fs`
**Notes**:
- `ObjectDisposedException` is a specific edge case from the spec: store may be disposed while transitions are in-flight during shutdown
- All other exceptions are caught and logged at Error level
- The observer is designed to be non-blocking: `store.Append` is fire-and-forget, so the transition pipeline is not delayed

### Subtask T026 -- Create `TransitionObserverTests.fs`

**Purpose**: Test agent classification, record construction, and error resilience.

**Steps**:
1. Create `test/Frank.Provenance.Tests/TransitionObserverTests.fs`
2. Create a mock `IProvenanceStore` that captures appended records:

```fsharp
type MockProvenanceStore() =
    let records = ResizeArray<ProvenanceRecord>()
    member _.AppendedRecords = records |> Seq.toList
    interface IProvenanceStore with
        member _.Append(r) = records.Add(r)
        member _.QueryByResource(_) = Task.FromResult([])
        member _.QueryByAgent(_) = Task.FromResult([])
        member _.QueryByTimeRange(_, _) = Task.FromResult([])
    interface System.IDisposable with
        member _.Dispose() = ()
```

3. Write tests covering:

**a. Authenticated user produces Person agent**: Create `TransitionEvent` with authenticated `ClaimsPrincipal`. Verify appended record has `AgentType.Person` with correct name and identifier.

**b. Unauthenticated request produces SoftwareAgent**: Create event with `None` user. Verify `AgentType.SoftwareAgent("system")`.

**c. Anonymous principal produces SoftwareAgent**: Create event with `Some principal` where `IsAuthenticated = false`. Verify `SoftwareAgent`.

**d. LLM header produces LlmAgent**: Create event with authenticated principal and `X-Agent-Type: llm` header. Verify `AgentType.LlmAgent` with identifier from claims.

**e. LLM header with model**: Add `X-Agent-Model: claude-opus-4` header. Verify `model = Some "claude-opus-4"`.

**f. Record fields correctness**: Verify all fields of the constructed `ProvenanceRecord` match the input `TransitionEvent`.

**g. Error resilience**: Create observer with a store that throws on Append. Verify OnNext does not throw (catches and logs).

**h. Disposed store resilience**: Create observer with a disposed store. Trigger OnNext. Verify no exception propagates.

**i. Multiple events**: Send 3 events through OnNext. Verify 3 records appended to store.

4. Add `TransitionObserverTests.fs` to test `.fsproj`

**Files**: `test/Frank.Provenance.Tests/TransitionObserverTests.fs`
**Validation**: `dotnet test test/Frank.Provenance.Tests/` passes with all observer tests green.

---

## Test Strategy

- Run `dotnet build` to verify compilation on all targets
- Run `dotnet test test/Frank.Provenance.Tests/` -- all observer tests pass
- Verify agent classification covers all four cases (authenticated, unauthenticated, anonymous, LLM)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Frank.Statecharts API changes | `TransitionEvent` type and `onTransition` CE operation are implemented on master. Use the existing types directly. |
| `X-Agent-Type` header not in TransitionEvent | Include `Headers: Map<string, string>` field in TransitionEvent type |
| `ClaimTypes.Name` null for some auth providers | Fall back to `Identity.Name`, then to generated GUID |
| Observer exception disrupts transition pipeline | All exceptions caught in OnNext; logged but not propagated |

---

## Review Guidance

- Verify agent extraction handles all 4 classification cases correctly
- Verify `X-Agent-Type: llm` is checked before default Person path
- Verify system agent uses well-known URN `urn:frank:agent:system`
- Verify all entity IDs are GUID-based and unique per instance
- Verify `store.Append` is fire-and-forget (no await)
- Verify error handling catches `ObjectDisposedException` specifically
- Verify no exceptions propagate from OnNext
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
