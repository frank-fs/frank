---
work_package_id: WP07
title: Frank.LinkedData — Core Runtime
lane: "doing"
dependencies: [WP02]
base_branch: 001-semantic-resources-phase1-WP02
base_commit: 315030704c62f190579e2cb728841588bb44cd61
created_at: '2026-03-05T19:31:27.107158+00:00'
subtasks:
- T035
- T036
- T037
- T038
phase: Phase 2 - LinkedData
assignee: ''
agent: ''
shell_pid: "94850"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-04T22:10:13Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-015
- FR-020
- FR-021
---

# WP07: Frank.LinkedData — Core Runtime

> **Review Feedback Status**: No review feedback yet.

## Review Feedback

_No feedback recorded._

> **Markdown Formatting Note**: Use ATX headings (`#`), fenced code blocks with language tags, and standard bullet lists. Do not use HTML tags or custom directives.

## Implementation Command

```
spec-kitty implement WP07 --base WP02
```

## Objectives & Success Criteria

Implement the core runtime layer of `Frank.LinkedData`: loading OWL/SHACL definitions from embedded assembly resources at startup, and projecting handler return values to RDF triples at request time.

Success criteria:
- `GraphLoader` successfully loads `ontology.owl.xml`, `shapes.shacl.ttl`, and `manifest.json` from embedded assembly resources
- `GraphLoader` returns a descriptive error (not an exception) when expected resources are absent
- `InstanceProjector` converts a known F# record type to the expected set of RDF triples
- `InstanceProjector` correctly handles option fields (skip when `None`), nested records (blank nodes), and list fields (one triple per element)
- `InstanceProjector` caches `Type → PropertyInfo[]` mappings so repeated projections do not re-run reflection
- `loadConfig` validates all resources are present before returning; missing resources produce a descriptive `Result.Error`

## Context & Constraints

- `Frank.LinkedData` is a runtime library; it must be fast and allocation-efficient on the hot path (per request)
- Embedded resource names are fixed by convention: `Frank.Semantic.ontology.owl.xml`, `Frank.Semantic.shapes.shacl.ttl`, `Frank.Semantic.manifest.json` — these are the names the CLI's `CompileCommand` (WP06/T031) documents in its `CompileResult.embeddedResourceNames`
- The consuming application embeds these files via MSBuild (the `Frank.Cli.MSBuild` package from WP01); `GraphLoader` must not assume a specific calling assembly — it accepts an `Assembly` argument so tests can supply a test assembly
- Only `dotNetRdf.Core` is a direct dependency; do not introduce `dotNetRdf.Ontology` or `dotNetRdf.Shacl` in `Frank.LinkedData` (those belong to `Frank.Cli.Core`)
- Reference `data-model.md` for the `LinkedDataConfig` and `ResourceRdfProjection` entity definitions
- Reference `research.md` for the dotNetRdf embedded resource loading patterns explored during research

## Subtasks & Detailed Guidance

### T035: GraphLoader.fs

Module: `Frank.LinkedData.Rdf.GraphLoader`

```fsharp
type LoadedSemantics =
    { OntologyGraph : IGraph
      ShapesGraph   : IGraph
      Manifest      : SemanticManifest }

val load : Assembly -> Result<LoadedSemantics, string>
```

Implementation notes:
- Open the manifest stream first: `assembly.GetManifestResourceStream("Frank.Semantic.manifest.json")` — if `null`, return `Result.Error "Assembly '<name>' does not contain embedded resource 'Frank.Semantic.manifest.json'. Run 'frank-cli compile' and add the output files as EmbeddedResource items."`
- Deserialise the manifest JSON stream using `System.Text.Json.JsonSerializer.Deserialize<SemanticManifest>` with `JsonSerializerOptions(PropertyNameCaseInsensitive = true)`
- Open the ontology stream: `assembly.GetManifestResourceStream("Frank.Semantic.ontology.owl.xml")` — if `null`, return a similar descriptive error
- Parse with `dotNetRdf`'s `RdfXmlParser`: create a new `Graph()`, call `parser.Load(graph, new StreamReader(stream))`
- Open the shapes stream and parse with `TurtleParser` using the same pattern
- On parse failure, catch `RdfParseException` and wrap in `Result.Error` with the exception message
- Return `Result.Ok { OntologyGraph = ...; ShapesGraph = ...; Manifest = ... }` on full success

