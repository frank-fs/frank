---
work_package_id: "WP01"
subtasks:
  - "T001"
  - "T002"
  - "T003"
  - "T004"
  - "T005"
  - "T006"
title: "Project Setup & Test Infrastructure"
phase: "Phase 0 - Foundation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: []
requirement_refs: ["FR-013"]
history:
  - timestamp: "2026-03-15T23:59:02Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP01 -- Project Setup & Test Infrastructure

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback, update `review_status: acknowledged`.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````xml`, ````bash`

---

## Implementation Command

No dependencies -- branch from target branch:
```bash
spec-kitty implement WP01
```

---

## Objectives & Success Criteria

1. A new test project `Frank.RdfValidation.Tests` exists at `test/Frank.RdfValidation.Tests/` and compiles successfully.
2. The project is added to `Frank.sln` under the `test` solution folder.
3. Shared test helpers are available in `TestHelpers.fs`:
   - TestHost creation with LinkedData + Provenance middleware
   - RDF graph loading from string (Turtle, JSON-LD, RDF/XML)
   - SPARQL query execution against single graphs and named graph datasets
4. `dotnet build test/Frank.RdfValidation.Tests/` succeeds with no warnings.
5. `dotnet test test/Frank.RdfValidation.Tests/` runs (even if no test cases yet -- Expecto entry point works).

---

## Context & Constraints

- **Spec**: `kitty-specs/015-rdf-sparql-validation/spec.md` -- test-only feature, no runtime library code
- **Plan**: `kitty-specs/015-rdf-sparql-validation/plan.md` -- project structure, design decisions D1-D5
- **Research**: `kitty-specs/015-rdf-sparql-validation/research.md` -- R1 (SPARQL execution), R2 (graph isomorphism), R3 (TestHost pattern), R5 (dotNetRdf packaging)
- **Quickstart**: `kitty-specs/015-rdf-sparql-validation/quickstart.md` -- .fsproj template, key dotNetRdf types
- **Data Model**: `kitty-specs/015-rdf-sparql-validation/data-model.md` -- RDF graph structures, SPARQL query patterns
- **Constitution VI**: All `IGraph`, `TripleStore`, `HttpClient`, `TestServer`, `StreamReader` must use `use` bindings
- **Constitution VIII**: Shared test helpers extracted to `TestHelpers.fs` to avoid duplicated logic across test modules
- **Existing patterns**: Reference `test/Frank.LinkedData.Tests/` and `test/Frank.Provenance.Tests/` for .fsproj structure, TestHost setup, and Expecto conventions

---

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `.fsproj` with Dependencies

- **Purpose**: Establish the project file with all required dependencies and correct configuration.
- **Files**: `test/Frank.RdfValidation.Tests/Frank.RdfValidation.Tests.fsproj` (new file)
- **Parallel?**: No (must exist before other subtasks)

**Steps**:

1. Create the directory `test/Frank.RdfValidation.Tests/` if it does not exist.
2. Create the `.fsproj` file following the exact pattern from `test/Frank.LinkedData.Tests/Frank.LinkedData.Tests.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <GenerateProgramFile>false</GenerateProgramFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="TestHelpers.fs" />
    <Compile Include="RdfParsingTests.fs" />
    <Compile Include="SparqlResourceQueryTests.fs" />
    <Compile Include="ProvenanceGraphTests.fs" />
    <Compile Include="GraphCoherenceTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.3" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0" />
    <PackageReference Include="dotNetRdf.Core" Version="3.5.1" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.LinkedData/Frank.LinkedData.fsproj" />
    <ProjectReference Include="../../src/Frank.Provenance/Frank.Provenance.fsproj" />
  </ItemGroup>
</Project>
```

