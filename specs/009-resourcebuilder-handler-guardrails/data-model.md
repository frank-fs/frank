# Data Model: ResourceBuilder Handler Guardrails

**Date**: 2026-02-03
**Feature**: 009-resourcebuilder-handler-guardrails

## Overview

This feature is a static analysis tool with no persistent data storage. The "data model" describes the in-memory structures used during AST analysis.

---

## Core Types

### HttpMethod

Represents the HTTP methods that can be registered in a Frank resource.

```fsharp
type HttpMethod =
    | GET
    | POST
    | PUT
    | DELETE
    | PATCH
    | HEAD
    | OPTIONS
    | CONNECT
    | TRACE
```

### OperationRegistration

Tracks a single HTTP method handler registration within a resource block.

```fsharp
type OperationRegistration = {
    Method: HttpMethod
    Range: FSharp.Compiler.Text.Range
    OperationName: string  // "get", "post", "datastar", etc.
}
```

### ResourceContext

Tracks the state while analyzing a single resource computation expression.

```fsharp
type ResourceContext = {
    /// Map from HTTP method to first registration location
    RegisteredMethods: Map<HttpMethod, OperationRegistration>
    /// Range of the resource CE for context in diagnostics
    ResourceRange: FSharp.Compiler.Text.Range
}

module ResourceContext =
    let empty range = {
        RegisteredMethods = Map.empty
        ResourceRange = range
    }

    let tryRegister (op: OperationRegistration) (ctx: ResourceContext) =
        match ctx.RegisteredMethods.TryFind op.Method with
        | Some existing -> Error (existing, op)  // Duplicate found
        | None -> Ok { ctx with RegisteredMethods = ctx.RegisteredMethods.Add(op.Method, op) }
```

### Diagnostic

The output produced when a duplicate is detected.

```fsharp
type Diagnostic = {
    Type: string           // "Duplicate HTTP handler"
    Message: string        // Human-readable message
    Code: string          // "FRANK001"
    Severity: Severity    // Warning
    Range: Range          // Location of the duplicate (second occurrence)
    Fixes: CodeFix list   // Empty for MVP
}

type Severity =
    | Hint
    | Info
    | Warning
    | Error
```

---

## Analysis Flow

```
┌─────────────────┐
│   Source File   │
│    (*.fs)       │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Parse to AST   │
│ (FSharp.Compiler│
│    .Service)    │
└────────┬────────┘
         │
         ▼
┌─────────────────┐
│  Walk AST with  │
│ DuplicateHandler│
│    Walker       │
└────────┬────────┘
         │
         ▼
┌─────────────────────────────────────────────┐
│ For each SynExpr.ComputationExpr:           │
│   1. Create ResourceContext                 │
│   2. Walk body expressions                  │
│   3. For each HTTP method operation:        │
│      - Try to register in context           │
│      - If duplicate: emit Diagnostic        │
│   4. Pop context when exiting CE            │
└────────┬────────────────────────────────────┘
         │
         ▼
┌─────────────────┐
│ Diagnostic list │
│    returned     │
└─────────────────┘
```

---

## State Transitions

### ResourceContext State Machine

```
                 ┌──────────────────┐
                 │      Empty       │
                 │ (no registrations│
                 └────────┬─────────┘
                          │
          Register HTTP method operation
                          │
                          ▼
                 ┌──────────────────┐
                 │   Tracking       │◄────────┐
                 │ (1+ methods      │         │
                 │  registered)     │─────────┘
                 └────────┬─────────┘  Register different
                          │            HTTP method
          Register duplicate method
                          │
                          ▼
                 ┌──────────────────┐
                 │ Emit Diagnostic  │
                 │ (continue        │
                 │  tracking)       │
                 └──────────────────┘
```

---

## HTTP Method Operation Mapping

Maps AST identifiers to HTTP methods for detection:

| AST Identifier | HTTP Method | Source |
|----------------|-------------|--------|
| `get` | GET | ResourceBuilder |
| `post` | POST | ResourceBuilder |
| `put` | PUT | ResourceBuilder |
| `delete` | DELETE | ResourceBuilder |
| `patch` | PATCH | ResourceBuilder |
| `head` | HEAD | ResourceBuilder |
| `options` | OPTIONS | ResourceBuilder |
| `connect` | CONNECT | ResourceBuilder |
| `trace` | TRACE | ResourceBuilder |
| `datastar` | * | Frank.Datastar extension |

**Note**: `datastar` operation requires argument inspection to determine the HTTP method:
- No method argument → GET (default)
- `HttpMethods.Get` → GET
- `HttpMethods.Post` → POST
- etc.

---

## Validation Rules

### VR-001: Single Handler Per Method
Within a single `resource` computation expression, each HTTP method may only be registered once.

**Valid:**
```fsharp
resource "/users" {
    get handler1
    post handler2
}
```

**Invalid:**
```fsharp
resource "/users" {
    get handler1
    get handler2  // FRANK001: Duplicate GET handler
}
```

### VR-002: Cross-Operation Detection
The `datastar` operation counts as registering an HTTP method and conflicts with explicit method handlers.

**Invalid:**
```fsharp
resource "/events" {
    datastar sseHandler      // Registers GET
    get otherHandler         // FRANK001: Duplicate GET handler
}
```

### VR-003: Independent Resource Blocks
Each `resource` block is analyzed independently. The same method can appear in different resources.

**Valid:**
```fsharp
resource "/users" {
    get handler1
}

resource "/posts" {
    get handler2  // OK - different resource
}
```

---

## Relationships

```
┌─────────────────┐         ┌─────────────────┐
│   SourceFile    │ 1    *  │    Resource     │
│                 │────────▶│    Context      │
│ (parsed AST)    │         │                 │
└─────────────────┘         └────────┬────────┘
                                     │
                                     │ 1    *
                                     ▼
                            ┌─────────────────┐
                            │   Operation     │
                            │  Registration   │
                            └────────┬────────┘
                                     │
                                     │ 0..1  (on duplicate)
                                     ▼
                            ┌─────────────────┐
                            │   Diagnostic    │
                            │                 │
                            └─────────────────┘
```

---

## No Persistent Storage

This feature has no database, file storage, or state persistence requirements. All analysis happens in-memory during a single file analysis pass.
