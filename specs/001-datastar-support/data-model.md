# Data Model: Datastar SSE Streaming Support

**Date**: 2025-01-25
**Feature**: 001-datastar-support

## Entities

### 1. Resource (existing - from Frank.Builder)

```fsharp
[<Struct>]
type Resource = { Endpoints: Endpoint[] }
```

- **Purpose**: Represents a Frank HTTP resource with its endpoints
- **Relationships**: Contains multiple ASP.NET Core `Endpoint` objects
- **Validation**: Endpoints array must not be null

### 2. ResourceSpec (existing - from Frank.Builder)

```fsharp
type ResourceSpec = {
    Name: string
    Handlers: (string * RequestDelegate) list
}
```

- **Purpose**: Accumulates handlers during computation expression building
- **Fields**:
  - `Name`: Optional display name for the resource
  - `Handlers`: List of (HTTP method, handler) tuples
- **State Transitions**:
  - Empty → With Name → With Handlers → Built (via `Build()`)

### 3. StreamingHandler (conceptual)

```fsharp
type StreamingHandler = HttpContext -> Task<unit>
```

- **Purpose**: Function signature for Datastar streaming operations
- **Relationships**: Receives HttpContext, performs SSE operations
- **Constraints**:
  - Should call SSE generator methods on `ctx.Response`
  - Should check `ctx.RequestAborted` for cancellation

### 4. Signals (user-defined)

```fsharp
// Example user type
[<CLIMutable>]
type CounterSignals = {
    count: int
}
```

- **Purpose**: Client-side state sent via request body
- **Validation**:
  - Must be JSON-deserializable
  - Invalid JSON returns `ValueNone`
- **Constraints**:
  - Should use `CLIMutable` for JSON deserialization compatibility
  - Should be minimal (ephemeral UI state only)

## Type Relationships

```
┌─────────────────────────────────────────────────────────────────┐
│                       Frank.Builder                             │
├─────────────────────────────────────────────────────────────────┤
│  ResourceBuilder ──builds──> ResourceSpec ──builds──> Resource  │
│       │                                                         │
│       │ extended by                                             │
│       ▼                                                         │
├─────────────────────────────────────────────────────────────────┤
│                    Frank.Datastar                               │
├─────────────────────────────────────────────────────────────────┤
│  DatastarExtensions (extends ResourceBuilder with):             │
│    • datastar: StreamingHandler -> ResourceSpec                 │
│    • patchElements: string|function -> ResourceSpec             │
│    • removeElement: string -> ResourceSpec                      │
│    • patchSignals: string|function -> ResourceSpec              │
│    • executeScript: string -> ResourceSpec                      │
│    • readSignals: (signals -> Task) -> ResourceSpec             │
│    • transformSignals: (input -> Task<output>) -> ResourceSpec  │
│                                                                 │
│  Datastar module (standalone functions):                        │
│    • stream: operations list -> HttpContext -> Task             │
│    • patchElements: html -> HttpContext -> Task                 │
│    • patchSignals: json -> HttpContext -> Task                  │
│    • removeElement: selector -> HttpContext -> Task             │
│    • executeScript: script -> HttpContext -> Task               │
│    • tryReadSignals<'T>: HttpContext -> Task<voption<'T>>       │
├─────────────────────────────────────────────────────────────────┤
│                StarFederation.Datastar.FSharp                   │
├─────────────────────────────────────────────────────────────────┤
│  ServerSentEventGenerator (static methods):                     │
│    • StartServerEventStreamAsync: Response -> Task              │
│    • PatchElementsAsync: Response -> html -> Task               │
│    • PatchSignalsAsync: Response -> json -> Task                │
│    • RemoveElementAsync: Response -> selector -> Task           │
│    • ExecuteScriptAsync: Response -> script -> Task             │
│    • ReadSignalsAsync<'T>: Request -> Task<voption<'T>>         │
│    • TryReadSignals<'T>: Request -> voption<'T>                 │
└─────────────────────────────────────────────────────────────────┘
```

## SSE Event Types

Datastar uses specific SSE event types:

| Event Type | Purpose | Data Format |
|------------|---------|-------------|
| `datastar-patch-elements` | Update DOM with HTML | HTML fragment with id selector |
| `datastar-patch-signals` | Update client signals | JSON object |
| `datastar-remove-elements` | Remove DOM elements | CSS selector |
| `datastar-execute-script` | Run JavaScript | JS code |

## Validation Rules

### Signal Deserialization

```
Input: JSON string from request body
Output: voption<'T>

Rules:
1. Empty body → ValueNone
2. Malformed JSON → ValueNone
3. Valid JSON, wrong shape → ValueNone
4. Valid JSON, correct shape → ValueSome value
```

### Stream Lifecycle

```
States: NotStarted → Started → Completed

Transitions:
- NotStarted → Started: StartServerEventStreamAsync called
- Started → Started: Additional events sent
- Started → Completed: Handler completes or client disconnects

Invariants:
- StartServerEventStreamAsync must be called exactly once per request
- Events can only be sent after stream is started
- Client disconnect should stop event generation
```

## Data Flow

### Request Processing

```
1. Client sends request with optional JSON body (signals)
2. Frank routes to Datastar-enabled resource
3. Handler executes:
   a. Stream started automatically
   b. Optionally read signals from body
   c. Send 0-n events (patches, removals, scripts)
   d. Handler completes
4. Connection closes
```

### Signal Reading

```
Client                    Server
  │                         │
  │──POST with JSON body───▶│
  │                         │
  │                    ReadSignalsAsync<'T>
  │                         │
  │                    if valid JSON:
  │                      ValueSome signals
  │                    else:
  │                      ValueNone
  │                         │
  │◀──SSE events───────────│
```

## Constraints from Constitution

Per Frank Constitution principles:

1. **Resource-Oriented**: Datastar operations are custom operations on `resource` builder
2. **Idiomatic F#**:
   - voption for optional signal reading (not nullable)
   - Task-based async (interop-friendly)
   - Curried functions in standalone module
3. **Library, Not Framework**:
   - No view engine dependency
   - Works with any HTML generation approach
4. **ASP.NET Core Native**:
   - HttpContext exposed directly
   - Uses ASP.NET Core Response/Request objects
5. **Performance Parity**:
   - Thin wrapper over StarFederation.Datastar.FSharp
   - No unnecessary allocations