Do not use `EmbeddedResourceLoader` from dotNetRdf if it requires the file to be on disk; always use `Assembly.GetManifestResourceStream`.

### T036: InstanceProjector.fs

Module: `Frank.LinkedData.Rdf.InstanceProjector`

```fsharp
val project : IGraph -> Uri -> obj -> IGraph
```

Parameters:
- `ontologyGraph` — the loaded ontology graph (used to look up property URIs by name)
- `resourceUri` — the URI of the subject node for the projected instance
- `instance` — the handler return value (type `obj`)

Implementation notes:

**Type-to-property mapping**:
- For each public instance property of `instance.GetType()`:
  1. Look up a property node in `ontologyGraph` whose local name matches the .NET property name (case-insensitive); if no match, skip silently
  2. Create a subject node for `resourceUri`
  3. Create an object node based on the property value type:
     - `string` → `LiteralNode` with no datatype
     - `int`, `int64`, `float`, `decimal` → `LiteralNode` with `xsd:integer` / `xsd:decimal`
     - `bool` → `LiteralNode` with `xsd:boolean`
     - `DateTimeOffset`, `DateTime` → `LiteralNode` with `xsd:dateTime`, ISO 8601 format
     - `FSharpOption<_>` with `None` → skip; with `Some v` → recurse with the inner value
     - `IEnumerable<_>` (but not `string`) → emit one triple per element; for complex elements use a blank node (see nested record handling)
     - Nested F# record (detected via `FSharp.Reflection.FSharpType.IsRecord`) → create a `BlankNode`, recursively project properties of the nested record attaching them to the blank node, then use the blank node as the object
     - All other types → call `.ToString()` and emit as a plain `LiteralNode`
  4. Add the triple `(subject, property, object)` to the output graph

**Performance**:
- Cache `Type -> PropertyInfo[]` in a `ConcurrentDictionary<Type, PropertyInfo[]>` at module level
- Cache the ontology property lookup index `string -> INode` (built once per `ontologyGraph` instance) — key by graph identity (`obj` reference, not content)

**Output**:
- Return a new `Graph()` containing only the projected triples (do not mutate `ontologyGraph`)

### T037: LinkedDataConfig.fs

Module: `Frank.LinkedData.Config`

Type definitions:

```fsharp
type SemanticManifest =
    { Version      : string
      BaseUri      : string
      SourceHash   : string
      Vocabularies : string list
      GeneratedAt  : DateTimeOffset }

type LinkedDataConfig =
    { OntologyGraph : IGraph
      ShapesGraph   : IGraph
      BaseUri       : string
      Manifest      : SemanticManifest }
```

Function:

```fsharp
val loadConfig : Assembly -> Result<LinkedDataConfig, string>
```

Implementation:
- Call `GraphLoader.load assembly`
- On `Result.Error`, propagate the error string unchanged
- On `Result.Ok semantics`:
  - Validate `semantics.Manifest.BaseUri` is a well-formed URI; if not, return `Result.Error "Manifest baseUri is not a valid URI: <value>"`
  - Validate `semantics.Manifest.Version` is non-empty; if empty, return `Result.Error "Manifest version is empty"`
  - Return `Result.Ok { OntologyGraph = semantics.OntologyGraph; ShapesGraph = semantics.ShapesGraph; BaseUri = semantics.Manifest.BaseUri; Manifest = semantics.Manifest }`

