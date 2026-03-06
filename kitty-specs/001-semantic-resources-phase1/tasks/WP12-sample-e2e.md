---
work_package_id: WP12
title: Sample App & End-to-End Integration
lane: done
dependencies:
- WP06
- WP08
- WP09
subtasks:
- T059
- T060
- T061
- T062
- T063
phase: Phase 3 - Integration
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-04T22:10:13Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-013, FR-014, FR-015, FR-016, FR-017, FR-018]
---

> **Review Feedback Status**: No review feedback yet.

## Review Feedback

_No feedback recorded._

> **Markdown Formatting Note**: All prose in this file uses plain Markdown. Code samples use fenced code blocks with language tags. Lists use `-` bullets.

## Objectives & Success Criteria

Create a sample Frank application with LinkedData enabled and validate the complete extract-to-serve pipeline end-to-end.

**Success criteria**:
- Sample app compiles and serves HTTP requests using Frank with `useLinkedData` and per-resource `linkedData`
- The full CLI pipeline (`frank-cli extract` → `frank-cli clarify` → `frank-cli validate` → `frank-cli compile` → `dotnet build`) runs without errors
- After build, the sample assembly contains all three embedded semantic artifacts
- HTTP requests with RDF `Accept` headers receive correct RDF responses; `application/json` requests are unaffected
- `quickstart.md` instructions match the actual workflow
- All existing Frank test projects (`Frank.Tests`, `Frank.Auth.Tests`, `Frank.Datastar.Tests`, `Frank.OpenApi.Tests`, `Frank.Analyzers.Tests`) continue to pass (SC-003)
- New test projects (`Frank.Cli.Core.Tests`, `Frank.LinkedData.Tests`) also pass

## Context & Constraints

- This WP validates the entire feature against all success criteria (SC-001 through SC-006)
- Sample app location: `sample/Frank.LinkedData.Sample/`
- The pipeline requires `frank-cli` to be available; prefer installing it as a `dotnet tool` local tool in the sample project's `dotnet-tools.json`, or use a project reference during development
- SC-003 is non-negotiable: zero behavioral changes to existing resources or tests
- Reference the `sample/` directory for existing sample app patterns (Datastar samples, Hox sample, Oxpecker sample)

## Subtasks & Detailed Guidance

### T059 — Sample app Program.fs

**File**: `sample/Frank.LinkedData.Sample/Program.fs`

Create a small but realistic Frank app that exercises the LinkedData feature with non-trivial domain types.

Domain model:

```fsharp
type ProductCategory = Electronics | Books | Clothing

type Product = {
    Id: int
    Name: string
    Price: decimal
    InStock: bool option
    Category: ProductCategory
}
```

Resources:

```fsharp
resource "/products" {
    linkedData
    name "Product Collection"
    get listProducts    // return all products as JSON / RDF
    post createProduct  // accept new product
}

resource "/products/{id}" {
    linkedData
    name "Product"
    get getProduct      // return single product as JSON / RDF
    put updateProduct   // replace product
    delete deleteProduct
}
```

App setup:

```fsharp
webHost argv {
    useLinkedData
    // resources...
}
```

Use an in-memory `Dictionary<int, Product>` for storage, seeded with a few sample products. The handlers should use `ctx.Negotiate(statusCode, value)` so Frank's content negotiation (including LinkedData) handles the response format.

Include `Frank.Cli.MSBuild` in the project file:

```xml
<PackageReference Include="Frank.Cli.MSBuild" Version="*" />
```

(Or a project reference during development: `<ProjectReference Include="../../src/Frank.Cli.MSBuild/Frank.Cli.MSBuild.csproj" />`.)

---

### T060 — Run full CLI pipeline

**File**: `test/Frank.LinkedData.Sample.Tests/PipelineTests.fs` (or a shell script `scripts/run-pipeline.sh` if Expecto integration proves impractical)

Prefer Expecto for CI integration. Document and verify each pipeline step:

1. `dotnet build sample/Frank.LinkedData.Sample/` — baseline build succeeds before any semantic artifacts exist
2. `frank-cli extract --project sample/Frank.LinkedData.Sample/Frank.LinkedData.Sample.fsproj` — produces `extract.json` in `obj/frank-cli/`; verify the file exists and contains at least the `/products` and `/products/{id}` routes
3. `frank-cli clarify --project ...` — review any clarification questions; for this simple app there should be none (or answer them programmatically); verify exit code 0
4. `frank-cli validate --project ...` — verify completeness of extracted metadata; verify exit code 0 and no validation errors in stdout
5. `frank-cli compile --project ...` — generates `ontology.owl.xml`, `shapes.shacl.ttl`, `manifest.json` in `obj/frank-cli/`; verify all three files exist and are non-empty
6. `dotnet build sample/Frank.LinkedData.Sample/` — second build embeds the artifacts via MSBuild targets; verify build succeeds

Each Expecto test can invoke the CLI commands via `Process.Start` or `dotnet run` and assert on exit codes and output file existence.

---

### T061 — Verify embedded resources + HTTP tests

**File**: `test/Frank.LinkedData.Sample.Tests/HttpTests.fs`

After the pipeline in T060 has run:

**Embedded resource verification**:
- Load the built assembly: `Assembly.LoadFrom("sample/Frank.LinkedData.Sample/bin/Debug/net10.0/Frank.LinkedData.Sample.dll")`
- Assert `assembly.GetManifestResourceNames()` contains:
  - `Frank.Semantic.ontology.owl.xml`
  - `Frank.Semantic.shapes.shacl.ttl`
  - `Frank.Semantic.manifest.json`

**HTTP content negotiation tests** (using ASP.NET Core `TestHost` or `HttpClient` against a running instance):

- `GET /products/1` with `Accept: application/ld+json` → 200, valid JSON-LD body, `Content-Type: application/ld+json`
  - Verify: body contains a reference to the `Product` class from the ontology
  - Verify: `Name`, `Price`, `InStock` properties appear as RDF predicates
- `GET /products/1` with `Accept: text/turtle` → 200, valid Turtle body
- `GET /products/1` with `Accept: application/rdf+xml` → 200, valid RDF/XML body
- `GET /products/1` with `Accept: application/json` → 200, standard JSON body (object with `id`, `name`, `price`, `inStock` fields), NOT RDF

For "valid" RDF body verification: attempt to parse the response body with dotNetRdf and assert no parse errors and at least one triple present.

---

### T062 — Quickstart validation

**File**: `quickstart.md` (already exists in the feature spec directory at `kitty-specs/001-semantic-resources-phase1/quickstart.md`)

Walk through `quickstart.md` verbatim, executing each command exactly as written:

1. Read each step in `quickstart.md`
2. Execute it in a clean environment (fresh clone or clean working directory)
3. Verify the expected outcome described in the doc matches what actually happens
4. If any step fails or produces different output, update `quickstart.md` to match reality

Common discrepancies to watch for:
- CLI flag names or argument syntax that changed during implementation
- File paths that differ from what was planned
- Commands that require prerequisites not mentioned in the doc
- Output formats that changed

This task is manual/exploratory by nature. Document any changes made to `quickstart.md` in the Activity Log.

---

### T063 — Solution integration + regression

Run all existing tests to confirm SC-003 (zero behavioral change) and verify new tests pass.

**Existing test projects** — all must pass:

```sh
dotnet build Frank.sln
dotnet test test/Frank.Tests/
dotnet test test/Frank.Auth.Tests/
dotnet test test/Frank.Datastar.Tests/
dotnet test test/Frank.OpenApi.Tests/
bash test/Frank.Analyzers.Tests/run-analyzer-tests.sh
```

**New test projects** — all must pass:

```sh
dotnet test test/Frank.Cli.Core.Tests/
dotnet test test/Frank.LinkedData.Tests/
```

**Solution file**: Verify all new `src/` and `test/` projects have been added to `Frank.sln`:
- `src/Frank.Cli.Core/`
- `src/Frank.Cli.MSBuild/`
- `src/Frank.LinkedData/`
- `test/Frank.Cli.Core.Tests/`
- `test/Frank.LinkedData.Tests/`

If any project is missing from `Frank.sln`, add it with `dotnet sln add`.

**Success criteria checklist** (verify each against the spec):

- SC-001: `frank-cli extract` discovers all resources from the sample app
- SC-002: `frank-cli compile` produces valid OWL ontology, SHACL shapes, and manifest
- SC-003: All existing tests pass, zero failures, zero behavioral changes in existing resources
- SC-004: GET requests with RDF `Accept` headers return correct RDF responses for LinkedData-enabled resources
- SC-005: Full pipeline runs end-to-end on the sample app without manual intervention
- SC-006: Non-LinkedData resources return identical responses before and after the feature is added

## Risks & Mitigations

- **E2E pipeline complexity**: The pipeline has many moving parts (CLI tool installation, FCS project loading, MSBuild targets, runtime content negotiation). If any step fails, isolate by testing each stage independently before diagnosing cross-stage issues.
- **frank-cli availability**: The CLI must be installed or available as a `dotnet tool` for the pipeline to work. Prefer `dotnet tool install --local` with a `dotnet-tools.json` so CI can reproduce the setup with `dotnet tool restore`.
- **Assembly loading for embedded resource verification**: Loading the built assembly in a test may lock the file on Windows. Use `Assembly.ReflectionOnlyLoadFrom` or `MetadataLoadContext` if necessary to avoid file locks.
- **Timing of pipeline steps in CI**: If T060 and T061 run as part of the same `dotnet test` invocation, ensure T060's build step completes before T061's HTTP tests start. Use Expecto's `sequenced` combinator to enforce ordering if needed.

## Review Guidance

This is the final validation WP. All six success criteria should be directly verifiable by running the tests and commands described here.

- Confirm `quickstart.md` matches the actual workflow after T062
- Confirm `dotnet build Frank.sln` succeeds with all projects added to the solution
- Confirm all existing test projects pass (SC-003) — this is the most critical check
- Confirm RDF responses contain ontology-derived terms, not just generic triples
- Confirm `Accept: application/json` requests to LinkedData-enabled resources return standard JSON (the LinkedData formatter must not intercept non-RDF accepts)

## Activity Log

| Timestamp | Agent | Action |
|-----------|-------|--------|
| 2026-03-04T22:10:13Z | system | Prompt generated via /spec-kitty.tasks |
