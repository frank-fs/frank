---
work_package_id: WP12
title: ShapeLoader and Internal Refactoring in Frank.Validation
lane: "doing"
dependencies:
- WP10
base_branch: 005-shacl-validation-from-fsharp-types-WP10
base_commit: c0a8102670d8a4b4a3a7b2e7d6071ad5cab8f18c
created_at: '2026-03-15T13:53:26.954185+00:00'
subtasks: [T064, T065, T066, T067, T068, T069, T070]
shell_pid: "8753"
agent: "claude-opus"
history:
- timestamp: '2026-03-14T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated from build-time SHACL unification design spec
design_ref: docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md
requirement_refs: [FR-001, FR-008, FR-009]
---

# Work Package Prompt: WP12 -- ShapeLoader and Internal Refactoring in Frank.Validation

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
spec-kitty implement WP12 --base WP10
```

Depends on WP10 (enriched Turtle artifact). Can be developed in parallel with WP11 -- use a manually-generated Turtle file for testing.

---

## Objectives & Success Criteria

- Create `UriConventions.fs` -- migrate pure URI helper functions from `ShapeDerivation.fs`
- Create `ShapeLoader.fs` -- load pre-computed SHACL shapes from embedded Turtle resource
- Change `ShaclShape.TargetType` from `Type` to `Type option`
- Change `ValidationMarker.ShapeType` from `Type` to `Uri`
- Refactor `ShapeCache` from `Type`-keyed to `Uri`-keyed, pre-populated at startup
- Trim `TypeMapping.fs` to retain only `xsdUri` (delete `mapType`)
- Delete `ShapeDerivation.fs`
- Update `DataGraphBuilder.fs` and `ShapeGraphBuilder.fs` imports
- All existing tests updated and passing

---

## Context & Constraints

**Reference documents**:
- `docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md` -- WP12 section
- `src/Frank.Validation/ShapeDerivation.fs` -- Code to extract and delete
- `src/Frank.Validation/ShapeGraphBuilder.fs` -- Reverse logic for deserialization; update imports
- `src/Frank.Validation/DataGraphBuilder.fs` -- Update imports from ShapeDerivation to UriConventions
- `src/Frank.Validation/ShapeCache.fs` -- Refactor keying
- `src/Frank.Validation/Types.fs` -- Amend ShaclShape.TargetType
- `src/Frank.Validation/Constraints.fs` -- Amend ValidationMarker.ShapeType
- `src/Frank.Validation/ValidationMiddleware.fs` -- Update cache lookup calls

**Key constraints**:
- Frank.Validation targets net8.0;net9.0;net10.0 (multi-targeting)
- `ShapeLoader` depends only on dotNetRdf (already a dependency) for Turtle parsing
- No FCS dependency in Frank.Validation -- that stays in Frank.Cli.Core
- `UriConventions.fs` must be early in compile order (before DataGraphBuilder, ShapeGraphBuilder)
- `ShapeLoader.fs` must be after Types.fs, UriConventions.fs, and before ShapeCache.fs
- This WP can be tested against a manually-created Turtle file before WP11's MSBuild target exists

---

## Subtasks & Detailed Guidance

### Subtask T064 -- Create `UriConventions.fs`

**Purpose**: Extract pure URI construction functions from `ShapeDerivation.fs` into a shared module.

**Steps**:
1. Create `src/Frank.Validation/UriConventions.fs`.
2. Move these functions from `ShapeDerivation.fs`:

```fsharp
namespace Frank.Validation

open System

/// Shared URI construction conventions for SHACL shapes and properties.
/// These match the URIs emitted by Frank.Cli.Core's ShapeGenerator.
module UriConventions =

    /// Build a NodeShape URI for a type.
    /// Pattern: urn:frank:shape:{assembly-name}:{type-full-name}
    let buildNodeShapeUri (assemblyName: string) (typeFullName: string) =
        let encoded = Uri.EscapeDataString(typeFullName)
        Uri(sprintf "urn:frank:shape:%s:%s" assemblyName encoded)

    /// Build a property path URI for a field name.
    /// Pattern: urn:frank:property:{fieldName}
    let buildPropertyPathUri (fieldName: string) =
        sprintf "urn:frank:property:%s" fieldName
