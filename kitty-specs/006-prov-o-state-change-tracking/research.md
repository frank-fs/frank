# Research: Frank.Provenance (PROV-O State Change Tracking)

**Feature**: 006-prov-o-state-change-tracking
**Date**: 2026-03-07

## Decision 1: PROV-O Vocabulary Mapping to F# Types

### Context

W3C PROV-O defines three core classes (`prov:Entity`, `prov:Activity`, `prov:Agent`) and relationships between them (`prov:wasGeneratedBy`, `prov:used`, `prov:wasAssociatedWith`, `prov:wasAttributedTo`, `prov:wasDerivedFrom`). Frank.Provenance must map these to idiomatic F# types.

### Decision

Map PROV-O classes to F# immutable records, not to dotNetRdf `INode` types directly. The F# records are the internal domain model; graph construction is a separate projection step (GraphBuilder).

| PROV-O Class | F# Type | Rationale |
|-------------|---------|-----------|
| `prov:Entity` | `ProvenanceEntity` record | Snapshot of resource state at a point in time |
| `prov:Activity` | `ProvenanceActivity` record | The state transition event |
| `prov:Agent` | `ProvenanceAgent` record | Actor responsible (human, system, LLM) |
| `prov:Person` | `AgentType.Person` DU case | Authenticated human user |
| `prov:SoftwareAgent` | `AgentType.SoftwareAgent` DU case | System/unauthenticated |
| (LLM subclass) | `AgentType.LlmAgent` DU case | LLM-originated changes |

The `ProvenanceRecord` type is the aggregate root -- it contains references to one Agent, one Activity, and two Entities (pre-transition `prov:used`, post-transition `prov:wasGeneratedBy`). This is not a PROV-O class itself; it is a convenience wrapper for the complete provenance assertion produced by a single state transition.

### Rejected Alternative

Representing provenance directly as dotNetRdf triples without an intermediate F# domain model. Rejected because: (1) querying triples by resource/agent/time is expensive without indexes, (2) the F# types provide compile-time safety for record construction, (3) graph construction is a serialization concern, not a domain concern.

## Decision 2: dotNetRdf.Core Graph Construction API

### Context

Frank.LinkedData already depends on dotNetRdf (VDS.RDF namespace). Frank.Provenance needs to construct PROV-O RDF graphs from `ProvenanceRecord` instances for content-negotiated responses.

### Decision

Use dotNetRdf's `Graph` class with explicit triple assertion, matching the pattern in `Frank.LinkedData.WebHostBuilderExtensions.projectJsonToRdf`. The `GraphBuilder` module provides a pure function `toGraph : ProvenanceRecord list -> IGraph` that constructs a complete PROV-O graph.

Key API patterns:
```fsharp
let graph = new Graph()
let subject = graph.CreateUriNode(UriFactory.Root.Create(uri))
let predicate = graph.CreateUriNode(UriFactory.Root.Create(ProvVocabulary.wasGeneratedBy))
let object_ = graph.CreateUriNode(UriFactory.Root.Create(entityUri))
graph.Assert(Triple(subject, predicate, object_)) |> ignore
```

For typed literals (timestamps):
```fsharp
let xsdDateTime = UriFactory.Root.Create("http://www.w3.org/2001/XMLSchema#dateTime")
let literal = graph.CreateLiteralNode(timestamp.ToString("o"), xsdDateTime)
```

Namespace prefixes are registered on the graph for readable Turtle output:
```fsharp
graph.NamespaceMap.AddNamespace("prov", UriFactory.Root.Create("http://www.w3.org/ns/prov#"))
graph.NamespaceMap.AddNamespace("frank", UriFactory.Root.Create("https://frank-web.dev/ns/provenance/"))
```

### Serialization

Graph serialization reuses Frank.LinkedData's `writeRdf` function for Turtle, JSON-LD, and RDF/XML. No new serializers needed.

## Decision 3: MailboxProcessor Store Design

### Context