This function is called once at startup and its result is stored in DI. It must not perform I/O after the first call — the caching of the result is the responsibility of the DI registration in WP08.

### T038: Unit tests

Location: `test/Frank.LinkedData.Tests/`

**GraphLoader tests**:

Create a test helper that compiles a minimal assembly with three embedded resources (a trivial OWL/XML ontology, a trivial SHACL Turtle file, and a minimal manifest JSON) and returns that `Assembly`.

- `"GraphLoader loads all three resources successfully"` — call `GraphLoader.load testAssembly` and verify `Result.isOk`
- `"GraphLoader returns descriptive error when manifest resource is absent"` — use an assembly with no embedded resources; verify the error string contains the assembly name and the expected resource name
- `"GraphLoader returns descriptive error when ontology XML is malformed"` — embed a syntactically invalid XML file; verify `Result.isError` and error string contains the parse error

**InstanceProjector tests**:

Define a test record type within the test project:

```fsharp
type TestPerson = { Name: string; Age: int; Homepage: string option }
type TestAddress = { Street: string; City: string }
type TestWithNested = { Label: string; Address: TestAddress }
type TestWithList = { Tags: string list }
```

Build a minimal `IGraph` with ontology property nodes for each field name.

- `"InstanceProjector projects string and int fields"` — project `{ Name = "Alice"; Age = 30; Homepage = None }` and verify two triples (Name and Age; Homepage skipped)
- `"InstanceProjector skips None option fields"` — verify no triple for `Homepage` when `None`
- `"InstanceProjector emits triple for Some option field"` — project with `Homepage = Some "https://example.com"` and verify triple present
- `"InstanceProjector handles nested record as blank node"` — project `{ Label = "x"; Address = { Street = "1 Main"; City = "Town" } }` and verify a blank node triple for Address
- `"InstanceProjector emits one triple per list element"` — project `{ Tags = ["a"; "b"; "c"] }` and verify three triples for Tags
- `"InstanceProjector caches PropertyInfo lookup"` — call project twice for the same type; the second call must not perform reflection (verify via a counter or mock, or simply assert performance is within a tight time bound)

**LinkedDataConfig tests**:

- `"loadConfig returns Ok for a valid assembly"` — use the test assembly from GraphLoader tests
- `"loadConfig propagates GraphLoader error"` — use an assembly with no resources; verify `Result.isError`
- `"loadConfig rejects invalid baseUri"` — embed a manifest with `"baseUri": "not-a-uri"`; verify error contains `"not a valid URI"`

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| `Assembly.GetManifestResourceStream` returns `null` vs. raising an exception depending on .NET version | Always null-check the stream before use; never call `Read` on a null stream |
| FSharp option type detection (`FSharpOption<_>`) requires F# reflection helpers | Use `FSharp.Reflection.FSharpType.IsUnion` + check union case names for `"Some"` / `"None"` pattern; test against all supported TFMs |
| `ConcurrentDictionary` cache could grow unbounded in a test harness that creates many types | Acceptable for a runtime library; document that the cache is process-scoped and not cleared |
| dotNetRdf `TurtleParser` constructor API may differ between 3.x minor versions | Confirm against `dotNetRdf.Core` 3.5.1 specifically; do not use APIs marked `[Obsolete]` |

## Review Guidance

- Run `dotnet test test/Frank.LinkedData.Tests/` — all tests must pass on all three TFMs (net8.0, net9.0, net10.0)
- Verify that projecting a 100-field record completes in under 1 ms on a warm cache (benchmark informally in a test)
- Confirm `Frank.LinkedData` has no dependency on `Frank.Cli.Core` (no circular reference)
- Check that the assembly `GetManifestResourceStream` call uses the exact resource name strings that `CompileCommand` (WP06/T031) documents

## Activity Log

| Timestamp | Lane | Agent | Action |
|---|---|---|---|
| 2026-03-04T22:10:13Z | planned | system | Prompt generated via /spec-kitty.tasks |