```

3. Note: The original `ShapeDerivation.buildNodeShapeUri` takes a `System.Type` and extracts assembly/type names from it. The new version takes strings so it works without reflection. Add an overload that takes `Type` for backwards compatibility during the transition.

4. Add `UriConventions.fs` to `.fsproj` compile list BEFORE `DataGraphBuilder.fs` and `ShapeGraphBuilder.fs`.

**Files**: `src/Frank.Validation/UriConventions.fs` (new), `src/Frank.Validation/Frank.Validation.fsproj`

### Subtask T065 -- Update DataGraphBuilder and ShapeGraphBuilder imports

**Purpose**: Switch `DataGraphBuilder.fs` and `ShapeGraphBuilder.fs` from `ShapeDerivation` to `UriConventions`.

**Steps**:
1. In `DataGraphBuilder.fs`, replace all `ShapeDerivation.buildPropertyPathUri` calls with `UriConventions.buildPropertyPathUri`:
   - Line 58: `ShapeDerivation.buildPropertyPathUri prop.Path` -> `UriConventions.buildPropertyPathUri prop.Path`
   - Line 66: same
   - Line 84: same

2. In `ShapeGraphBuilder.fs`, replace:
   - Line 59: `ShapeDerivation.buildPropertyPathUri prop.Path` -> `UriConventions.buildPropertyPathUri prop.Path`

3. Verify both files compile without importing `ShapeDerivation`.

**Files**: `src/Frank.Validation/DataGraphBuilder.fs`, `src/Frank.Validation/ShapeGraphBuilder.fs`
**Notes**: This is a mechanical find-and-replace. The function signatures are identical.

### Subtask T066 -- Amend `ShaclShape.TargetType` to `Type option`

**Purpose**: Make `TargetType` optional so shapes loaded from Turtle (where Type is not available) can exist.

**Steps**:
1. In `src/Frank.Validation/Types.fs`, change:

```fsharp
type ShaclShape =
    { TargetType: Type option       // Changed from Type
      NodeShapeUri: Uri
      Properties: PropertyShape list
      Closed: bool
      Description: string option }
```

2. Find all usages of `shape.TargetType` across Frank.Validation and update:
   - `ShapeDerivation.fs` (will be deleted, but update first if needed for interim compilation)
   - `ShapeCache.fs` (switch to Uri keying -- T067)
   - `ShapeGraphBuilder.fs` (check if TargetType is used -- it's not in the current code)
   - `ValidationMiddleware.fs` (check if TargetType is used)
   - Test files

3. The compiler will flag every incomplete pattern match, making this safe to refactor.

**Files**: `src/Frank.Validation/Types.fs`, plus all files that reference `TargetType`

### Subtask T067 -- Refactor ShapeCache and ValidationMarker

**Purpose**: Switch from `Type`-keyed to `Uri`-keyed caching and pre-population.

**Steps**:
1. In `src/Frank.Validation/Constraints.fs`, change `ValidationMarker`:

```fsharp
type ValidationMarker =
    { ShapeUri: Uri                 // Changed from ShapeType: Type
      CustomConstraints: CustomConstraint list
      ResolverConfig: ShapeResolverConfig option }
```

2. In `src/Frank.Validation/ShapeCache.fs`, refactor:

```fsharp
type ShapeCache() =
    let cache = ConcurrentDictionary<Uri, struct (ShapesGraph * ShaclShape)>()

    /// Pre-populate the cache with all shapes loaded from the embedded resource.
    member _.LoadAll(shapes: ShaclShape list) =
        for shape in shapes do
            let shapesGraph = ShapeGraphBuilder.buildShapesGraph shape
            cache.TryAdd(shape.NodeShapeUri, struct (shapesGraph, shape)) |> ignore

    /// Get a cached shape by its NodeShape URI.
    member _.TryGet(shapeUri: Uri) : struct (ShapesGraph * ShaclShape) voption =
        match cache.TryGetValue(shapeUri) with
        | true, value -> ValueSome value
        | false, _ -> ValueNone

    /// Get or create a ShapesGraph for a resolved ShaclShape (capability-dependent).
    member _.GetOrAddResolved(shape: ShaclShape) : struct (ShapesGraph * ShaclShape) =
        cache.GetOrAdd(
            shape.NodeShapeUri,
            fun _ ->
                let shapesGraph = ShapeGraphBuilder.buildShapesGraph shape
                struct (shapesGraph, shape))

    /// Clear all cached shapes. Useful for testing.
    member _.Clear() = cache.Clear()
```

3. Update `ValidationMiddleware.fs` to use `shapeCache.TryGet(marker.ShapeUri)` instead of `shapeCache.GetOrAdd(marker.ShapeType)`.

**Files**: `src/Frank.Validation/Constraints.fs`, `src/Frank.Validation/ShapeCache.fs`, `src/Frank.Validation/ValidationMiddleware.fs`

### Subtask T068 -- Create `ShapeLoader.fs`

**Purpose**: Load pre-computed SHACL shapes from an embedded Turtle resource.

**Steps**:
1. Create `src/Frank.Validation/ShapeLoader.fs`:

```fsharp
namespace Frank.Validation