**Key decisions**:
- `dotNetRdf.Core` is listed as an explicit `PackageReference` even though it's a transitive dependency -- this ensures SPARQL query types (`VDS.RDF.Query.*`) are available (Research R5)
- `TestHost` version `10.0.0` matches the target framework `net10.0`
- Both `Frank.LinkedData` and `Frank.Provenance` are project references (not package references)
- `<Compile>` items list ALL source files in dependency order: `TestHelpers.fs` first (shared), then test modules, then `Program.fs` last

**Notes**: The test modules (`RdfParsingTests.fs`, etc.) will be created as stub files in this WP to allow compilation. Actual test implementations come in WP02-WP05.

---

### Subtask T002 -- Create `Program.fs` (Expecto Entry Point)

- **Purpose**: Standard Expecto test runner entry point, matching all other Frank test projects.
- **Files**: `test/Frank.RdfValidation.Tests/Program.fs` (new file)
- **Parallel?**: No (depends on T001 for .fsproj)

**Steps**:

1. Create `Program.fs` with the standard Expecto entry point:

```fsharp
module Frank.RdfValidation.Tests.Program

open Expecto

[<EntryPoint>]
let main argv =
    Tests.runTestsInAssemblyWithCLIArgs [] argv
```

This is the same pattern used in all Frank test projects. It discovers all `[<Tests>]` attributes in the assembly.

---

### Subtask T003 -- Add Project to `Frank.sln`

- **Purpose**: Ensure the test project is discoverable by `dotnet test` and IDE tooling.
- **Files**: `Frank.sln` (modified)
- **Parallel?**: No (depends on T001)

**Steps**:

1. From the repository root, run:
```bash
dotnet sln Frank.sln add test/Frank.RdfValidation.Tests/Frank.RdfValidation.Tests.fsproj --solution-folder test
```

2. Verify the project appears in the solution:
```bash
dotnet sln Frank.sln list | grep RdfValidation
```

**Important**: The `--solution-folder test` flag groups the project under the `test` folder in the solution, matching the convention used by all other test projects.

---

### Subtask T004 -- Create `TestHelpers.fs` with TestHost Creation Helper

- **Purpose**: Provide a reusable function that creates a TestHost with both LinkedData and Provenance middleware, sample resource endpoints, and proper DI registration. All downstream test modules depend on this.
- **Files**: `test/Frank.RdfValidation.Tests/TestHelpers.fs` (new file)
- **Parallel?**: No (must be created before T005, T006)

**Steps**:

1. Create `TestHelpers.fs` as the first `<Compile>` item in the .fsproj.

2. Module declaration:
```fsharp
module Frank.RdfValidation.Tests.TestHelpers
```

3. Implement `createTestHost` function that:
   - Creates a `HostBuilder` with `UseTestServer()`
   - Registers `LinkedDataConfig` in DI (with an ontology `IGraph` containing at least 2 resource types)
   - Registers provenance services (`IObservable<TransitionEvent>`, `IProvenanceStore`)
   - Applies `UseRouting()`, then LinkedData middleware, then Provenance middleware
   - Registers at least 2-3 sample endpoints:
     - `GET /person/1` returning JSON `{"Name":"Alice","Age":30}` with `LinkedDataMarker` metadata
     - `GET /order/42` returning JSON `{"Product":"Widget","Quantity":5}` with `LinkedDataMarker` metadata
     - Optionally a `POST /person` endpoint (for unsafe transition testing in WP03)
   - Returns the started `IHost`

4. Build the test ontology graph:
   - Create OWL property declarations for each JSON property that could appear in responses
   - Register namespace prefixes (`rdf:`, `rdfs:`, `owl:`, `xsd:`, application-specific prefix)
   - This ontology enables the LinkedData middleware to convert JSON responses to proper RDF

**Reference patterns**:
- `test/Frank.LinkedData.Tests/ContentNegotiationTests.fs` lines 22-60: `createTestHost` function
- `test/Frank.Provenance.Tests/IntegrationTests.fs` lines 22-80: `TransitionSubject`, `makeAuthPrincipal`, TestHost setup
- The `LinkedDataConfig` type requires: `OntologyGraph`, `ShapesGraph`, `BaseUri`, `Manifest`
- The `SemanticManifest` type requires: `Version`, `BaseUri`, `SourceHash`, `Vocabularies`, `GeneratedAt`

