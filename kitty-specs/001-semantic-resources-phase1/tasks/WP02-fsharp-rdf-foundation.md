---
work_package_id: WP02
title: F# RDF Foundation
lane: "doing"
dependencies: [WP01]
base_branch: 001-semantic-resources-phase1-WP01
base_commit: e940579b385af16c14a3af584d68d03952856347
created_at: '2026-03-05T15:23:55.981309+00:00'
subtasks:
- T008
- T009
- T010
- T011
- T012
phase: Phase 0 - Foundation
assignee: ''
agent: ''
shell_pid: "80069"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-04T22:10:13Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-022]
---

# WP02: F# RDF Foundation

## Implementation Command

```
spec-kitty implement WP02 --base WP01
```

## Objectives

Create F# wrappers for dotNetRdf, extraction state model, diff engine, and vocabulary constants. These form the foundational data layer that all higher-level mapping and analysis modules depend on.

## Context

- dotNetRdf is a C#-oriented library with a mutable API and null-returning methods. The F# wrappers must convert these into idiomatic F# patterns: `option` returns instead of nulls, pipeline-friendly function signatures, immutable-feeling DU representations.
- `ExtractionState` persists to `obj/frank-cli/` as JSON. Ontology and shapes graphs within the state are serialized as Turtle strings embedded in the JSON document.
- Vocabulary constants cover the standard RDF vocabularies used throughout the project: Schema.org (`https://schema.org/`), Hydra (`http://www.w3.org/ns/hydra/core#`), OWL (`http://www.w3.org/2002/07/owl#`), SHACL (`http://www.w3.org/ns/shacl#`), RDF, and RDFS.
- Reference `data-model.md` for the exact `ExtractionState` field definitions and JSON schema.

## Subtask Details

### T008: FSharpRdf.fs — dotNetRdf F# Wrappers

**Module**: `Frank.Cli.Core.Rdf.FSharpRdf`

**File location**: `src/Frank.Cli.Core/Rdf/FSharpRdf.fs`

Define a discriminated union to represent RDF nodes without exposing dotNetRdf interfaces:

```fsharp
type RdfNode =
    | UriNode of Uri
    | LiteralNode of value: string * datatype: Uri option
    | BlankNode of id: string
```

Implement these functions:

- `createGraph : unit -> IGraph` — creates a new empty dotNetRdf `Graph` instance
- `createUriNode : IGraph -> Uri -> INode` — creates a URI node on the given graph
- `createLiteralNode : IGraph -> string -> Uri option -> INode` — creates a typed or plain literal
- `createBlankNode : IGraph -> string -> INode` — creates a blank node with given id
- `assertTriple : IGraph -> INode * INode * INode -> unit` — asserts a subject/predicate/object triple (pipeline-friendly: `graph |> assertTriple (s, p, o)`)
- `getNode : IGraph -> Uri -> INode option` — looks up a URI node, returns `None` if absent (not null)
- `triplesWithSubject : IGraph -> INode -> Triple seq` — returns all triples where given node is subject
- `triplesWithPredicate : IGraph -> INode -> Triple seq` — returns all triples with given predicate

Conversion functions between F# DU and dotNetRdf `INode`:
- `toRdfNode : INode -> RdfNode` — converts dotNetRdf INode to F# DU (handle null → BlankNode or raise)
- `fromRdfNode : IGraph -> RdfNode -> INode` — converts F# DU to dotNetRdf INode

All functions returning dotNetRdf mutable objects should be treated as opaque handles; the DU is the idiomatic representation for pattern matching.

### T009: ExtractionState.fs — State Persistence Model

**Module**: `Frank.Cli.Core.State.ExtractionState`

**File location**: `src/Frank.Cli.Core/State/ExtractionState.fs`

Define these types (reference `data-model.md` for authoritative field list):

```fsharp
type SourceLocation = {
    File: string
    Line: int
    Column: int
}

type ExtractionMetadata = {
    Timestamp: DateTimeOffset
    SourceHash: string       // SHA-256 hex of analyzed source files
    ToolVersion: string      // frank-cli version
    BaseUri: Uri
    Vocabularies: string list // e.g. ["schema.org"; "hydra"]
}

type UnmappedType = {
    TypeName: string
    Reason: string
    Location: SourceLocation
}

type ExtractionState = {
    Ontology: IGraph
    Shapes: IGraph
    SourceMap: Map<Uri, SourceLocation>
    Clarifications: Map<string, string>
    Metadata: ExtractionMetadata
    UnmappedTypes: UnmappedType list
}
```