open System
open System.IO
open System.Reflection
open VDS.RDF
open VDS.RDF.Parsing

/// Loads pre-computed SHACL shapes from embedded Turtle resources.
module ShapeLoader =

    let private sh = "http://www.w3.org/ns/shacl#"
    let private xsd = "http://www.w3.org/2001/XMLSchema#"
    let private rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"

    let private resourceName = "Frank.Semantic.shapes.shacl.ttl"

    /// Parse an XSD datatype URI to an XsdDatatype value.
    let private parseXsdDatatype (uri: string) : XsdDatatype option =
        match uri with
        | u when u = xsd + "string" -> Some XsdString
        | u when u = xsd + "integer" -> Some XsdInteger
        | u when u = xsd + "long" -> Some XsdLong
        | u when u = xsd + "double" -> Some XsdDouble
        | u when u = xsd + "decimal" -> Some XsdDecimal
        | u when u = xsd + "boolean" -> Some XsdBoolean
        | u when u = xsd + "dateTimeStamp" -> Some XsdDateTimeStamp
        | u when u = xsd + "dateTime" -> Some XsdDateTime
        | u when u = xsd + "date" -> Some XsdDate
        | u when u = xsd + "time" -> Some XsdTime
        | u when u = xsd + "duration" -> Some XsdDuration
        | u when u = xsd + "anyURI" -> Some XsdAnyUri
        | u when u = xsd + "base64Binary" -> Some XsdBase64Binary
        | _ -> None

    /// Extract an integer value from a literal node.
    let private intValue (node: INode) : int =
        match node with
        | :? ILiteralNode as lit -> int lit.Value
        | _ -> 0

    /// Extract a string value from a literal node.
    let private strValue (node: INode) : string =
        match node with
        | :? ILiteralNode as lit -> lit.Value
        | :? IUriNode as uri -> uri.Uri.ToString()
        | _ -> ""

    /// Walk an RDF list (rdf:first/rdf:rest/rdf:nil) and collect string values.
    let private readRdfStringList (graph: IGraph) (listNode: INode) : string list =
        // ... walk rdf:first/rdf:rest chain collecting literal values
        []  // Implementation: follow rdf:first for value, rdf:rest for next, until rdf:nil

    /// Walk an RDF list and collect URI values.
    let private readRdfUriList (graph: IGraph) (listNode: INode) : Uri list =
        // ... walk rdf:first/rdf:rest chain collecting URI values
        []

    /// Deserialize a single property shape blank node into a PropertyShape.
    let private loadPropertyShape (graph: IGraph) (propNode: INode) : PropertyShape =
        // Read sh:path, sh:datatype, sh:minCount, sh:maxCount, sh:pattern, sh:in, sh:or, sh:node
        // ... query triples with propNode as subject
        { Path = ""; Datatype = None; MinCount = 0; MaxCount = None
          NodeReference = None; InValues = None; OrShapes = None
          Pattern = None; MinInclusive = None; MaxInclusive = None; Description = None }

    /// Deserialize all NodeShapes from the graph into ShaclShape values.
    let loadFromGraph (graph: IGraph) : ShaclShape list =
        let nodeShapeType = graph.CreateUriNode(UriFactory.Create(sh + "NodeShape"))
        let rdfType = graph.CreateUriNode(UriFactory.Create(rdf + "type"))
        // Find all subjects that are rdf:type sh:NodeShape
        // For each, extract properties and build ShaclShape with TargetType = None
        []

    /// Load shapes from an assembly's embedded resource.
    let loadFromAssembly (assembly: Assembly) : ShaclShape list =
        use stream = assembly.GetManifestResourceStream(resourceName)
        if isNull stream then
            raise (InvalidOperationException(
                sprintf "Embedded resource '%s' not found in assembly '%s'. \
                         Ensure Frank.Cli.MSBuild is referenced and 'dotnet build' has been run, \
                         or run 'frank-cli extract --emit-artifacts' manually."
                        resourceName (assembly.GetName().Name)))
        let graph = new Graph()
        use reader = new StreamReader(stream)
        let parser = TurtleParser()
        parser.Load(graph, reader)
        loadFromGraph graph
