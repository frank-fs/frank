# Data Model: Frank.Provenance

**Feature**: 006-prov-o-state-change-tracking
**Date**: 2026-03-07

## Entity Relationship Overview

```
ProvenanceRecord (aggregate root)
       │
       ├──1:1──> ProvenanceAgent ──> AgentType (DU)
       │              │
       │              │ typed as
       │              ├── Person (prov:Person)
       │              ├── SoftwareAgent (prov:SoftwareAgent)
       │              └── LlmAgent (prov:SoftwareAgent + frank:LlmAgent)
       │
       ├──1:1──> ProvenanceActivity
       │              │
       │              ├── prov:wasAssociatedWith ──> Agent
       │              ├── prov:used ──> Entity (pre-transition)
       │              └── prov:wasGeneratedBy ──<── Entity (post-transition)
       │
       ├──1:1──> ProvenanceEntity (pre-transition, prov:used)
       │
       └──1:1──> ProvenanceEntity (post-transition, prov:wasGeneratedBy)

IProvenanceStore
       │
       │ default impl
       v
MailboxProcessorProvenanceStore
       │
       │ contains
       v
StoreState { Records, ResourceIndex, AgentIndex }

TransitionObserver ──subscribes──> onTransition (IObservable<TransitionEvent>)
       │
       │ produces
       v
ProvenanceRecord ──appended to──> IProvenanceStore

GraphBuilder.toGraph : ProvenanceRecord list -> IGraph
       │
       │ serialized by
       v
Frank.LinkedData.writeRdf (Turtle, JSON-LD, RDF/XML)
```

## Core Entities

### ProvenanceRecord

The aggregate root representing a complete PROV-O assertion for a single successful state transition. One record per transition.

```fsharp
type ProvenanceRecord = {
    Id: string
    ResourceUri: string
    Agent: ProvenanceAgent
    Activity: ProvenanceActivity
    UsedEntity: ProvenanceEntity
    GeneratedEntity: ProvenanceEntity
    RecordedAt: DateTimeOffset
}
```

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Unique identifier (GUID-based URI fragment) |
| ResourceUri | string | The resource URI where the transition occurred |
| Agent | ProvenanceAgent | The actor responsible for the state change |
| Activity | ProvenanceActivity | The state transition activity |
| UsedEntity | ProvenanceEntity | Pre-transition state snapshot (`prov:used`) |
| GeneratedEntity | ProvenanceEntity | Post-transition state snapshot (`prov:wasGeneratedBy`) |
| RecordedAt | DateTimeOffset | When the provenance record was created |

**Identity**: `Id` is a GUID-based URI fragment, unique per record.
**Lifecycle**: Immutable once created. Subject to retention policy eviction in the default store.
**PROV-O Mapping**: Not a PROV-O class itself; it is the container for the complete assertion graph.

### ProvenanceAgent

The actor responsible for a state change. Maps to `prov:Agent` and its subclasses.

```fsharp
type AgentType =
    | Person of name: string * identifier: string
    | SoftwareAgent of identifier: string
    | LlmAgent of identifier: string * model: string option

type ProvenanceAgent = {
    Id: string
    AgentType: AgentType
}
```

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Agent URI (derived from identity claims or system ID) |
| AgentType | AgentType | Discriminated union classifying the agent |

**AgentType variants**:

| Variant | PROV-O Type | Source |
|---------|-------------|--------|
| `Person(name, identifier)` | `prov:Person` | `ClaimsPrincipal` with identity claims |
| `SoftwareAgent(identifier)` | `prov:SoftwareAgent` | Unauthenticated or system request |
| `LlmAgent(identifier, model)` | `prov:SoftwareAgent` + `frank:LlmAgent` annotation | `X-Agent-Type: llm` header |

**Key relationship**: Agent identity is extracted from `HttpContext.User` (`ClaimsPrincipal`). The `Person` variant captures `ClaimTypes.Name` and `ClaimTypes.NameIdentifier`. The `LlmAgent` variant optionally captures a model identifier from `X-Agent-Model` header.

### ProvenanceActivity

The state transition event. Maps to `prov:Activity`.