Implement serialization:
- `save : string -> ExtractionState -> Result<unit, string>` — writes JSON to the given path. The `Ontology` and `Shapes` graphs serialize as Turtle strings (use dotNetRdf's `CompressingTurtleWriter`). `SourceMap` keys serialize as URI strings.
- `load : string -> Result<ExtractionState, string>` — reads JSON from path, deserializes Turtle strings back to graphs using dotNetRdf's `TurtleParser`. Returns `Error` with message if file not found or parse fails.
- Helper: `defaultStatePath : string -> string` — given a project directory, returns `{projectDir}/obj/frank-cli/state.json`

Use `System.Text.Json` for JSON serialization. Define a custom `JsonConverter` for `IGraph` that round-trips through Turtle string representation.

### T010: DiffEngine.fs — Graph Comparison

**Module**: `Frank.Cli.Core.State.DiffEngine`

**File location**: `src/Frank.Cli.Core/State/DiffEngine.fs`

Define output types:

```fsharp
type DiffEntry = {
    Type: string          // "Class", "Property", "Shape", "Triple"
    Uri: Uri
    Label: string option
    Field: string option  // property/field name if applicable
    From: string option   // previous value (for Modified)
    To: string option     // new value (for Modified)
}

type DiffResult = {
    Added: DiffEntry list
    Removed: DiffEntry list
    Modified: DiffEntry list
}
```

Implement:
- `diffGraphs : IGraph -> IGraph -> DiffResult` — compares two graphs triple-by-triple. A triple present in the new graph but not the old is "Added"; present in old but not new is "Removed". Modification detection: if subject+predicate exists in both but with different objects, classify as "Modified" with `From`/`To` set to the string representations of the old and new object nodes.
- `diffStates : ExtractionState -> ExtractionState -> DiffResult` — diffs both ontology and shapes graphs, merging the results.
- `formatDiff : DiffResult -> string` — produces a human-readable summary string suitable for CLI output.

Triple comparison must use dotNetRdf's `INode.Equals` semantics (URI equality is by string, literals by value+datatype).

### T011: Vocabulary Constants

**Module**: `Frank.Cli.Core.Rdf.Vocabularies`

**File location**: `src/Frank.Cli.Core/Rdf/Vocabularies.fs`

Define one sub-module per vocabulary. Each module exposes the namespace URI as `ns` and individual term URIs as string constants. Example pattern:

```fsharp
module Rdf =
    let ns = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"
    let Type = ns + "type"
    let Property = ns + "Property"

module Rdfs =
    let ns = "http://www.w3.org/2000/01/rdf-schema#"
    let Class = ns + "Class"
    let SubClassOf = ns + "subClassOf"
    let Domain = ns + "domain"
    let Range = ns + "range"
    let Label = ns + "label"
    let Comment = ns + "comment"

module Owl =
    let ns = "http://www.w3.org/2002/07/owl#"
    let Class = ns + "Class"
    let ObjectProperty = ns + "ObjectProperty"
    let DatatypeProperty = ns + "DatatypeProperty"
    let EquivalentClass = ns + "equivalentClass"
    let EquivalentProperty = ns + "equivalentProperty"
    let Thing = ns + "Thing"

module Shacl =
    let ns = "http://www.w3.org/ns/shacl#"
    let NodeShape = ns + "NodeShape"
    let PropertyShape = ns + "PropertyShape"
    let TargetClass = ns + "targetClass"
    let Path = ns + "path"
    let Datatype = ns + "datatype"
    let MinCount = ns + "minCount"
    let MaxCount = ns + "maxCount"
    let Class = ns + "class"

module Hydra =
    let ns = "http://www.w3.org/ns/hydra/core#"
    let Resource = ns + "Resource"
    let Operation = ns + "Operation"
    let SupportedOperation = ns + "supportedOperation"
    let Method = ns + "method"
    let ApiDocumentation = ns + "ApiDocumentation"

module SchemaOrg =
    let ns = "https://schema.org/"
    let Action = ns + "Action"
    let ReadAction = ns + "ReadAction"
    let CreateAction = ns + "CreateAction"
    let UpdateAction = ns + "UpdateAction"
    let DeleteAction = ns + "DeleteAction"
    let Name = ns + "name"
    let Description = ns + "description"
    let Email = ns + "email"
    let Url = ns + "url"
    let Price = ns + "price"
    let DateCreated = ns + "dateCreated"
    let DateModified = ns + "dateModified"
```

Add an XSD module for datatype URIs used in SHACL shapes:

```fsharp
module Xsd =
    let ns = "http://www.w3.org/2001/XMLSchema#"
    let String = ns + "string"
    let Integer = ns + "integer"
    let Double = ns + "double"
    let Boolean = ns + "boolean"
    let DateTime = ns + "dateTime"
```

### T012: Unit Tests

**Project**: Frank.Cli.Core.Tests

**Files to create**:

`test/Frank.Cli.Core.Tests/Rdf/FSharpRdfTests.fs`:
- Create a graph and assert triples, then query back with `triplesWithSubject` and verify count
- Test `getNode` returns `Some` for existing URI and `None` for missing URI
- Test DU roundtrip: create `UriNode`, convert to INode with `fromRdfNode`, convert back with `toRdfNode`, verify equality

`test/Frank.Cli.Core.Tests/State/ExtractionStateTests.fs`:
- Build a minimal `ExtractionState` with a non-empty ontology graph (at least 3 triples)
- Call `save` to a temp file path, then `load` from the same path
- Verify loaded state has same number of triples in ontology graph
- Verify metadata fields round-trip correctly (timestamp, baseUri, vocabularies)
- Test `load` on a non-existent path returns `Error`

`test/Frank.Cli.Core.Tests/State/DiffEngineTests.fs`:
- Create two graphs; graph2 has one added triple, one removed triple vs graph1
- Call `diffGraphs` and verify `Added` has 1 entry, `Removed` has 1 entry, `Modified` is empty
- Test modification detection: same subject+predicate, different object in graph2 → `Modified` list has 1 entry

Register all test modules in `Program.fs` using Expecto's `testList` composition.

## Review Guidance

- `FSharpRdf` functions must never return null — verify with tests using deliberate null-trap assertions
- `ExtractionState` save/load must be a true roundtrip: triple count in ontology graph must be identical before and after serialization
- `DiffEngine` correctness: manually trace through a known 3-triple example to verify Added/Removed/Modified classification
- Vocabulary constants: spot-check URIs against the actual specifications (schema.org, hydra, SHACL W3C spec)