The default `IProvenanceStore` must handle concurrent writes from multiple request threads while providing consistent ordering. F#'s `MailboxProcessor` is the idiomatic choice for serialized message processing.

### Decision

#### Message Types

```fsharp
type StoreMessage =
    | Append of ProvenanceRecord
    | QueryByResource of resourceUri: string * AsyncReplyChannel<ProvenanceRecord list>
    | QueryByAgent of agentId: string * AsyncReplyChannel<ProvenanceRecord list>
    | QueryByTimeRange of start: DateTimeOffset * end_: DateTimeOffset * AsyncReplyChannel<ProvenanceRecord list>
    | Dispose of AsyncReplyChannel<unit>
```

`Append` uses `Post` (fire-and-forget) for minimal overhead on the request path. All `Query*` messages use `PostAndAsyncReply` because they need to return results.

#### State Shape

```fsharp
type StoreState = {
    Records: ResizeArray<ProvenanceRecord>
    ResourceIndex: Dictionary<string, ResizeArray<int>>   // resourceUri -> record indices
    AgentIndex: Dictionary<string, ResizeArray<int>>      // agentId -> record indices
    MaxRecords: int
}
```

The `ResizeArray` is used instead of immutable lists for performance (spec requires sub-millisecond operations at 10,000 records). Indexes by resource URI and agent ID enable O(k) queries where k is the result count, rather than O(n) scans.

#### Retention Policy

When `Records.Count` exceeds `MaxRecords`:
1. Calculate eviction count: `Records.Count - MaxRecords + evictionBatch` (default batch: 100, to amortize eviction cost)
2. Remove oldest records from `Records`
3. Rebuild indexes (simple approach; index-aware eviction is a premature optimization for 10K records)

The eviction batch size prevents per-append eviction overhead. Rebuilding indexes on eviction is O(n) but happens infrequently (every 100 appends after capacity is reached).

#### Disposal

`Dispose` sends a message to the MailboxProcessor, which:
1. Processes any remaining `Append` messages in the queue
2. Clears all state
3. Sets a disposed flag
4. Subsequent messages after disposal are no-ops (logged, not thrown)

### Rejected Alternative

`ConcurrentDictionary` with `ReaderWriterLockSlim` for concurrent access. Rejected because: (1) MailboxProcessor is idiomatic F# and simpler to reason about, (2) Frank.Statecharts already establishes the MailboxProcessor pattern for its store, (3) lock-based solutions require careful ordering discipline.

## Decision 4: onTransition Hook Integration Pattern

### Context

Frank.Statecharts exposes `onTransition` as an `IObservable<TransitionEvent>` on the state machine store. Frank.Provenance must subscribe to these events and produce `ProvenanceRecord` instances.

### Decision

The `TransitionObserver` module implements `IObserver<TransitionEvent>` and is registered during `useProvenance` setup. The observer:

1. Receives `TransitionEvent` containing:
   - `InstanceId: string` (resource instance key)
   - `ResourceUri: string` (route template)
   - `PreviousState: obj` (boxed 'State)
   - `NewState: obj` (boxed 'State)
   - `Event: obj` (boxed 'Event triggering transition)
   - `Timestamp: DateTimeOffset`
   - `User: ClaimsPrincipal option` (from HttpContext)
   - `HttpMethod: string`

2. Constructs `ProvenanceAgent` from `User`:
   - `Some principal` with identity claims -> `AgentType.Person` with name/identifier from claims
   - `Some principal` without identity (anonymous) -> `AgentType.SoftwareAgent` with system ID
   - Header `X-Agent-Type: llm` present -> `AgentType.LlmAgent` with identifier from claims or header
   - `None` -> `AgentType.SoftwareAgent` with system ID

3. Constructs `ProvenanceActivity` with method, URI, timestamps, event name

4. Constructs pre-transition `ProvenanceEntity` (`prov:used`) and post-transition `ProvenanceEntity` (`prov:wasGeneratedBy`)

5. Assembles `ProvenanceRecord` and calls `IProvenanceStore.Append`

The observer is resilient: `OnError` and store `ObjectDisposedException` are logged but do not propagate (the observer continues processing subsequent events).

### Subscription Lifecycle

```
useProvenance setup:
  1. Register IProvenanceStore in DI (singleton)
  2. Register TransitionObserver in DI (singleton, depends on IProvenanceStore + ILogger)
  3. In middleware setup phase, enumerate all StateMachineMetadata endpoints
  4. Subscribe TransitionObserver to each store's onTransition observable
  5. Store IDisposable subscriptions for cleanup on host shutdown
```

The subscriptions are stored in a `ProvenanceSubscriptionManager` that implements `IHostedService` for proper lifecycle management (start subscribing after endpoints are built, dispose on shutdown).

## Decision 5: Custom Media Type Registration with Frank.LinkedData

### Context

Provenance is served via content negotiation on the resource URI. Requests with `Accept: application/vnd.frank.provenance+turtle` get provenance; requests with `Accept: application/json` get the normal resource representation.

### Decision

The provenance middleware runs **before** the standard handler and **before** the LinkedData middleware. It intercepts GET requests where the `Accept` header matches `application/vnd.frank.provenance+*`:

```
Supported media types:
  application/vnd.frank.provenance+json       -> JSON-LD serialization
  application/vnd.frank.provenance+ld+json    -> JSON-LD serialization (alias)
  application/vnd.frank.provenance+turtle     -> Turtle serialization
  application/vnd.frank.provenance+rdf+xml    -> RDF/XML serialization
```

When a provenance media type is matched:
1. Extract resource URI from request path
2. Query `IProvenanceStore.QueryByResource(resourceUri)`
3. Build RDF graph via `GraphBuilder.toGraph`
4. Map `vnd.frank.provenance+turtle` -> `text/turtle` for serialization
5. Serialize graph using Frank.LinkedData's `writeRdf`
6. Return response with the original `vnd.frank.provenance+*` content type

When no provenance media type is matched, the middleware passes through to the next handler (zero overhead -- just a string prefix check on the Accept header).

### Rejected Alternative

Separate `/provenance` sub-route per resource. Rejected because: (1) violates Constitution I (resource-oriented design -- provenance is a representation of the resource, not a separate resource), (2) spec explicitly requires content negotiation on the resource URI, (3) separate routes would require route registration logic that duplicates the resource's route.

## Decision 6: Retention Policy Implementation

### Context

The in-memory store must prevent unbounded memory growth. The spec requires configurable max record count (default 10,000) with oldest-first eviction.

### Decision

#### Configuration

```fsharp
type ProvenanceStoreConfig = {
    MaxRecords: int           // Default: 10_000
    EvictionBatchSize: int    // Default: 100
}
```

Exposed via `useProvenance` custom operation:
```fsharp
useProvenance                                     // defaults
useProvenance { maxRecords 50_000 }               // custom limit
```

The `useProvenance` operation accepts an optional `ProvenanceStoreConfig` value. If none is provided, defaults are used.

#### Eviction Strategy

Oldest-first (FIFO) eviction. When a batch eviction is triggered:

1. Remove the oldest `EvictionBatchSize` records from the `Records` list
2. Rebuild `ResourceIndex` and `AgentIndex` from remaining records
3. Log eviction event at `Information` level with record count

Batch eviction (removing 100 at a time rather than 1 per append) amortizes the O(n) index rebuild cost. At 10,000 records with batch size 100, eviction happens once every 100 appends after capacity, costing ~0.1ms per eviction (one pass over records to rebuild two dictionaries).

#### Time-Based Retention (Deferred)

Time-window-based retention (e.g., "keep records from last 24 hours") is noted in the spec as an option but not required for this feature. The interface supports it via `QueryByTimeRange`, and a future enhancement could add `MaxAge: TimeSpan option` to `ProvenanceStoreConfig` with a periodic cleanup timer.
