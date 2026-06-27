# Frank.Provenance Vertical Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the standalone `Frank.Provenance` vertical — request-level W3C PROV-O with vocabulary-mapped domain IRIs resolved at build time, plus a query endpoint and in-memory store — closing the load-bearing v7.3.2 gap (#325, #330) and unblocking #331/#332/#333.

**Architecture:** A build-time emitter (`ProvenanceEmitter`, Fabulous.AST) generates the **only** compile-time artifact — a `typeName → (ProvOClass, IRI)` map — from the existing `ResolvedModel`. At runtime, `ProvenanceMiddleware` resolves each request to its produced type via **stable ASP.NET metadata** (route pattern + `ProducesResponseTypeMetadata` from Frank.OpenApi's `produces`), looks the type up in the generated map, builds a PROV-O record (request=Activity, response=Entity, principal=Agent), appends it to a ported `MailboxProcessorStore`, and serves it inline (content-negotiated) or via `GET /provenance`. Missing `produces` ⇒ untyped `prov:Activity` (graceful degradation). Zero `Frank.Statecharts` coupling.

**Tech Stack:** F# (net8.0/net9.0/net10.0), ASP.NET Core, dotNetRdf.Core, Fabulous.AST + Fantomas (codegen), Expecto + Microsoft.AspNetCore.TestHost, MSBuild custom tasks, FCS (FSharp.Compiler.Service).

## Global Constraints

- **Build:** `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln` (ICU mismatch on nix-darwin).
- **Test sln:** `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample"`. New test projects under `test/` target **net10.0 only** and must be added to `Frank.sln`.
- **Worktree:** all work in `.claude/worktrees/provenance-vertical` (branch `provenance-vertical`). Bash cwd resets to master between calls — **use absolute worktree paths in every command**.
- **Codegen MUST use Fabulous.AST + Fantomas via `AstRender` helpers. NEVER string concatenation.** (CLAUDE.md, hard rule.)
- **PROV-O output is COMPACTED** (fix #6 from expert review). `ProvenanceGraph.compact` calls `JsonLdProcessor.Compact` against `{"prov":...,"http":...,"rdfs":...}`. Result: `prov:`/`http:`/`rdfs:` terms are CURIEs; domain IRIs (e.g. `https://schema.org/OrderAction`) stay FULL (no `schema:` prefix — intentional for cross-dataset linkability). Test assertions: use CURIEs for prov/http/rdfs terms (`prov:Activity`, `prov:Agent`, `http:methodName`, `prov:used`); use full IRIs for schema.org terms. Do NOT change `RdfSerialization.serializeGraphJsonLdWithContext` — LinkedData/Validation depend on its expanded behavior.
- **No statechart coupling:** `Frank.Provenance` must not reference `Frank.Statecharts`/`Frank.Statecharts.Core`.
- **Holzmann:** ≤2 nesting levels, ≤60-line functions, bounded loops, preconditions via `invalidArg`/`invalidOp`, no module-level mutable, `Result<_,string>` over `Option` for diagnostics.
- **Disposal:** `use` for every `IDisposable`. **No bare `with _ ->`** — log via `ILogger`.
- **Fantomas:** `dotnet fantomas --check src/` must pass before every commit. Pre-commit hook enforces it.
- Commit footer on every commit:
  ```
  Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>
  Claude-Session: https://claude.ai/code/session_01P5EphcEDpZQMv2A3roMkfh
  ```

---

## File Structure

**New package `src/Frank.Provenance/`** (compile order = listed order):

| File | Responsibility |
|------|----------------|
| `ProvenanceTypes.fs` | `ProvAgent`, `ProvenanceRecord`, `ProvenanceStoreConfig`, `ProvenanceConfig`. Pure data, no statechart fields. |
| `ProvVocabulary.fs` | Base PROV-O / RDF / XSD term IRI constants. |
| `ProvenanceGraph.fs` | Pure `ProvenanceRecord -> IGraph` + `toJsonLd`. |
| `IProvenanceStore.fs` | `IProvenanceStore` interface. |
| `MailboxProcessorStore.fs` | Ported agent-based store (append + resource/agent/time indexes + bounded eviction). |
| `GeneratedProvenanceResolver.fs` | Scan assemblies for `GeneratedProvenance`; build `ProvenanceConfig`. Fails closed. |
| `ProvenanceMiddleware.fs` | Per-request capture; metadata→type→IRI; store append; content-negotiated inline emit. |
| `ProvenanceEndpoint.fs` | `GET /provenance` query handler. |
| `Frank.Provenance.fs` | `useProvenance`/`useProvenanceWith` CE ops. |
| `Frank.Provenance.fsproj` | Project file. |

**Codegen / MSBuild:**
- Create `src/Frank.Cli.Core/ProvenanceEmitter.fs`
- Create `src/Frank.Cli.MSBuild/GenerateProvenanceTask.fs`
- Modify `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets` (add generate+inject target pair + `UsingTask`)

**Tests (net10.0, add each to `Frank.sln`):**
- `test/Frank.Provenance.Tests/` — `Main.fs`, `ProvenanceGraphTests.fs`, `StoreTests.fs`, `ResolverTests.fs`, `MiddlewareTestHelpers.fs`, `MiddlewareTests.fs`, `EndpointTests.fs`
- `test/Frank.Cli.Core.Tests/ProvenanceEmitterTests.fs` (add to existing project)
- `test/Frank.Cli.MSBuild.Tests/GenerateProvenanceTaskTests.fs` (add to existing project)

**Sample (composition + capstone):** wire into an existing sample under `sample/` in Tasks 16–17.

**Source-of-truth mirrors** (read before each corresponding task):
- Emitter: `src/Frank.Cli.Core/ValidationEmitter.fs`, `src/Frank.Cli.Core/DiscoveryEmitter.fs`, `src/Frank.Cli.Core/AstRender.fs`
- Resolver: `src/Frank.Validation/GeneratedValidationResolver.fs`, `src/Frank/GeneratedModuleReflection.fs`
- Middleware/CE: `src/Frank.Validation/ValidationMiddleware.fs`, `src/Frank.Validation/Frank.Validation.fs`
- Store: `git show 4d85df54~1:src/Frank.Provenance/MailboxProcessorStore.fs`
- MSBuild task: `src/Frank.Cli.MSBuild/GenerateValidationTask.fs`; targets: `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets` (the `FrankGenerateValidation`/`FrankInjectGeneratedValidationFile` pair)
- Serialization: `src/Frank.Semantic/RdfSerialization.fs`
- Test harness: `test/Frank.Validation.Tests/MiddlewareTestHelpers.fs`, `test/Frank.Cli.Core.Tests/ValidationEmitterTests.fs`

---

## Phase 0 — Outside-in: failing E2E first

### Task 1: Failing HTTP E2E (AC #3) against a hand-stub, then red

**Files:**
- Create: `test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`
- Create: `test/Frank.Provenance.Tests/Main.fs`
- Create: `test/Frank.Provenance.Tests/MiddlewareTestHelpers.fs`
- Create: `test/Frank.Provenance.Tests/MiddlewareTests.fs`
- Modify: `Frank.sln` (add the test project)

**Interfaces:**
- Consumes (from later tasks, hand-stubbed here): `ProvenanceConfig`, `ProvenanceMiddleware`, `ProvAgent`, `ProvenanceRecord`.
- Produces: the AC that pins the whole vertical.

This task proves the test harness compiles and the AC fails for the right reason (no middleware yet). The middleware is hand-stubbed minimally inside the test helper so the suite builds; the stub is deleted in Task 9 when the real middleware lands.

- [ ] **Step 1: Create the test project file**

`test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <Compile Include="MiddlewareTestHelpers.fs" />
    <Compile Include="MiddlewareTests.fs" />
    <Compile Include="Main.fs" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="Expecto" Version="10.2.1" />
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0-*" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../../src/Frank.Provenance/Frank.Provenance.fsproj" />
    <ProjectReference Include="../../src/Frank/Frank.fsproj" />
    <ProjectReference Include="../../src/Frank.Semantic/Frank.Semantic.fsproj" />
    <!-- No Frank.OpenApi ref: the test helper attaches the STANDARD
         Microsoft.AspNetCore.Http.Metadata.ProducesResponseTypeMetadata directly. -->
  </ItemGroup>
</Project>
```
> Confirm the exact Expecto + TestHost versions by copying the `<PackageReference>` lines from `test/Frank.Validation.Tests/Frank.Validation.Tests.fsproj`. Use those verbatim.

- [ ] **Step 2: Write the E2E test (will not compile until Tasks 2–9 exist)**

Because `Frank.Provenance` does not exist yet, this task is gated behind Tasks 2–9. **Reorder note for the executor:** implement Tasks 2–9 first (they are pure unit-tested units), then return here to make the E2E green. Write the test now as the north star:

`test/Frank.Provenance.Tests/MiddlewareTests.fs`:
```fsharp
module Frank.Provenance.Tests.MiddlewareTests

open System.Net.Http
open Expecto
open Frank.Provenance.Tests.MiddlewareTestHelpers

[<Tests>]
let tests =
    testList "ProvenanceMiddleware E2E" [
        testCaseAsync "POST with prov profile returns typed prov:Activity (AC #3)" <| async {
            use server = startProvenanceServer (orderProvConfig ())
            use client = server.CreateClient()
            use req = new HttpRequestMessage(HttpMethod.Post, "/orders")
            req.Headers.TryAddWithoutValidation(
                "Accept", "application/ld+json; profile=\"http://www.w3.org/ns/prov\"") |> ignore
            let! resp = client.SendAsync(req) |> Async.AwaitTask
            let! body = resp.Content.ReadAsStringAsync() |> Async.AwaitTask
            // The shared serializer (RdfSerialization.serializeGraphJsonLdWithContext) emits
            // EXPANDED JSON-LD — @type values are FULL IRIs, not CURIEs. Assert full IRIs.
            Expect.stringContains body "http://www.w3.org/ns/prov#Activity" "Activity type present"
            Expect.stringContains body "https://schema.org/OrderAction" "domain IRI from provClass"
            Expect.stringContains body "http://www.w3.org/ns/prov#Agent" "Agent present"
            Expect.isFalse (body.Contains "urn:frank:") "no hardcoded urn:frank: activity IRI"
        }
    ]
```

- [ ] **Step 3: Write `MiddlewareTestHelpers.fs`** (mirrors `test/Frank.Validation.Tests/MiddlewareTestHelpers.fs`)

```fsharp
module Frank.Provenance.Tests.MiddlewareTestHelpers

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.TestHost
open Microsoft.Extensions.DependencyInjection
open Frank.Semantic
open Frank.Provenance

/// A config whose generated map types OrderPlaced as (Activity, schema:OrderAction).
let orderProvConfig () : ProvenanceConfig =
    { ProvClasses =
        Map.ofList [ "Frank.Provenance.Tests.OrderPlaced",
                     (ProvOClass.Activity, Some(Uri "https://schema.org/OrderAction")) ]
      KnownNamespaces = [| "https://schema.org/" |]
      StoreConfig = ProvenanceStoreConfig.defaults }

type OrderPlaced = { Id: string }

let startProvenanceServer (config: ProvenanceConfig) =
    let builder = WebApplication.CreateBuilder()
    builder.WebHost.UseTestServer() |> ignore
    builder.Services.AddSingleton(config) |> ignore
    builder.Services.AddSingleton<IProvenanceStore>(
        fun sp ->
            MailboxProcessorProvenanceStore(
                config.StoreConfig,
                sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()
                  .CreateLogger("prov")) :> IProvenanceStore) |> ignore
    let app = builder.Build()
    app.UseMiddleware<ProvenanceMiddleware>() |> ignore

    // POST /orders declares it produces OrderPlaced at 201 via standard ASP.NET metadata.
    app.MapPost("/orders", Func<HttpContext, System.Threading.Tasks.Task>(fun ctx ->
        ctx.Response.StatusCode <- 201
        ctx.Response.WriteAsync("{}")))
       .WithMetadata(Microsoft.AspNetCore.Http.Metadata.ProducesResponseTypeMetadata(201, typeof<OrderPlaced>))
    |> ignore

    app.StartAsync().GetAwaiter().GetResult()
    app
```
> `ProducesResponseTypeMetadata` lives in `Microsoft.AspNetCore.Http.Metadata`. Confirm the constructor arity by checking `src/Frank.OpenApi/HandlerDefinition.fs:71` usage (`ProducesResponseTypeMetadata(statusCode, type, contentTypes)`); use the matching overload.

- [ ] **Step 4: Write `Main.fs`** (Expecto entrypoint — copy from `test/Frank.Validation.Tests/Main.fs`, change module name).

- [ ] **Step 5: Add to `Frank.sln`**

Run (absolute paths):
```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/provenance-vertical
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet sln Frank.sln add test/Frank.Provenance.Tests/Frank.Provenance.Tests.fsproj
```

- [ ] **Step 6: Commit (test scaffold only)**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/provenance-vertical
git add test/Frank.Provenance.Tests Frank.sln
git commit -m "test(provenance): #331 failing E2E for typed PROV-O response (AC #3)"
```
Expected: commit succeeds. The project will not yet build (depends on Tasks 2–9); that is the intended red.

---

## Phase 1 — Runtime package (pure units first, TDD)

### Task 2: `ProvenanceTypes.fs`

**Files:**
- Create: `src/Frank.Provenance/ProvenanceTypes.fs`
- Create: `src/Frank.Provenance/Frank.Provenance.fsproj`
- Test: `test/Frank.Provenance.Tests/StoreTests.fs` (type construction smoke; full store tests in Task 5)

**Interfaces:**
- Produces:
  ```fsharp
  type ProvAgent = { Id: string; Label: string option }
  type ProvenanceRecord =
      { Id: string
        ResourceUri: string
        HttpMethod: string
        StatusCode: int
        DomainType: (Frank.Semantic.ProvOClass * System.Uri) option  // None = untyped Activity
        Agent: ProvAgent
        StartedAt: System.DateTimeOffset
        EndedAt: System.DateTimeOffset }
  type ProvenanceStoreConfig = { MaxRecords: int; EvictionBatchSize: int }
  // module ProvenanceStoreConfig.defaults = { MaxRecords = 10_000; EvictionBatchSize = 100 }
  type ProvenanceConfig =
      { ProvClasses: Map<string, Frank.Semantic.ProvOClass * System.Uri option>
        KnownNamespaces: string[]
        StoreConfig: ProvenanceStoreConfig }
  ```

- [ ] **Step 1: Write the fsproj**

`src/Frank.Provenance/Frank.Provenance.fsproj`:
```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>provenance;prov-o;rdf;lineage</PackageTags>
    <Description>Request-level W3C PROV-O for Frank resources</Description>
  </PropertyGroup>
  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>
  <ItemGroup>
    <Compile Include="ProvenanceTypes.fs" />
    <Compile Include="ProvVocabulary.fs" />
    <Compile Include="ProvenanceGraph.fs" />
    <Compile Include="IProvenanceStore.fs" />
    <Compile Include="MailboxProcessorStore.fs" />
    <Compile Include="GeneratedProvenanceResolver.fs" />
    <Compile Include="ProvenanceMiddleware.fs" />
    <Compile Include="ProvenanceEndpoint.fs" />
    <Compile Include="Frank.Provenance.fs" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="../Frank/Frank.fsproj" />
    <ProjectReference Include="../Frank.Semantic/Frank.Semantic.fsproj" />
  </ItemGroup>
  <ItemGroup>
    <PackageReference Include="dotNetRdf.Core" Version="3.5.1" />
  </ItemGroup>
</Project>
```
> Confirm `dotNetRdf.Core` version against `src/Frank.Validation/Frank.Validation.fsproj` (3.5.1). No `dotNetRdf.Shacl` needed here.
> **No Frank.OpenApi reference.** The middleware reads the **standard** `Microsoft.AspNetCore.Http.Metadata.IProducesResponseTypeMetadata` (available net8/9/10 via `FrameworkReference Microsoft.AspNetCore.App`), not an OpenApi type. Frank.OpenApi targets net10.0-only and would break multi-target. Authoring `produces` is the consumer project's concern, not Frank.Provenance's. (Verified 2026-06-27: `produces` attaches the standard `ProducesResponseTypeMetadata` — `src/Frank.OpenApi/HandlerDefinition.fs:71`.)

- [ ] **Step 2: Write `ProvenanceTypes.fs`** with the exact types from Interfaces above, namespace `Frank.Provenance`, plus:
```fsharp
module ProvenanceStoreConfig =
    let defaults = { MaxRecords = 10_000; EvictionBatchSize = 100 }
```

- [ ] **Step 3: Add the package to `Frank.sln`**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/provenance-vertical
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet sln Frank.sln add src/Frank.Provenance/Frank.Provenance.fsproj
```

- [ ] **Step 4: Build the package alone to verify it compiles**

Run:
```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/provenance-vertical
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build src/Frank.Provenance/Frank.Provenance.fsproj 2>&1 | tail -5
```
Expected: FAIL — missing `.fs` files referenced in fsproj (ProvVocabulary etc.). This confirms the fsproj compile list. (It will go green at Task 9.) Alternatively, temporarily comment out not-yet-created `<Compile>` lines, build `ProvenanceTypes.fs` alone to PASS, then restore. Prefer the latter for a clean green.

- [ ] **Step 5: Commit**

```bash
git add src/Frank.Provenance/ProvenanceTypes.fs src/Frank.Provenance/Frank.Provenance.fsproj Frank.sln
git commit -m "feat(provenance): #330 ProvenanceTypes + package scaffold"
```

### Task 3: `ProvVocabulary.fs`

**Files:**
- Create: `src/Frank.Provenance/ProvVocabulary.fs`

**Interfaces:**
- Produces: `module ProvVocabulary` with string constants:
  ```fsharp
  let Namespace = "http://www.w3.org/ns/prov#"
  module Class = let Activity=..#Activity; let Entity=..#Entity; let Agent=..#Agent
  module Property =
      let WasGeneratedBy=..#wasGeneratedBy; let WasAssociatedWith=..#wasAssociatedWith
      let Used=..#used; let StartedAtTime=..#startedAtTime; let EndedAtTime=..#endedAtTime
  module Rdf = let Type="http://www.w3.org/1999/02/22-rdf-syntax-ns#type"
  module Xsd = let DateTime="http://www.w3.org/2001/XMLSchema#dateTime"; let Integer=..#integer
  let FrankNamespace = "https://frankfs.dev/ns/prov#"   // for frank:httpMethod, frank:statusCode
  module Frank = let HttpMethod=FrankNamespace+"httpMethod"; let StatusCode=FrankNamespace+"statusCode"
  ```

- [ ] **Step 1: Write `ProvVocabulary.fs`** with the constants above (no statechart terms). Namespace `Frank.Provenance`, `[<RequireQualifiedAccess>] module ProvVocabulary`.

- [ ] **Step 2: Commit** — `git commit -m "feat(provenance): #330 base PROV-O term IRIs"`

### Task 4: `ProvenanceGraph.fs` (pure record → IGraph)

**Files:**
- Create: `src/Frank.Provenance/ProvenanceGraph.fs`
- Test: `test/Frank.Provenance.Tests/ProvenanceGraphTests.fs`

**Interfaces:**
- Consumes: `ProvenanceRecord`, `ProvVocabulary`, `Frank.Semantic.ProvOClass`, `Frank.Semantic.RdfSerialization`.
- Produces:
  ```fsharp
  module ProvenanceGraph
  val toGraph : ProvenanceRecord -> VDS.RDF.IGraph
  val toJsonLd : ProvenanceRecord -> string         // PROV-O ld+json, @context prov
  val listToJsonLd : ProvenanceRecord list -> string
  ```

- [ ] **Step 1: Write the failing test**

`test/Frank.Provenance.Tests/ProvenanceGraphTests.fs`:
```fsharp
module Frank.Provenance.Tests.ProvenanceGraphTests

open System
open Expecto
open Frank.Semantic
open Frank.Provenance

let private rec0 dt =
    { Id = "urn:uuid:act-1"
      ResourceUri = "/orders/1"
      HttpMethod = "POST"
      StatusCode = 201
      DomainType = dt
      Agent = { Id = "urn:agent:alice"; Label = Some "alice" }
      StartedAt = DateTimeOffset(2026, 6, 27, 0, 0, 0, TimeSpan.Zero)
      EndedAt = DateTimeOffset(2026, 6, 27, 0, 0, 1, TimeSpan.Zero) }

[<Tests>]
let tests =
    testList "ProvenanceGraph" [
        test "typed Activity carries domain IRI + prov:Activity + Agent" {
            let g = ProvenanceGraph.toJsonLd (rec0 (Some(ProvOClass.Activity, Uri "https://schema.org/OrderAction")))
            Expect.stringContains g "Activity" "prov:Activity present"
            Expect.stringContains g "https://schema.org/OrderAction" "domain IRI present"
            Expect.stringContains g "wasAssociatedWith" "agent association present"
        }
        test "untyped Activity omits any domain IRI but is still prov:Activity" {
            let g = ProvenanceGraph.toJsonLd (rec0 None)
            Expect.stringContains g "Activity" "still an Activity"
            Expect.isFalse (g.Contains "schema.org/OrderAction") "no domain IRI when DomainType None"
        }
    ]
```

- [ ] **Step 2: Run, verify it fails**

Run:
```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/provenance-vertical
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Provenance.Tests/ --filter "ProvenanceGraph" 2>&1 | tail -15
```
Expected: FAIL (compile error — `ProvenanceGraph` undefined). Add `<Compile Include="ProvenanceGraphTests.fs" />` to the test fsproj before `Main.fs`.

- [ ] **Step 3: Implement `ProvenanceGraph.fs`**

Build the graph with dotNetRdf (mirror the old `GraphBuilder.fs` triple-assertion style, minus statechart fields). Node layout:
- entity node `ResourceUri` → `rdf:type prov:Entity`; `prov:wasGeneratedBy` activity.
- activity node `Id` → `rdf:type prov:Activity`; `prov:startedAtTime`/`prov:endedAtTime` (xsd:dateTime, `o` format); `prov:wasAssociatedWith` agent; `frank:httpMethod` (plain literal); `frank:statusCode` (xsd:integer).
- agent node `Agent.Id` → `rdf:type prov:Agent`; `rdfs:label` if `Label` Some.
- `DomainType`: `(ProvOClass.Activity, iri)` → assert `activity rdf:type iri`; `(Entity, iri)` → `entity rdf:type iri`; `(Agent, iri)` → `agent rdf:type iri`.

```fsharp
module Frank.Provenance.ProvenanceGraph

open System
open VDS.RDF
open Frank.Semantic

let private provContext = """{"@context":{"prov":"http://www.w3.org/ns/prov#","frank":"https://frankfs.dev/ns/prov#","rdfs":"http://www.w3.org/2000/01/rdf-schema#"}}"""

let private u (g: IGraph) (s: string) = g.CreateUriNode(UriFactory.Create s) :> INode
let private lit (g: IGraph) (v: string) (dt: string) = g.CreateLiteralNode(v, UriFactory.Create dt) :> INode
let private plain (g: IGraph) (v: string) = g.CreateLiteralNode v :> INode
let private assertT (g: IGraph) s p o = g.Assert(Triple(s, p, o)) |> ignore

let private domainTypeNode (g: IGraph) (record: ProvenanceRecord) (cls: ProvOClass) =
    match record.DomainType with
    | Some(c, iri) when c = cls -> Some(u g (iri.AbsoluteUri))
    | _ -> None

let toGraph (record: ProvenanceRecord) : IGraph =
    let g = new Graph() :> IGraph
    let rdfType = u g ProvVocabulary.Rdf.Type
    let entity = u g record.ResourceUri
    let activity = u g record.Id
    let agent = u g record.Agent.Id
    // entity
    assertT g entity rdfType (u g ProvVocabulary.Class.Entity)
    assertT g entity (u g ProvVocabulary.Property.WasGeneratedBy) activity
    domainTypeNode g record ProvOClass.Entity |> Option.iter (assertT g entity rdfType)
    // activity
    assertT g activity rdfType (u g ProvVocabulary.Class.Activity)
    domainTypeNode g record ProvOClass.Activity |> Option.iter (assertT g activity rdfType)
    assertT g activity (u g ProvVocabulary.Property.StartedAtTime) (lit g (record.StartedAt.ToString "o") ProvVocabulary.Xsd.DateTime)
    assertT g activity (u g ProvVocabulary.Property.EndedAtTime) (lit g (record.EndedAt.ToString "o") ProvVocabulary.Xsd.DateTime)
    assertT g activity (u g ProvVocabulary.Property.WasAssociatedWith) agent
    assertT g activity (u g ProvVocabulary.Frank.HttpMethod) (plain g record.HttpMethod)
    assertT g activity (u g ProvVocabulary.Frank.StatusCode) (lit g (string record.StatusCode) ProvVocabulary.Xsd.Integer)
    // agent
    assertT g agent rdfType (u g ProvVocabulary.Class.Agent)
    domainTypeNode g record ProvOClass.Agent |> Option.iter (assertT g agent rdfType)
    match record.Agent.Label with
    | Some l -> assertT g agent (u g "http://www.w3.org/2000/01/rdf-schema#label") (plain g l)
    | None -> ()
    g

let toJsonLd (record: ProvenanceRecord) : string =
    RdfSerialization.serializeGraphJsonLdWithContext (toGraph record) provContext

let listToJsonLd (records: ProvenanceRecord list) : string =
    let g = new Graph() :> IGraph
    for r in records do g.Merge(toGraph r) |> ignore
    RdfSerialization.serializeGraphJsonLdWithContext g provContext
```
> The `toGraph` body is 20 lines but does one job (assemble triples). If a reviewer flags it >60-line or multi-job, extract `addEntity`/`addActivity`/`addAgent` private helpers (mirror old `GraphBuilder`).

- [ ] **Step 4: Run, verify PASS**

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Provenance.Tests/ --filter "ProvenanceGraph" 2>&1 | tail -8
```
Expected: 2 passed.

- [ ] **Step 5: Fantomas + commit**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/provenance-vertical
dotnet fantomas src/Frank.Provenance/ProvenanceGraph.fs
git add src/Frank.Provenance/ProvenanceGraph.fs test/Frank.Provenance.Tests/
git commit -m "feat(provenance): #330 pure ProvenanceRecord -> PROV-O graph + ld+json"
```

### Task 5: `IProvenanceStore.fs` + `MailboxProcessorStore.fs` (ported)

**Files:**
- Create: `src/Frank.Provenance/IProvenanceStore.fs`
- Create: `src/Frank.Provenance/MailboxProcessorStore.fs`
- Test: `test/Frank.Provenance.Tests/StoreTests.fs`

**Interfaces:**
- Produces:
  ```fsharp
  type IProvenanceStore =
      abstract Append : ProvenanceRecord -> unit
      abstract QueryByResource : string -> ProvenanceRecord list
      abstract QueryByAgent : string -> ProvenanceRecord list
  type MailboxProcessorProvenanceStore =
      new : ProvenanceStoreConfig * Microsoft.Extensions.Logging.ILogger -> MailboxProcessorProvenanceStore
      interface IProvenanceStore
      interface System.IDisposable
  ```

- [ ] **Step 1: Write the failing test**

`test/Frank.Provenance.Tests/StoreTests.fs`:
```fsharp
module Frank.Provenance.Tests.StoreTests

open System
open Expecto
open Microsoft.Extensions.Logging.Abstractions
open Frank.Semantic
open Frank.Provenance

let private mk id resource =
    { Id = id; ResourceUri = resource; HttpMethod = "POST"; StatusCode = 201
      DomainType = None; Agent = { Id = "urn:agent:a"; Label = None }
      StartedAt = DateTimeOffset.UnixEpoch; EndedAt = DateTimeOffset.UnixEpoch }

[<Tests>]
let tests =
    testList "MailboxProcessorProvenanceStore" [
        test "append then query by resource returns the record" {
            use store = new MailboxProcessorProvenanceStore(ProvenanceStoreConfig.defaults, NullLogger.Instance)
            (store :> IProvenanceStore).Append(mk "a1" "/orders/1")
            let got = (store :> IProvenanceStore).QueryByResource "/orders/1"
            Expect.equal got.Length 1 "one record for the resource"
        }
        test "bounded eviction caps retained records" {
            let cfg = { MaxRecords = 4; EvictionBatchSize = 2 }
            use store = new MailboxProcessorProvenanceStore(cfg, NullLogger.Instance)
            let s = store :> IProvenanceStore
            for i in 1..10 do s.Append(mk (string i) "/r")
            Expect.isLessThanOrEqual (s.QueryByResource "/r").Length cfg.MaxRecords "never exceeds MaxRecords"
        }
    ]
```
Add both `<Compile>` lines to the test fsproj (before `Main.fs`).

- [ ] **Step 2: Run, verify it fails** (`--filter "MailboxProcessorProvenanceStore"`) — compile error, store undefined.

- [ ] **Step 3: Implement the store**

Port `git show 4d85df54~1:src/Frank.Provenance/MailboxProcessorStore.fs` with these deltas:
- `IProvenanceStore` interface as above (drop `QueryByTimeRange` unless a test needs it — YAGNI).
- Adapt index keys to the new record: resource index keyed by `record.ResourceUri`, agent index keyed by `record.Agent.Id`.
- Keep the agent-loop + `evictIfNeeded` + `rebuildIndexes` machinery (it already satisfies bounded-loop rule via `MaxRecords`/`EvictionBatchSize`).
- `IDisposable` posts `Dispose` to the agent then disposes it; `ensureNotDisposed` precondition via `ObjectDisposedException`.
- Log evictions via the injected `ILogger`.

> Read the full old file first; reproduce the agent loop faithfully. The old `Append`/`QueryByResource`/`QueryByAgent` message handlers map 1:1.

- [ ] **Step 4: Run, verify PASS** (2 passed).

- [ ] **Step 5: Fantomas + commit** — `git commit -m "feat(provenance): #330 in-memory MailboxProcessor store (ported, statechart-free)"`

### Task 6: `GeneratedProvenanceResolver.fs`

**Files:**
- Create: `src/Frank.Provenance/GeneratedProvenanceResolver.fs`
- Test: `test/Frank.Provenance.Tests/ResolverTests.fs`

**Interfaces:**
- Consumes: `Frank.GeneratedModuleReflection.readStaticProp`, `findSinglePublicType` (see `src/Frank/GeneratedModuleReflection.fs`), `ProvenanceConfig`, `ProvOClass`.
- Produces:
  ```fsharp
  module GeneratedProvenanceResolver
  // Generated module exposes:
  //   provClasses : (string * (string * string)) list   // typeName, (ProvOClass case name, IRI or "")
  //   knownNamespaces : string[]
  val resolveFromType : System.Type -> Result<ProvenanceConfig, string>
  val resolveGeneratedConfig : System.Reflection.Assembly[] -> Result<ProvenanceConfig, string>
  ```

- [ ] **Step 1: Write the failing test**

`test/Frank.Provenance.Tests/ResolverTests.fs`:
```fsharp
module Frank.Provenance.Tests.ResolverTests

open Expecto
open Frank.Semantic
open Frank.Provenance

type FakeGeneratedProvenance() =
    static member val provClasses : (string * (string * string)) list =
        [ "MyApp.OrderPlaced", ("Activity", "https://schema.org/OrderAction")
          "MyApp.Ping", ("Activity", "") ] with get
    static member val knownNamespaces : string[] = [| "https://schema.org/" |] with get

[<Tests>]
let tests =
    testList "GeneratedProvenanceResolver" [
        test "resolveFromType maps case name + IRI, empty IRI -> None" {
            match GeneratedProvenanceResolver.resolveFromType typeof<FakeGeneratedProvenance> with
            | Ok cfg ->
                Expect.equal cfg.ProvClasses.["MyApp.OrderPlaced"]
                    (ProvOClass.Activity, Some(System.Uri "https://schema.org/OrderAction")) "typed entry"
                Expect.equal cfg.ProvClasses.["MyApp.Ping"] (ProvOClass.Activity, None) "empty IRI -> None"
            | Error e -> failtestf "expected Ok, got %s" e
        }
    ]
```
> Match the static-member shape that `readStaticProp` expects by reading `src/Frank.Validation/GeneratedValidationResolver.fs` + `GeneratedModuleReflection.fs`. If `readStaticProp` reads properties (not auto-properties), adjust the fake to expose `static member provClasses` as a getter — copy the exact shape `ValidationEmitter` emits (`AstRender.valueDecl "shapesGraph" …` ⇒ a module-level `let`, surfaced as a static property on the module type). The fake must mirror that.

- [ ] **Step 2: Run, verify it fails.**

- [ ] **Step 3: Implement** (mirror `GeneratedValidationResolver.fs`):
```fsharp
module Frank.Provenance.GeneratedProvenanceResolver

open System
open System.Reflection
open Frank.Semantic
open Frank.GeneratedModuleReflection

let private parseProvClass (name: string) : Result<ProvOClass, string> =
    match name with
    | "Entity" -> Ok ProvOClass.Entity
    | "Activity" -> Ok ProvOClass.Activity
    | "Agent" -> Ok ProvOClass.Agent
    | other -> Error $"unknown ProvOClass '{other}'"

let private toEntry (typeName: string, (clsName: string, iri: string)) =
    parseProvClass clsName
    |> Result.map (fun cls ->
        let iriOpt = if String.IsNullOrEmpty iri then None else Some(Uri iri)
        typeName, (cls, iriOpt))

let private buildConfig (t: Type) : Result<ProvenanceConfig, string> =
    match readStaticProp<(string * (string * string)) list> "provClasses" t,
          readStaticProp<string[]> "knownNamespaces" t with
    | Ok entries, Ok ns ->
        let folded =
            (Ok [], entries) ||> List.fold (fun acc e ->
                match acc, toEntry e with
                | Error x, _ -> Error x
                | _, Error x -> Error x
                | Ok xs, Ok x -> Ok(x :: xs))
        folded |> Result.map (fun pairs ->
            { ProvClasses = Map.ofList pairs
              KnownNamespaces = ns
              StoreConfig = ProvenanceStoreConfig.defaults })
    | Error e, _ -> Error e
    | _, Error e -> Error e

let resolveFromType (t: Type) : Result<ProvenanceConfig, string> = buildConfig t

let resolveGeneratedConfig (assemblies: Assembly[]) : Result<ProvenanceConfig, string> =
    assemblies |> findSinglePublicType "GeneratedProvenance" |> Result.bind buildConfig
```

- [ ] **Step 4: Run, verify PASS.**

- [ ] **Step 5: Fantomas + commit** — `git commit -m "feat(provenance): #330 GeneratedProvenance resolver (fail-closed)"`

### Task 7: `ProvenanceMiddleware.fs`

**Files:**
- Create: `src/Frank.Provenance/ProvenanceMiddleware.fs`
- Test: covered by E2E (Task 1) + a focused capture test in `MiddlewareTests.fs`.

**Interfaces:**
- Consumes: `ProvenanceConfig`, `IProvenanceStore`, `ProvenanceGraph`, `ProducesResponseTypeMetadata`, endpoint route pattern.
- Produces: `type ProvenanceMiddleware(next, config, store, logger) with member InvokeAsync : HttpContext -> Task`.

**Capture algorithm (one job: observe a request and record provenance):**
1. Run `next.Invoke ctx` (handler executes, sets status). For the inline-prov case, buffer the response body first (see Step 3).
2. `let endpoint = ctx.GetEndpoint()`; resource IRI = the matched route pattern raw text (`endpoint.Metadata.GetMetadata<IRouteDiagnosticsMetadata>()` or `ctx.Request.Path`). Use `ctx.Request.Path.Value` for the resource URI — it is the concrete instance IRI (`/orders/1`), which is what PROV wants.
3. Domain type: `open Microsoft.AspNetCore.Http.Metadata`; from `endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>()` pick the entry whose `StatusCode = ctx.Response.StatusCode`; read its `Type.FullName`; `Map.tryFind` in `config.ProvClasses`. Absent ⇒ `DomainType = None`. **Read the interface `IProducesResponseTypeMetadata` (standard ASP.NET), NOT a Frank.OpenApi type — Frank.Provenance has no OpenApi reference.** `Type` may be `typeof<Void>` (the `producesEmpty`/no-type case) → treat as no match → `DomainType = None`.
4. Agent: `ctx.User.Identity.Name` if authenticated, else `"urn:frank:agent:anonymous"`; `ProvAgent.Id` = `"urn:frank:agent:" + name`.
5. Build `ProvenanceRecord` (Id = `"urn:uuid:" + Guid.NewGuid()`; Started/Ended captured around `next`).
6. `store.Append record`.
7. If the request `Accept` contains `profile="http://www.w3.org/ns/prov"` ⇒ replace the response body with `ProvenanceGraph.toJsonLd record` and set `Content-Type: application/ld+json; profile="http://www.w3.org/ns/prov"`.

- [ ] **Step 1: Add a focused capture test to `MiddlewareTests.fs`**
```fsharp
testCaseAsync "records untyped Activity when no produces metadata" <| async {
    use server = startProvenanceServer (orderProvConfig ())
    use client = server.CreateClient()
    let! resp = client.GetAsync("/no-produces") |> Async.AwaitTask
    Expect.equal (int resp.StatusCode) 200 "passes through"
}
```
Add a `/no-produces` GET (status 200, no `WithMetadata`) to `startProvenanceServer`.

- [ ] **Step 2: Run, verify it fails** (route + middleware not present).

- [ ] **Step 3: Implement the middleware.** Precondition guards in `do` block (mirror `ValidationMiddleware`): non-null `config`, non-null `store`. For the inline-prov body swap:
```fsharp
member _.InvokeAsync(ctx: HttpContext) : Task =
    task {
        let wantsProv = ProvNegotiation.requested ctx     // parse Accept for profile=prov
        let started = DateTimeOffset.UtcNow
        let originalBody = ctx.Response.Body
        use buffer = if wantsProv then new MemoryStream() else null
        if wantsProv then ctx.Response.Body <- buffer
        do! next.Invoke ctx
        let ended = DateTimeOffset.UtcNow
        let record = Capture.build config ctx started ended      // steps 2–5, pure-ish
        store.Append record
        if wantsProv then
            ctx.Response.Body <- originalBody
            ctx.Response.ContentType <- "application/ld+json; profile=\"http://www.w3.org/ns/prov\""
            do! ctx.Response.WriteAsync(ProvenanceGraph.toJsonLd record)
        else
            // body already written straight through
            ()
    }
```
Put `ProvNegotiation.requested`, `Capture.build`, and the `ProducesResponseTypeMetadata` lookup in `private module`s above the type so each function does one job (Holzmann 11). `Capture.build` must not exceed 60 lines; split agent/domain-type resolution into helpers.
> **Edge:** when `wantsProv` and the buffered handler wrote nothing, that is fine — we discard the buffer and emit PROV-O. Do **not** copy the buffer to the original stream in the prov branch (we are replacing the representation).

- [ ] **Step 4: Run focused test, verify PASS.**

- [ ] **Step 5: Fantomas + commit** — `git commit -m "feat(provenance): #330 ProvenanceMiddleware capture + content-negotiated inline PROV-O"`

### Task 8: `ProvenanceEndpoint.fs` (query endpoint)

**Files:**
- Create: `src/Frank.Provenance/ProvenanceEndpoint.fs`
- Test: `test/Frank.Provenance.Tests/EndpointTests.fs`

**Interfaces:**
- Produces: `module ProvenanceEndpoint` with `val handle : IProvenanceStore -> HttpContext -> Task` serving `GET /provenance?resource=<iri>` → `ProvenanceGraph.listToJsonLd (store.QueryByResource resource)`. Missing `resource` query param ⇒ 400 problem+json (reuse a small problem+json writer; mirror `ValidationRespond.writeProblemJson` shape).

- [ ] **Step 1: Write the failing test** (`EndpointTests.fs`): start a server with the endpoint mapped, append two records via the store, `GET /provenance?resource=/r` → body contains two `http://www.w3.org/ns/prov#Activity` occurrences (full IRI — see serialization constraint); `GET /provenance` (no param) → 400.

- [ ] **Step 2: Run, verify it fails.**

- [ ] **Step 3: Implement** `ProvenanceEndpoint.handle`.

- [ ] **Step 4: Run, verify PASS.**

- [ ] **Step 5: Fantomas + commit** — `git commit -m "feat(provenance): #330 GET /provenance lineage query endpoint"`

### Task 9: `Frank.Provenance.fs` CE + make E2E (Task 1) green

**Files:**
- Create: `src/Frank.Provenance/Frank.Provenance.fs`
- Modify: `test/Frank.Provenance.Tests/MiddlewareTestHelpers.fs` (remove any hand-stub; use the real middleware + register the query endpoint)

**Interfaces:**
- Consumes: `WebHostSpec`, `Frank.Builder`, `GeneratedProvenanceResolver`, `ProvenanceMiddleware`, `ProvenanceEndpoint`, `IProvenanceStore`.
- Produces: CE ops `useProvenance` (auto-load) / `useProvenanceWith` (explicit), mirroring `Frank.Validation.fs`.

- [ ] **Step 1: Implement `Frank.Provenance.fs`** (mirror `src/Frank.Validation/Frank.Validation.fs`):
  - `useProvenanceWith(spec, config)`: `AddSingleton<ProvenanceConfig>(config)`, `AddSingleton<IProvenanceStore>(store factory)`, `UseMiddleware<ProvenanceMiddleware>()`, and map `GET /provenance` to `ProvenanceEndpoint.handle`.
  - `useProvenance(spec)`: `GeneratedProvenanceResolver.resolveGeneratedConfig (AppDomain.CurrentDomain.GetAssemblies())` → `Ok cfg` ⇒ `TryAddSingleton` + middleware + endpoint; `Error msg` ⇒ `invalidOp msg`.
  - Register the store with `TryAddSingleton<IProvenanceStore>` so an explicit override wins.

- [ ] **Step 2: Build the full package**

```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/provenance-vertical
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build src/Frank.Provenance/Frank.Provenance.fsproj 2>&1 | tail -5
```
Expected: Build succeeded.

- [ ] **Step 3: Run the full Provenance test suite incl. the Task 1 E2E**

```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Provenance.Tests/ 2>&1 | tail -15
```
Expected: all green, including "POST with prov profile returns typed prov:Activity (AC #3)".

- [ ] **Step 4: Fantomas + commit** — `git commit -m "feat(provenance): #330 useProvenance/useProvenanceWith CE; E2E AC #3 green"`

---

## Phase 2 — Build-time emitter + MSBuild

### Task 10: `ProvenanceEmitter.fs`

**Files:**
- Create: `src/Frank.Cli.Core/ProvenanceEmitter.fs`
- Modify: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (add `<Compile>` after `ValidationEmitter.fs`)
- Test: `test/Frank.Cli.Core.Tests/ProvenanceEmitterTests.fs` (+ add `<Compile>` to that fsproj)

**Interfaces:**
- Consumes: `ResolvedModel`, `VocabularyRegistry`, `LockFile`, `AstRender`.
- Produces:
  ```fsharp
  module Frank.Cli.Core.ProvenanceEmitter
  val emit : moduleName:string -> registry:VocabularyRegistry -> lock:LockFile -> Result<string, string>
  ```
  Emits module `GeneratedProvenance` with:
  ```fsharp
  let provClasses : (string * (string * string)) list = [ ... ]
  let knownNamespaces : string[] = [| ... |]
  ```
  Entries: one per `ResolvedResource` with `ProvClass.IsSome`. Value = `(provClass case name, ClassIri.AbsoluteUri or "")`. Key = `ResolvedResource.FSharpType`.

- [ ] **Step 1: Write the failing test** (mirror `test/Frank.Cli.Core.Tests/ValidationEmitterTests.fs`):
```fsharp
module Frank.Cli.Core.Tests.ProvenanceEmitterTests

open Expecto
open Frank.Cli.Core

// Build a registry+lock where a type has provClass Activity AND a class mapping to schema:OrderAction.
// (Reuse the registry/lock construction helpers already used in ValidationEmitterTests.)

[<Tests>]
let tests =
    testList "ProvenanceEmitter" [
        test "emits provClasses entry with case name + class IRI" {
            // arrange registry+lock so OrderPlaced -> provClass Activity, ClassIri schema:OrderAction
            match ProvenanceEmitter.emit "MyApp.GeneratedProvenance" registry lock with
            | Ok src ->
                Expect.stringContains src "module GeneratedProvenance" "module header"
                Expect.stringContains src "Activity" "provClass case rendered"
                Expect.stringContains src "https://schema.org/OrderAction" "class IRI rendered"
                Expect.stringContains src "knownNamespaces" "namespaces emitted"
            | Error e -> failtestf "expected Ok, got %s" e
        }
        test "FCS-typechecks the generated source" {
            // mirror ValidationEmitterTests' typecheck gate (AstRender output must compile).
            ()
        }
    ]
```
> Copy the registry/lock arrangement + the FCS typecheck gate verbatim from `ValidationEmitterTests.fs`. The typecheck gate is mandatory — it is how we prove Fabulous.AST output compiles (the codegen-fabulous-ast rule).

- [ ] **Step 2: Run, verify it fails.**

- [ ] **Step 3: Implement `ProvenanceEmitter.fs`** (mirror `ValidationEmitter.fs` structure; reuse `computeKnownNamespaces`):
```fsharp
module Frank.Cli.Core.ProvenanceEmitter

open Frank.Semantic
open Frank.Semantic.LockFile

let private provClassName (c: ProvOClass) : string =
    match c with
    | ProvOClass.Entity -> "Entity"
    | ProvOClass.Activity -> "Activity"
    | ProvOClass.Agent -> "Agent"

let private entryExpr (r: ResolvedResource) =
    let cls = provClassName r.ProvClass.Value
    let iri = r.ClassIri |> Option.map (fun u -> u.AbsoluteUri) |> Option.defaultValue ""
    AstRender.tupleExpr
        [ AstRender.strExpr r.FSharpType
          AstRender.tupleExpr [ AstRender.strExpr cls; AstRender.strExpr iri ] ]

let private renderModule moduleName (knownNamespaces: string list) (resources: ResolvedResource list) : string =
    let entries = resources |> List.filter (fun r -> r.ProvClass.IsSome) |> List.map entryExpr
    let decls =
        [ AstRender.valueDecl "provClasses" "(string * (string * string)) list" (AstRender.listExpr entries)
          AstRender.valueDecl "knownNamespaces" "string[]" (AstRender.arrayExpr (knownNamespaces |> List.map AstRender.strExpr)) ]
    AstRender.formatModule moduleName (Some AstRender.autoGeneratedHeader) [] decls

let emit (moduleName: string) (registry: VocabularyRegistry) (lock: LockFile) : Result<string, string> =
    let knownNamespaces = ValidationEmitter.computeKnownNamespaces registry  // if private, lift to a shared helper
    AstRender.validateModuleName moduleName
    |> Result.bind (fun () -> ResolvedModel.build registry lock)
    |> Result.map (fun model -> renderModule moduleName knownNamespaces model.Resources)
```
> **Two adaptations to verify against `AstRender.fs`:** (a) a `tupleExpr` (non-applied tuple `("a", ("b","c"))`) helper may not exist — `AstRender` has `tupleAppExpr` (constructor-applied). If a bare-tuple helper is missing, add `let tupleExpr (items) = TupleExpr items` to `AstRender.fs` (one-line, mirrors `listExpr`) in this task and unit-test it. (b) `computeKnownNamespaces` is `private` in `ValidationEmitter`; **do not duplicate it** (constitution rule 8) — lift it to a small shared module (e.g. `AstRender.computeKnownNamespaces` or a new `EmitterShared.fs`) and have both emitters call it. Make that lift its own committed step.
> **No `provClass` entries:** `entries = []` ⇒ emit `provClasses = []` and `knownNamespaces`. Valid; resolver handles the empty map.

- [ ] **Step 4: Run, verify PASS** (both tests, incl. FCS typecheck gate).

- [ ] **Step 5: Fantomas + commit** — `git commit -m "feat(provenance): #325 ProvenanceEmitter (Fabulous.AST) — type->(ProvOClass,IRI) map"`

### Task 11: `GenerateProvenanceTask.fs`

**Files:**
- Create: `src/Frank.Cli.MSBuild/GenerateProvenanceTask.fs`
- Modify: `src/Frank.Cli.MSBuild/Frank.Cli.MSBuild.fsproj` (add `<Compile>` after `GenerateValidationTask.fs`)
- Test: `test/Frank.Cli.MSBuild.Tests/GenerateProvenanceTaskTests.fs` (+ fsproj `<Compile>`)

**Interfaces:**
- Produces: `type GenerateProvenanceTask` (inherits `Task`) with `LockFilePath`, `OutputPath`, `ModuleName`, `SourceFiles`, `AssemblyRefs`, `VocabularyBinding` inputs; `GeneratedFile` output; writes `GeneratedProvenance.fs`.

- [ ] **Step 1: Write the failing test** (mirror `test/Frank.Cli.MSBuild.Tests/GenerateValidationTaskTests.fs`): construct the task with a known lock + sources, `Execute()`, assert `GeneratedProvenance.fs` exists and contains `module GeneratedProvenance`.

- [ ] **Step 2: Run, verify it fails.**

- [ ] **Step 3: Implement** by copying `GenerateValidationTask.fs` with deltas: class/error-message names → Provenance; output filename `GeneratedProvenance.fs`; **call `ProvenanceEmitter.emit this.ModuleName registry lock`** (no `typesByName` — the provenance emitter does not need enriched field types). Drop the `Extractor.extractTypeInfosFromSources` call entirely.

- [ ] **Step 4: Run, verify PASS.**

- [ ] **Step 5: Fantomas + commit** — `git commit -m "feat(provenance): #325 GenerateProvenanceTask MSBuild task"`

### Task 12: MSBuild targets (generate + inject pair)

**Files:**
- Modify: `src/Frank.Cli.MSBuild/build/Frank.Cli.MSBuild.targets`

- [ ] **Step 1: Add the `UsingTask`** for `Frank.Cli.MSBuild.GenerateProvenanceTask` (next to the other `UsingTask` entries).

- [ ] **Step 2: Add `FrankGenerateProvenance` + `FrankInjectGeneratedProvenanceFile`** by copying the `FrankGenerateValidation` / `FrankInjectGeneratedValidationFile` pair verbatim with these substitutions:
  - `Validation` → `Provenance` in target names, property names (`_FrankGeneratedProvenanceFile`, `_FrankGeneratedProvFile`, `_FrankProgramFsProv`, `_FrankLastCompileProv`, `_FrankProvVocabSource`), and module-name default (`$(RootNamespace).GeneratedProvenance`).
  - Package/project condition: `Frank.Provenance`.
  - Output filename: `GeneratedProvenance.fs`.
  - The `GenerateProvenanceTask` invocation keeps `SourceFiles="@(_FrankProvVocabSource)"` and `AssemblyRefs="@(ReferencePath)"` (the task still needs them for `VocabularyEvaluator.evalRegistry`).

- [ ] **Step 3: Verify the targets parse** by building `Frank.Cli.MSBuild` and running `dotnet build-server shutdown` (per `src/CLAUDE.md`, the task DLL is cached):
```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/provenance-vertical
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build src/Frank.Cli.MSBuild/Frank.Cli.MSBuild.fsproj 2>&1 | tail -5
dotnet build-server shutdown
```
Expected: Build succeeded.

- [ ] **Step 4: Commit** — `git commit -m "feat(provenance): #325 MSBuild generate+inject targets for GeneratedProvenance.fs"`

### Task 13: End-to-end MSBuild generation gate (real generated file)

**Files:**
- Test: extend `test/Frank.Cli.MSBuild.Tests/GenerateProvenanceTaskTests.fs` with a fixture project that references `Frank.Provenance` and has a vocab CE with a `provClass`.

- [ ] **Step 1: Add a fixture-based test** that runs the task against a real lock + vocab source declaring `provClass typeof<OrderPlaced> Activity` plus a class mapping to `schema:OrderAction`, and asserts the generated file's `provClasses` contains `("…OrderPlaced", ("Activity", "https://schema.org/OrderAction"))`.
> If a comparable end-to-end fixture exists for Validation (`GenerateValidationTaskTests`), mirror its fixture setup exactly.

- [ ] **Step 2: Run, verify it fails, implement any missing wiring, verify PASS.**

- [ ] **Step 3: Commit** — `git commit -m "test(provenance): #325 e2e MSBuild generation produces real provClasses table"`

---

## Phase 3 — Composition, capstone, full verification

### Task 14: Full solution build + test

- [ ] **Step 1: Build the whole solution**
```bash
cd /Users/ryanr/Code/frank/.claude/worktrees/provenance-vertical
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln 2>&1 | tail -8
```
Expected: Build succeeded, 0 errors.

- [ ] **Step 2: Run the full filtered test suite**
```bash
DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test Frank.sln --filter "FullyQualifiedName!~Sample" 2>&1 | tail -20
```
Expected: all green. Record exact pass counts per project; do not trust any agent-reported count — re-run yourself.

- [ ] **Step 3: Fantomas check**
```bash
dotnet fantomas --check src/
```
Expected: pass. Fix any drift, recommit.

### Task 15: 4-way composition test (#332, spec AC #5)

**Files:**
- Test: `test/Frank.Provenance.Tests/CompositionTests.fs` (or a dedicated composition test project if cross-package references warrant — prefer adding LinkedData/Validation/Discovery project refs to `Frank.Provenance.Tests` only if clean; otherwise create `test/Frank.Composition.Tests/`).

**Interfaces:**
- Asserts: for one resource composing all four packages, the PROV-O Activity `@type` IRI **equals** the LinkedData class IRI for the same mapped type, and the SHACL property path / ALPS descriptor reference the same vocabulary IRIs.

- [ ] **Step 1: Write the failing composition test** — start a TestServer with `useLinkedData`, `useValidation`, `useProvenance`, `useDiscovery` on one resource whose type maps to `schema:OrderAction`; POST with prov profile; assert the Activity `@type` contains the same `https://schema.org/…` IRI that the JSON-LD `@context` / SHACL report use. Decision to surface if blocked: whether to drive this off a shared generated module fixture or a live sample build (recommend fixture for hermeticity).

- [ ] **Step 2: Run, verify it fails; wire; verify PASS.**

- [ ] **Step 3: Commit** — `git commit -m "test(provenance): #332 4-way IRI composition — same field, same IRI everywhere"`

### Task 16: Capstone — wire into a sample (#333, spec AC #6)

**Files:**
- Modify: an existing sample under `sample/` (the tic-tac-toe-style sample used by the other verticals — locate via `find sample -name '*.fsproj'` and the one already referencing `Frank.Discovery`/`Frank.Validation`).
- Modify: that sample's `test-e2e.sh` if present (`find sample/ -name test-e2e.sh`).

> **Composition constraint discovered at Task 15 (must honor here):** when `useProvenance` and `useLinkedData` are composed on one host, `ProvenanceMiddleware` must be registered OUTERMOST (its buffer-and-replace wraps LinkedData's ld+json short-circuit). And because the domain-type lookup matches the response status, a resource SERVED by LinkedData at 200 needs its `produces typeof<T> 200` declaration (not 201). POSTs that create resources (201) live on routes LinkedData does not intercept. Wire the capstone accordingly.

- [ ] **Step 1: Add `useProvenance` to the sample host**, declare `provClass` on the relevant domain type in its vocabulary CE, and ensure the move operation declares `produces typeof<…> <status>` (status matching how the resource is served — see the composition constraint above).

- [ ] **Step 2: Build the sample with the MSBuild generator active**; confirm `obj/.../GeneratedProvenance.fs` is generated and injected (check it compiled). Run `dotnet build-server shutdown` first (cached task DLL).

- [ ] **Step 3: Add/extend the sample E2E** (curl-based) asserting: POST a move with prov profile → PROV-O response with the schema.org-aligned Activity type; `GET /provenance?resource=<game>` → lineage with ≥1 Activity. Run it yourself.

- [ ] **Step 4: Commit** — `git commit -m "feat(provenance): #333 capstone — provenance wired into sample, e2e verified"`

### Task 17: Self-review, discipline, expert review, finalize

- [ ] **Step 1: `/self-reflect`** against this plan + the spec ACs (AT1–AT5). Confirm each AC has observed evidence.

- [ ] **Step 2: `/discipline`** on the changed `src/` files. Fix any rule 9–15 violations (nesting, function length, bounded loops, preconditions). Recommit fixes.

- [ ] **Step 3: `/expert-review`** — dispatch Tim Berners-Lee (Linked Data: are the PROV-O + domain IRIs dereferenceable/consistent, is `@context` correct, should output be COMPACTED vs the current expanded JSON-LD), Darrel Miller (HTTP: content-negotiation profile semantics — **the LinkedData/Provenance `application/ld+json` profile collision found at Task 15**, status codes, problem+json), David Fowler (.NET/ASP.NET: metadata read, **middleware ordering requirement — Provenance must be outermost when composed with LinkedData**, response-body swap correctness), @7sharp9 (F# perf: allocations in the capture hot path, blocking `PostAndReply` in the store/query endpoint, store agent throughput). Treat all findings as potentially blocking; surface to the user — never self-triage.

- [ ] **Step 4: `/simplify`** on the diff; apply in-scope cleanups. **Known finding to resolve here (rule 8):** `writeProblemJson` is duplicated between `src/Frank.Provenance/ProvenanceEndpoint.fs` and `src/Frank.Validation/ValidationMiddleware.fs` (also the `respond400/413/422` family). Extract a shared problem+json writer (candidate home: `Frank.Semantic`, next to `RdfSerialization`) and have both packages consume it.

- [ ] **Step 5: Final full build + test + fantomas** (repeat Task 14 commands). Report verified pass counts with evidence.

- [ ] **Step 6: STOP — do not merge or push.** Present results to the user for merge approval (CLAUDE.md: pushing requires explicit approval; merge sequence is user-gated). Offer the `--ff-only` merge sequence on approval.

---

## Self-Review (plan vs spec)

**Spec coverage:**
- Spec §4 (GeneratedProvenance emitter, conditional gen) → Tasks 10–13. ✓
- Spec §5 Frank.Provenance (consumes GeneratedProvenance, request→Activity/response→Entity/principal→Agent, MailboxProcessorStore retained, no statecharts) → Tasks 2–9. ✓
- Spec AC #3 (typed Activity) → Task 1 + Task 9. ✓
- Spec AC #5 (composition) → Task 15. ✓
- Spec AC #6 (capstone) → Task 16. ✓
- Negative: GeneratedProvenance absent → untyped only → Task 7 Step 1 test + resolver empty-map path (Task 6). ✓
- Lock-gate negative (proposed/unresolved) → shared `ValidateLockFileTask`, already enforced by the existing targets the Provenance pair sits beside (Task 12). ✓
- Graceful degradation (no produces) → Tasks 7, design contract. ✓
- Both exposures (inline + query endpoint) → Tasks 7 (inline) + 8 (query). ✓

**Placeholder scan:** No "TBD"/"handle edge cases" left; the two intentional reorder notes (Task 1 gated behind 2–9) and the two `AstRender` adaptations (Task 10) are explicit, verified-against-source instructions, not deferrals.

**Type consistency:** `ProvenanceConfig.ProvClasses : Map<string, ProvOClass * Uri option>` used consistently across Tasks 2 (def), 6 (resolver builds it), 7 (middleware reads it). Generated map shape `(string * (string * string)) list` consistent across Tasks 6 (resolver consumes), 10 (emitter produces), test fakes. `ProvenanceRecord.DomainType : (ProvOClass * Uri) option` consistent across Tasks 2/4/5/7.

**Open verification items for the executor (surface, do not silently resolve):**
1. `AstRender.tupleExpr` may need adding (Task 10) — confirm against `AstRender.fs`.
2. `ValidationEmitter.computeKnownNamespaces` is `private` — must be lifted to a shared helper, not duplicated (Task 10).
3. `ProducesResponseTypeMetadata` constructor arity/namespace — confirm against `src/Frank.OpenApi/HandlerDefinition.fs:71` (Tasks 1, 7).
4. Resource IRI source in the middleware: `ctx.Request.Path.Value` (instance IRI) vs route template — plan picks instance path; confirm it satisfies AC #5's "same IRI" (the *class* IRI comes from the Activity `@type`, not the subject, so instance path is correct).