```

2. Add `ShapeLoader.fs` to `.fsproj` compile list after `UriConventions.fs` and before `ShapeCache.fs`.

**Files**: `src/Frank.Validation/ShapeLoader.fs` (new), `src/Frank.Validation/Frank.Validation.fsproj`
**Notes**: The `loadPropertyShape` and `loadFromGraph` functions are the reverse of `ShapeGraphBuilder.buildShapesGraph`. Use `ShapeGraphBuilder.fs` as the reference for which triples to read.

### Subtask T069 -- Delete ShapeDerivation.fs and trim TypeMapping.fs

**Purpose**: Remove superseded reflection-based code.

**Steps**:
1. Delete `src/Frank.Validation/ShapeDerivation.fs`.
2. Remove it from the `.fsproj` compile list.
3. In `src/Frank.Validation/TypeMapping.fs`, delete the `mapType` function (reflection-based `Type -> XsdDatatype option`). Retain only `xsdUri : XsdDatatype -> Uri`.
4. Verify no remaining references to `ShapeDerivation` anywhere in Frank.Validation.
5. Verify `TypeMapping.xsdUri` is still used by `DataGraphBuilder.fs` (it is -- for constructing data graph literals).

**Files**: `src/Frank.Validation/ShapeDerivation.fs` (delete), `src/Frank.Validation/TypeMapping.fs` (trim), `src/Frank.Validation/Frank.Validation.fsproj`

### Subtask T070 -- Create ShapeLoader tests

**Purpose**: Verify shapes are correctly deserialized from Turtle.

**Steps**:
1. Create a test Turtle file with known shapes (manually authored or generated by WP10's enriched ShapeGenerator).
2. Test cases:

**a. Basic record shape**:
- Turtle with one NodeShape, three property shapes (string, int, bool)
- Load and verify: ShaclShape has 3 PropertyShapes with correct paths, datatypes, minCount, maxCount

**b. Optional field**:
- Property with sh:minCount 0
- Verify: PropertyShape.MinCount = 0

**c. Guid field**:
- Property with sh:datatype xsd:string and sh:pattern (UUID regex)
- Verify: PropertyShape.Pattern = Some "^[0-9a-fA-F]..."

**d. Simple DU (sh:in)**:
- Property with sh:in RDF list ["Red"; "Green"; "Blue"]
- Verify: PropertyShape.InValues = Some ["Red"; "Green"; "Blue"]

**e. Payload DU (sh:or)**:
- Property with sh:or RDF list of NodeShape URIs
- Verify: PropertyShape.OrShapes = Some [uri1; uri2]

**f. Closed shape**:
- NodeShape with sh:closed true
- Verify: ShaclShape.Closed = true

**g. TargetType is None**:
- Verify all loaded shapes have TargetType = None

**h. Missing embedded resource**:
- Call `loadFromAssembly` with an assembly that has no embedded resource
- Verify `InvalidOperationException` with descriptive message

**i. Round-trip with ShapeGraphBuilder**:
- Build a ShaclShape programmatically
- Convert to IGraph via ShapeGraphBuilder
- Serialize to Turtle string
- Parse back and load via ShapeLoader
- Verify the loaded shape matches the original (modulo TargetType = None)

**Files**: `test/Frank.Validation.Tests/ShapeLoaderTests.fs` (new)
**Validation**: `dotnet build` and `dotnet test` pass.

---

## Test Strategy

- Run `dotnet build` to verify Frank.Validation compiles without ShapeDerivation.fs
- Run `dotnet test` for ShapeLoader tests and all existing tests
- Verify DataGraphBuilder and ShapeGraphBuilder still work with UriConventions imports
- Round-trip test is the key validation: build shape -> serialize -> deserialize -> compare

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Deleting ShapeDerivation.fs breaks existing tests | Update tests first (T065, T066) before deleting; compiler catches all missing references |
| RDF list deserialization is complex | Port logic from ShapeGraphBuilder's buildRdfList in reverse; test each list type |
| ShaclShape.TargetType change cascades widely | Compiler flags every usage; most consumers only use NodeShapeUri and Properties |
| TypeMapping.mapType deletion breaks tests | Update test references; only xsdUri is needed at runtime |

---

## Review Guidance

- Verify UriConventions functions match ShapeDerivation originals exactly
- Verify ShapeLoader deserializes every constraint type that ShapeGraphBuilder serializes
- Verify ShaclShape.TargetType is Type option everywhere (no missed usages)
- Verify ValidationMarker.ShapeType is Uri (not Type)
- Verify ShapeCache is pre-populated at startup (not lazy derivation)
- Verify ShapeDerivation.fs is fully deleted (no remnants)
- Verify TypeMapping.fs retains only xsdUri
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-14T00:00:00Z -- system -- lane=planned -- Prompt created from build-time SHACL unification design.
- 2026-03-15T13:53:27Z – claude-opus – shell_pid=8753 – lane=doing – Assigned agent via workflow command