```fsharp
type ProvenanceActivity = {
    Id: string
    HttpMethod: string
    ResourceUri: string
    EventName: string
    PreviousState: string
    NewState: string
    StartedAt: DateTimeOffset
    EndedAt: DateTimeOffset
}
```

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Activity URI (GUID-based) |
| HttpMethod | string | HTTP method that triggered the transition (POST, PUT, DELETE, PATCH) |
| ResourceUri | string | Resource URI where the transition occurred |
| EventName | string | Name of the triggering event (from statechart event type) |
| PreviousState | string | String representation of the pre-transition state |
| NewState | string | String representation of the post-transition state |
| StartedAt | DateTimeOffset | `prov:startedAtTime` -- when the transition began |
| EndedAt | DateTimeOffset | `prov:endedAtTime` -- when the transition completed |

**PROV-O properties**:
- `prov:startedAtTime` -> `StartedAt`
- `prov:endedAtTime` -> `EndedAt`
- `prov:wasAssociatedWith` -> link to the `ProvenanceAgent`
- `prov:used` -> link to the pre-transition `ProvenanceEntity`

### ProvenanceEntity

A snapshot of resource state at a point in time. Maps to `prov:Entity`.

```fsharp
type ProvenanceEntity = {
    Id: string
    ResourceUri: string
    StateName: string
    CapturedAt: DateTimeOffset
}
```

| Field | Type | Description |
|-------|------|-------------|
| Id | string | Entity URI (GUID-based, distinct per snapshot) |
| ResourceUri | string | The resource this entity represents |
| StateName | string | The state name at capture time |
| CapturedAt | DateTimeOffset | When this snapshot was taken |

**PROV-O properties**:
- Pre-transition entity: referenced via `prov:used` from the Activity
- Post-transition entity: references Activity via `prov:wasGeneratedBy`
- Both entities reference the Agent via `prov:wasAttributedTo`
- Post-transition entity references pre-transition entity via `prov:wasDerivedFrom`

### ProvenanceGraph

A queryable collection of `ProvenanceRecord` instances for a given scope. Not persisted directly; constructed on demand from store queries.

```fsharp
type ProvenanceGraph = {
    ResourceUri: string
    Records: ProvenanceRecord list
}
```

| Field | Type | Description |
|-------|------|-------------|
| ResourceUri | string | The resource scope of this graph |
| Records | ProvenanceRecord list | Ordered list of provenance records (oldest first) |

**Lifecycle**: Created on demand by `IProvenanceStore` query methods. Serializable to RDF via `GraphBuilder.toGraph`.

### IProvenanceStore

Persistence abstraction for provenance records. Pluggable via DI.

```fsharp
type IProvenanceStore =
    inherit IDisposable
    abstract Append: ProvenanceRecord -> unit
    abstract QueryByResource: resourceUri: string -> Task<ProvenanceRecord list>
    abstract QueryByAgent: agentId: string -> Task<ProvenanceRecord list>
    abstract QueryByTimeRange: start: DateTimeOffset * end_: DateTimeOffset -> Task<ProvenanceRecord list>
```

| Method | Signature | Description |
|--------|-----------|-------------|
| Append | `ProvenanceRecord -> unit` | Fire-and-forget append (MailboxProcessor `Post`) |
| QueryByResource | `string -> Task<ProvenanceRecord list>` | All records for a resource URI |
| QueryByAgent | `string -> Task<ProvenanceRecord list>` | All records for an agent ID |
| QueryByTimeRange | `DateTimeOffset * DateTimeOffset -> Task<ProvenanceRecord list>` | Records within a time window |
| Dispose | `unit -> unit` | Drain pending appends, release resources |

**Design notes**:
- `Append` is synchronous (`unit` return) because the default store uses `MailboxProcessor.Post` (fire-and-forget). External stores may internally buffer and batch.
- Query methods return `Task` for async compatibility with external stores.
- Inherits `IDisposable` per Constitution VI.

### MailboxProcessorProvenanceStore

Default in-memory `IProvenanceStore` implementation.

```fsharp
type ProvenanceStoreConfig = {
    MaxRecords: int
    EvictionBatchSize: int
}

module ProvenanceStoreConfig =
    let defaults = { MaxRecords = 10_000; EvictionBatchSize = 100 }

type MailboxProcessorProvenanceStore(config: ProvenanceStoreConfig, logger: ILogger) =
    interface IProvenanceStore
    interface IDisposable
```