**Key decisions**:
- The TestHost must produce real RDF output that dotNetRdf can parse -- this means the ontology must define properties matching the JSON keys in the sample endpoint responses
- Use `NullLogger.Instance` for the LinkedData middleware (matching LinkedData tests pattern)
- The TransitionSubject pattern from Provenance tests avoids a System.Reactive dependency

---

### Subtask T005 -- Add RDF Graph Loading Helpers

- **Purpose**: Parse RDF response bodies from HTTP responses into dotNetRdf `IGraph` instances for SPARQL querying.
- **Files**: `test/Frank.RdfValidation.Tests/TestHelpers.fs` (add to existing)
- **Parallel?**: No (adds to T004's file)

**Steps**:

1. Add three graph loading functions:

```fsharp
/// Parse a Turtle string into an IGraph.
let loadTurtleGraph (turtle: string) : IGraph =
    let graph = new Graph()
    use reader = new System.IO.StringReader(turtle)
    let parser = TurtleParser()
    parser.Load(graph, reader)
    graph

/// Parse a JSON-LD string into an IGraph.
let loadJsonLdGraph (jsonld: string) : IGraph =
    let graph = new Graph()
    use reader = new System.IO.StringReader(jsonld)
    let parser = JsonLdParser()
    parser.Load(graph, reader)
    graph

/// Parse an RDF/XML string into an IGraph.
let loadRdfXmlGraph (rdfxml: string) : IGraph =
    let graph = new Graph()
    use reader = new System.IO.StringReader(rdfxml)
    let parser = RdfXmlParser()
    parser.Load(graph, reader)
    graph
```

2. Add a convenience function to fetch RDF from the TestHost:

```fsharp
/// Send a GET request with a specific Accept header and return the response body.
let getRdfResponse (client: HttpClient) (path: string) (accept: string) : Async<string> =
    async {
        let request = new HttpRequestMessage(HttpMethod.Get, path)
        request.Headers.Add("Accept", accept)
        let! (response: HttpResponseMessage) = client.SendAsync(request) |> Async.AwaitTask
        response.EnsureSuccessStatusCode() |> ignore
        let! body = response.Content.ReadAsStringAsync() |> Async.AwaitTask
        return body
    }
```

**Key types to open**:
```fsharp
open VDS.RDF
open VDS.RDF.Parsing
open System.IO
open System.Net.Http
```

**Notes**:
- The `StringReader` must use `use` binding for proper disposal (Constitution VI)
- Each loading function creates a new `Graph()` -- callers are responsible for disposing with `use` bindings
- The `JsonLdParser` type name may vary by dotNetRdf version -- check the actual type available in dotNetRdf.Core 3.5.1 (it may be in a separate namespace or require `VDS.RDF.Parsing` import)

---

### Subtask T006 -- Add SPARQL Execution Helpers

- **Purpose**: Provide reusable functions to execute SPARQL queries against in-memory graphs and datasets, returning typed results.
- **Files**: `test/Frank.RdfValidation.Tests/TestHelpers.fs` (add to existing)
- **Parallel?**: No (adds to T005's file)

**Steps**:

1. Add SPARQL execution for a single graph:

```fsharp
/// Execute a SPARQL SELECT query against a single graph and return the result set.
let executeSparql (graph: IGraph) (sparql: string) : SparqlResultSet =
    let dataset = InMemoryDataset(graph)
    let processor = LeviathanQueryProcessor(dataset)
    let parser = SparqlQueryParser()
    let query = parser.ParseFromString(sparql)
    match processor.ProcessQuery(query) with
    | :? SparqlResultSet as results -> results
    | _ -> failwith "Expected SparqlResultSet from SELECT/ASK query"
```

2. Add SPARQL execution for named graph datasets:

```fsharp
/// Execute a SPARQL query against a TripleStore with named graphs.
let executeSparqlOnDataset (store: ITripleStore) (sparql: string) : SparqlResultSet =
    let dataset = InMemoryDataset(store)
    let processor = LeviathanQueryProcessor(dataset)
    let parser = SparqlQueryParser()
    let query = parser.ParseFromString(sparql)
    match processor.ProcessQuery(query) with
    | :? SparqlResultSet as results -> results
    | _ -> failwith "Expected SparqlResultSet from SELECT/ASK query"
```

3. Add a convenience function for ASK queries:

```fsharp
/// Execute a SPARQL ASK query and return the boolean result.
let executeSparqlAsk (graph: IGraph) (sparql: string) : bool =
    let dataset = InMemoryDataset(graph)
    let processor = LeviathanQueryProcessor(dataset)
    let parser = SparqlQueryParser()
    let query = parser.ParseFromString(sparql)
    match processor.ProcessQuery(query) with
    | :? SparqlResultSet as results -> results.Result
    | _ -> failwith "Expected SparqlResultSet from ASK query"
```

**Key types to open**:
```fsharp
open VDS.RDF.Query
open VDS.RDF.Query.Datasets
```

**Notes**:
- `LeviathanQueryProcessor` is the standard in-memory SPARQL 1.1 engine in dotNetRdf (Research R1)
- `InMemoryDataset` can wrap either a single `IGraph` or a `ITripleStore` (for named graphs)
- The `ProcessQuery` return type depends on the query type: `SparqlResultSet` for SELECT/ASK, `IGraph` for CONSTRUCT/DESCRIBE
- For ASK queries, `SparqlResultSet.Result` is a `bool` property
- The `SparqlQueryParser` is stateless and could be cached, but creating a new instance per call is fine for tests

4. Create stub files for the test modules that will be implemented in WP02-WP05:

Create minimal compilable stubs for:
- `RdfParsingTests.fs`
- `SparqlResourceQueryTests.fs`
- `ProvenanceGraphTests.fs`
- `GraphCoherenceTests.fs`

Each stub should look like:
```fsharp
module Frank.RdfValidation.Tests.RdfParsingTests

open Expecto

[<Tests>]
let tests =
    testList "RDF Parsing" []
```

This ensures the project compiles even before the test modules are implemented.

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| dotNetRdf.Core 3.5.1 may not include JSON-LD parser | Check if `VDS.RDF.Parsing.JsonLdParser` exists; if not, install `dotNetRdf.Writing.JsonLd` or use VDS.RDF.Parsing namespace discovery |
| TestHost middleware ordering matters | Reference both LinkedData and Provenance test projects for correct `app.Use()` ordering |
| Frank.LinkedData or Frank.Provenance may not be fully implemented yet | Create the project structure and helpers regardless; tests that depend on unimplemented features will fail gracefully |
| `SparqlQueryParser` may have changed API in dotNetRdf 3.5.1 | Check `VDS.RDF.Parsing.SparqlQueryParser` vs `VDS.RDF.Query.SparqlQueryParser` -- the class may be in a different namespace |

---

## Review Guidance

- Verify `.fsproj` matches the pattern from `Frank.LinkedData.Tests.fsproj` (same PropertyGroup, same package versions)
- Verify `TestHelpers.fs` is listed first in `<Compile>` items
- Verify `use` bindings for all disposable types (IGraph, TripleStore, StringReader, HttpClient, TestServer)
- Verify the project compiles with `dotnet build test/Frank.RdfValidation.Tests/`
- Verify `dotnet test test/Frank.RdfValidation.Tests/` runs (even with empty test lists)
- Verify the project appears in `Frank.sln` under the `test` solution folder

---

## Activity Log

- 2026-03-15T23:59:02Z -- system -- lane=planned -- Prompt created.