| Aspect | Detail |
|--------|--------|
| Concurrency | MailboxProcessor serializes writes; reads are `PostAndAsyncReply` (serialized but non-blocking) |
| Retention | Oldest-first eviction when `Records.Count > MaxRecords`, in batches of `EvictionBatchSize` |
| Indexes | `Dictionary<string, ResizeArray<int>>` for resource URI and agent ID |
| Disposal | Processes remaining queue, clears state, logs disposal |

**Internal state** (not exposed):

```fsharp
type private StoreState = {
    Records: ResizeArray<ProvenanceRecord>
    ResourceIndex: Dictionary<string, ResizeArray<int>>
    AgentIndex: Dictionary<string, ResizeArray<int>>
}
```

**Internal message type**:

```fsharp
type private StoreMessage =
    | Append of ProvenanceRecord
    | QueryByResource of string * AsyncReplyChannel<ProvenanceRecord list>
    | QueryByAgent of string * AsyncReplyChannel<ProvenanceRecord list>
    | QueryByTimeRange of DateTimeOffset * DateTimeOffset * AsyncReplyChannel<ProvenanceRecord list>
    | Dispose of AsyncReplyChannel<unit>
```

## Relationships

1. **ProvenanceRecord -> ProvenanceAgent**: Each record has exactly one agent (1:1). The agent may appear in multiple records across different transitions.

2. **ProvenanceRecord -> ProvenanceActivity**: Each record has exactly one activity (1:1). Activities are unique per transition -- no activity is shared across records.

3. **ProvenanceRecord -> ProvenanceEntity**: Each record has exactly two entities (1:2) -- one pre-transition (`UsedEntity`, referenced via `prov:used`) and one post-transition (`GeneratedEntity`, referenced via `prov:wasGeneratedBy`). Entities are unique per record.

4. **ProvenanceActivity -> ProvenanceAgent**: `prov:wasAssociatedWith` -- the activity was performed by this agent.

5. **ProvenanceActivity -> ProvenanceEntity (pre)**: `prov:used` -- the activity consumed the pre-transition entity.

6. **ProvenanceEntity (post) -> ProvenanceActivity**: `prov:wasGeneratedBy` -- the post-transition entity was generated by this activity.

7. **ProvenanceEntity (post) -> ProvenanceEntity (pre)**: `prov:wasDerivedFrom` -- the post-transition entity derives from the pre-transition entity.

8. **ProvenanceEntity -> ProvenanceAgent**: `prov:wasAttributedTo` -- both entities are attributed to the agent.

9. **TransitionObserver -> IProvenanceStore**: The observer appends records to the store after each successful transition.

10. **IProvenanceStore -> MailboxProcessorProvenanceStore**: Default implementation relationship. Custom stores implement the same interface.

## PROV-O Triple Pattern

For a single `ProvenanceRecord`, the generated RDF graph contains these triples:

```turtle
@prefix prov: <http://www.w3.org/ns/prov#> .
@prefix frank: <https://frank-web.dev/ns/provenance/> .
@prefix xsd: <http://www.w3.org/2001/XMLSchema#> .

# Activity
<activity/{id}>  a  prov:Activity ;
    prov:startedAtTime  "{startedAt}"^^xsd:dateTime ;
    prov:endedAtTime    "{endedAt}"^^xsd:dateTime ;
    prov:wasAssociatedWith  <agent/{agentId}> ;
    prov:used            <entity/{usedId}> ;
    frank:httpMethod     "{method}" ;
    frank:eventName      "{eventName}" .

# Agent
<agent/{agentId}>  a  prov:Person ;       # or prov:SoftwareAgent
    prov:label  "{name}" .

# Pre-transition Entity
<entity/{usedId}>  a  prov:Entity ;
    prov:wasAttributedTo  <agent/{agentId}> ;
    frank:stateName       "{previousState}" .

# Post-transition Entity
<entity/{generatedId}>  a  prov:Entity ;
    prov:wasGeneratedBy   <activity/{id}> ;
    prov:wasAttributedTo  <agent/{agentId}> ;
    prov:wasDerivedFrom   <entity/{usedId}> ;
    frank:stateName       "{newState}" .
```
