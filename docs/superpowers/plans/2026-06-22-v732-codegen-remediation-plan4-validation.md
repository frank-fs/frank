# v7.3.2 Codegen Remediation — Plan 4: Validation (typed `ShapeDecl` + SHACL interpreter)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the Validation codegen vertical as TYPED artifacts from the start: a `ShapeDecl` DU (illegal SHACL states unrepresentable) in `Frank.Semantic`, a `Shapes.toShapesGraph` SHACL interpreter in a new `Frank.Validation` package (the SINGLE graph-builder — no `buildShapesGraph` duplicate), and a `ValidationEmitter` that emits `let shapes : ShapeDecl list = [...]` via Fabulous.AST.

**Architecture:** `enrichTypes` (cherry-picked) gives each `ResolvedField` its CLR `TypeName`/`IsOptional`/`IsCollection`. `Frank.Semantic` gains `XsdDatatype`/`NonEmptyList`/`PropertyShape`/`ShapeDecl`. `AstRender` gains tupled-DU-application + int builders. A new `Frank.Validation` package holds `Shapes.toShapesGraph : ShapeDecl list -> ShapesGraph` (using `Triples` + dotNetRdf.Shacl) + `ValidationConfig` + `GeneratedValidationResolver`. `ValidationEmitter` projects `ResolvedModel` (enriched) → `ShapeDecl list` → typed source.

**Out of scope (→ Plan 5):** the MSBuild `GenerateValidationTask` + targets + `Extractor.extractTypeInfosFromSources` (build-time wiring), and the `Frank.Validation` runtime middleware/CE (the 422 path — that is the separate V3 runtime work). This plan delivers the typed emitter + interpreter + resolver, unit/tier-tested.

**Tech Stack:** F# net8/9/10, Fabulous.AST 1.10.0, dotNetRdf.Core + dotNetRdf.Shacl 3.5.1, Expecto, FSharp.Compiler.Service.

## Global Constraints

- Worktree root (ABSOLUTE; cwd RESETS between Bash calls): `/Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation`. `cd` to it first in every command; confirm `git branch --show-current` is `v732-codegen-remediation`.
- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` on every `dotnet` command.
- Suites by path: `test/Frank.Cli.Core.Tests` (baseline 171), `test/Frank.Semantic.Tests` (161), new `test/Frank.Validation.Tests`. **Run `dotnet fantomas --check` on BOTH changed `src/` AND `test/` files** before every commit.
- No F# code STRUCTURE via string concatenation; emission via Fabulous.AST. SHACL triples are built in the interpreter via `Triples`, never as emitted `g.Assert` code.
- `Frank.Semantic` stays light: only `dotNetRdf.Core`. `dotNetRdf.Shacl` goes ONLY in `Frank.Validation`.
- Illegal SHACL states unrepresentable: `PropertyShape.Path` required `Uri`; `EnumShape` cases are `NonEmptyList<Uri>` (empty/orphaned `sh:in` impossible); a node shape is EITHER `RecordShape` OR `EnumShape` (total DU); `Datatype` is the closed `XsdDatatype` enum (`None` = no `sh:datatype`).
- Commit after each task with the exact `git add` list.

## Verified Fabulous.AST 1.10.0 API for this plan (run-confirmed 2026-06-22)

```fsharp
// Tupled DU-case application: Ctor(a, b)
AppExpr("RecordShape", ParenExpr(TupleExpr [ argA; argB ]))   // → RecordShape(<a>, <b>)
ConstantExpr "1"                  // → 1   (int literal token)
AppExpr("Some", ConstantExpr "XsdInteger")   // → Some XsdInteger
AppExpr("Some", ConstantExpr "1")            // → Some 1
ConstantExpr "None"               // → None
AppExpr("System.Uri", ConstantExpr(String iri))   // → System.Uri "<iri>"
RecordExpr [ RecordFieldExpr("Head", e); RecordFieldExpr("Tail", ListExpr [...]) ]  // NonEmptyList literal
// (Plan 1-3 AstRender already provides strExpr/recordExpr/listExpr/appExpr/rawExpr/parenExpr/valueDecl/formatModule/ModuleDeclItem.)
```

## File Structure

- **Cherry-pick** `169fe69d` → `src/Frank.Semantic/ResolvedModel.fs` (+`enrichTypes`) and its test.
- **Modify** `src/Frank.Semantic/OntologyTypes.fs` (or new `ShapeTypes.fs`) — add `XsdDatatype`/`NonEmptyList`/`PropertyShape`/`ShapeDecl`. (Prefer extending the existing types file or a focused new `ShapeTypes.fs` after it.)
- **Modify** `src/Frank.Cli.Core/AstRender.fs` + tests — `tupleAppExpr`, `intExpr`, `someExpr`.
- **Create** `src/Frank.Validation/` — `Frank.Validation.fsproj` (dotNetRdf.Shacl + Frank.Semantic ref), `ValidationTypes.fs`, `Shapes.fs` (interpreter), `GeneratedValidationResolver.fs`. Add to `Frank.sln`.
- **Create** `test/Frank.Validation.Tests/` — `Shapes` conformance tests + resolver test.
- **Create** `src/Frank.Cli.Core/ValidationEmitter.fs` (added to fsproj after `LinkedDataEmitter.fs`) + `test/Frank.Cli.Core.Tests/ValidationEmitterTests.fs`.

---

### Task 1: Cherry-pick `enrichTypes` (`ResolvedField` type/cardinality)

**Files:** `src/Frank.Semantic/ResolvedModel.fs`, `test/Frank.Semantic.Tests/ResolvedModelTests.fs` (via cherry-pick).

- [ ] **Step 1: Cherry-pick.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && git cherry-pick -x 169fe69d`
If it applies cleanly → go to Step 3. If it CONFLICTS (Plan 3 did not touch `ResolvedField`, so conflicts are unlikely but possible if line-adjacent): resolve by KEEPING both the existing `ResolvedField` fields AND adding the three new ones (`TypeName: string`, `IsOptional: bool`, `IsCollection: bool`), and the `enrichTypes`/`classifyType`/`enrichField`/`enrichRecordFields`/`enrichResource` functions from the commit. `git cherry-pick --continue` after `git add`.

- [ ] **Step 2 (only if cherry-pick is impossible): re-apply manually.** `git cherry-pick --abort`, then add to `ResolvedField` the three fields (defaulted to `""`/`false`/`false` in `ResolvedModel.build`'s construction) and add `enrichTypes (typesByName: Map<string,TypeInfo>) (model: ResolvedModel) : Result<ResolvedModel,string>` per commit `169fe69d` (use `git show 169fe69d` as the exact source).

- [ ] **Step 3: Build + Semantic + Cli.Core suites.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/`
Expected: Semantic 161 + N (the enrichTypes tests from the commit) pass; **Cli.Core 171 UNCHANGED** (build signature unchanged → no caller churn; the 3 new fields default in `build`).

- [ ] **Step 4: Fantomas + (commit only if cherry-pick was aborted+manual — otherwise the cherry-pick already committed).**

If you re-applied manually: `dotnet fantomas src/Frank.Semantic/ResolvedModel.fs` then
```bash
git add src/Frank.Semantic/ResolvedModel.fs test/Frank.Semantic.Tests/ResolvedModelTests.fs
git commit -m "feat(semantic): ResolvedField type/cardinality via enrichTypes (cherry-pick 169fe69d)"
```

---

### Task 2: `Frank.Semantic` — `XsdDatatype`/`NonEmptyList`/`ShapeDecl`

**Files:** Create `src/Frank.Semantic/ShapeTypes.fs` (fsproj after `OntologyTypes.fs`); test `test/Frank.Semantic.Tests/ShapeTypesTests.fs`.

**Interfaces — Produces:**
```fsharp
namespace Frank.Semantic
open System
type NonEmptyList<'T> = { Head: 'T; Tail: 'T list }
module NonEmptyList =
    val ofList : 'T list -> NonEmptyList<'T> option
    val toList : NonEmptyList<'T> -> 'T list
type XsdDatatype = | XsdInteger | XsdLong | XsdDecimal | XsdDouble | XsdBoolean | XsdString | XsdDateTime
type PropertyShape =
    { Path: Uri; Datatype: XsdDatatype option; MinCount: int; MaxCount: int option; Pattern: string option }
type ShapeDecl =
    | RecordShape of targetClass: Uri * properties: PropertyShape list
    | EnumShape of targetClass: Uri * cases: NonEmptyList<Uri>
```

- [ ] **Step 1: Failing test** `ShapeTypesTests.fs`:

```fsharp
module Frank.Semantic.Tests.ShapeTypesTests
open System
open Expecto
open Frank.Semantic

[<Tests>]
let tests =
    testList "ShapeTypes" [
        test "NonEmptyList.ofList None on empty, Some on non-empty; toList round-trips" {
            Expect.isNone (NonEmptyList.ofList ([]: int list)) "empty → None"
            let nel = NonEmptyList.ofList [ 1; 2; 3 ] |> Option.get
            Expect.equal (NonEmptyList.toList nel) [ 1; 2; 3 ] "round-trip"
            Expect.equal nel.Head 1 "head"
        }
        test "ShapeDecl is a total DU over RecordShape | EnumShape" {
            let r = RecordShape(Uri "https://schema.org/MoveAction",
                                [ { Path = Uri "https://schema.org/position"; Datatype = Some XsdInteger; MinCount = 1; MaxCount = Some 1; Pattern = None } ])
            let e = EnumShape(Uri "https://schema.org/Status", { Head = Uri "https://schema.org/Active"; Tail = [] })
            let describe = function RecordShape _ -> "record" | EnumShape _ -> "enum"
            Expect.equal (describe r) "record" "record case"
            Expect.equal (describe e) "enum" "enum case"
        }
    ]
```

- [ ] **Step 2: Run — fails.** Run: `... dotnet test test/Frank.Semantic.Tests/ --filter "ShapeTypes"`

- [ ] **Step 3: Implement `ShapeTypes.fs`.**

```fsharp
namespace Frank.Semantic
open System

/// Non-empty by construction — an empty/orphaned sh:in list is unrepresentable.
type NonEmptyList<'T> = { Head: 'T; Tail: 'T list }
module NonEmptyList =
    let ofList = function [] -> None | x :: xs -> Some { Head = x; Tail = xs }
    let toList n = n.Head :: n.Tail

/// Closed set of xsd datatypes Frank maps F# primitives to. An arbitrary datatype IRI is unrepresentable.
type XsdDatatype =
    | XsdInteger | XsdLong | XsdDecimal | XsdDouble | XsdBoolean | XsdString | XsdDateTime

/// One property constraint. Path required (no pathless property shape).
type PropertyShape =
    { Path: Uri
      Datatype: XsdDatatype option   // None = domain type, no sh:datatype
      MinCount: int                  // 0 optional · 1 required
      MaxCount: int option           // None = unbounded (collection)
      Pattern: string option }

/// A node shape is EITHER a record OR a nullary-union enum — never both, never neither.
type ShapeDecl =
    | RecordShape of targetClass: Uri * properties: PropertyShape list
    | EnumShape of targetClass: Uri * cases: NonEmptyList<Uri>
```

Add `<Compile Include="ShapeTypes.fs" />` after `OntologyTypes.fs`. Add the test to the Semantic test fsproj.

- [ ] **Step 4: Run — passes. Step 5: full Semantic suite + fantomas + commit.**
```bash
git add src/Frank.Semantic/ShapeTypes.fs src/Frank.Semantic/Frank.Semantic.fsproj test/Frank.Semantic.Tests/ShapeTypesTests.fs test/Frank.Semantic.Tests/*.fsproj
git commit -m "feat(semantic): XsdDatatype + NonEmptyList + ShapeDecl (illegal SHACL states unrepresentable)"
```

---

### Task 3: `AstRender` — tupled-DU-application + int builders

**Files:** `src/Frank.Cli.Core/AstRender.fs` + `test/Frank.Cli.Core.Tests/AstRenderTests.fs`.

**Interfaces — Produces:**
- `AstRender.intExpr : int -> WidgetBuilder<Expr>`
- `AstRender.someExpr : WidgetBuilder<Expr> -> WidgetBuilder<Expr>` (→ `Some <e>`)
- `AstRender.tupleAppExpr : ctor:string -> args:WidgetBuilder<Expr> list -> WidgetBuilder<Expr>` (→ `Ctor(a, b, ...)`)

- [ ] **Step 1: Failing round-trip test** in `AstRenderTests.fs`:

```fsharp
test "tupleAppExpr renders a tupled DU-case application; intExpr/someExpr render" {
    let e =
        AstRender.tupleAppExpr "RecordShape"
            [ AstRender.uriExpr "https://schema.org/MoveAction"
              AstRender.listExpr [ AstRender.intExpr 1; AstRender.someExpr (AstRender.rawExpr "XsdInteger") ] ]
    let src = AstRender.formatModule "T.GeneratedValidation" None [] [ AstRender.valueDecl "x" "ShapeDecl" e ]
    Expect.stringContains src "RecordShape(System.Uri \"https://schema.org/MoveAction\"," "tupled ctor application"
    Expect.stringContains src "[ 1; Some XsdInteger ]" "int + Some-enum in list"
}
```
> `uriExpr` already renders `System.Uri "..."` (it is `AppExpr("System.Uri", ...)` in this codebase — confirm; if `uriExpr` renders `Uri "..."` without the `System.` prefix, the expected substring is `Uri "https://...`). Print the emitted source and assert reality; do not guess the prefix.

- [ ] **Step 2: Run — fails. Step 3: implement.** Append to `AstRender.fs`:

```fsharp
/// An integer literal expression.
let intExpr (n: int) : WidgetBuilder<Expr> = ConstantExpr(string n)

/// Some <e>
let someExpr (e: WidgetBuilder<Expr>) : WidgetBuilder<Expr> = AppExpr("Some", e)

/// A tupled discriminated-union case application: Ctor(a, b, ...)
let tupleAppExpr (ctor: string) (args: WidgetBuilder<Expr> list) : WidgetBuilder<Expr> =
    AppExpr(ctor, ParenExpr(TupleExpr args))
```

- [ ] **Step 4: Run — passes. Step 5: fantomas + commit.**
```bash
git add src/Frank.Cli.Core/AstRender.fs test/Frank.Cli.Core.Tests/AstRenderTests.fs
git commit -m "feat(cli): AstRender tupleAppExpr/intExpr/someExpr (ShapeDecl emission)"
```

---

### Task 4: `Frank.Validation` package — `Shapes.toShapesGraph` interpreter + resolver

**Files:** Create `src/Frank.Validation/{Frank.Validation.fsproj,ValidationTypes.fs,Shapes.fs,GeneratedValidationResolver.fs}`; add to `Frank.sln`; create `test/Frank.Validation.Tests/{Frank.Validation.Tests.fsproj,Program.fs,ShapesTests.fs,ResolverTests.fs}`.

**Read first:** `src/Frank.LinkedData/{Frank.LinkedData.fsproj,GeneratedLinkedDataResolver.fs}` and the `Frank.GeneratedModuleReflection` `readStaticProp`/`findSinglePublicType` helpers the LinkedData resolver uses — clone that structure.

**Interfaces — Produces:**
```fsharp
// Frank.Validation
type ValidationConfig = { Shapes: VDS.RDF.Shacl.ShapesGraph }
module Shapes =
    /// THE single place SHACL triples are built. Total over ShapeDecl, correct by construction.
    val toShapesGraph : Frank.Semantic.ShapeDecl list -> VDS.RDF.Shacl.ShapesGraph
module GeneratedValidationResolver =
    val resolveFromType : System.Type -> Result<ValidationConfig, string>
    val resolveGeneratedConfig : System.Reflection.Assembly[] -> Result<ValidationConfig, string>
```

- [ ] **Step 1: Failing SHACL conformance tests** `test/Frank.Validation.Tests/ShapesTests.fs`:

```fsharp
module Frank.Validation.Tests.ShapesTests
open System
open Expecto
open Frank.Semantic
open Frank.Validation
open VDS.RDF

let private dataGraph (classIri: string) (instanceIri: string) : IGraph =
    let g = new Graph() :> IGraph
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
    let inst = g.CreateUriNode(UriFactory.Create instanceIri)
    let rdfType = g.CreateUriNode(g.ResolveQName "rdf:type")
    g.Assert(Triple(inst, rdfType, g.CreateUriNode(UriFactory.Create classIri))) |> ignore
    g

[<Tests>]
let tests =
    testList "Shapes.toShapesGraph" [
        test "nullary-union sh:in: focus node in list conforms (well-formed list, no RdfException)" {
            let shapes =
                [ EnumShape(Uri "https://schema.org/GameStatusType",
                            { Head = Uri "https://schema.org/ActiveActionStatus"
                              Tail = [ Uri "https://schema.org/CompletedActionStatus" ] }) ]
            let sg = Shapes.toShapesGraph shapes
            let report = sg.Validate(dataGraph "https://schema.org/GameStatusType" "https://schema.org/ActiveActionStatus")
            Expect.isTrue report.Conforms "focus node present in sh:in list conforms"
        }
        test "nullary-union sh:in rejects focus node absent from the list" {
            let shapes =
                [ EnumShape(Uri "https://schema.org/GameStatusType",
                            { Head = Uri "https://schema.org/ActiveActionStatus"; Tail = [] }) ]
            let sg = Shapes.toShapesGraph shapes
            let report = sg.Validate(dataGraph "https://schema.org/GameStatusType" "https://schema.org/UnknownStatus")
            Expect.isFalse report.Conforms "focus node absent from sh:in does not conform"
        }
        test "record shape with required int property: missing property does not conform" {
            let shapes =
                [ RecordShape(Uri "https://schema.org/MoveAction",
                              [ { Path = Uri "https://schema.org/position"; Datatype = Some XsdInteger; MinCount = 1; MaxCount = Some 1; Pattern = None } ]) ]
            let sg = Shapes.toShapesGraph shapes
            let report = sg.Validate(dataGraph "https://schema.org/MoveAction" "https://example.org/move1")
            Expect.isFalse report.Conforms "missing required position → non-conforming (no RdfException)"
        }
    ]
```

- [ ] **Step 2: Run — fails** (package/types missing). Run: `... dotnet test test/Frank.Validation.Tests/ --filter "Shapes"` (after creating the projects in Step 3).

- [ ] **Step 3: Create `Frank.Validation`.**

`Frank.Validation.fsproj` — clone `Frank.LinkedData.fsproj`: same `TargetFrameworks` net8/9/10, `<ProjectReference Include="../Frank.Semantic/Frank.Semantic.fsproj" />`, `<PackageReference Include="dotNetRdf.Core" Version="3.5.1" />` AND **`<PackageReference Include="dotNetRdf.Shacl" Version="3.5.1" />`** (SHACL `ShapesGraph`/`Report`/`Validate` live here, separate from Core). Compile order: `ValidationTypes.fs`, `Shapes.fs`, `GeneratedValidationResolver.fs`.

`ValidationTypes.fs`:
```fsharp
namespace Frank.Validation
type ValidationConfig = { Shapes: VDS.RDF.Shacl.ShapesGraph }
```

`Shapes.fs` — the interpreter. Build SHACL triples via `Frank.Semantic.Triples`, then wrap in a `ShapesGraph`. For `RecordShape`: a `sh:NodeShape` with `sh:targetClass`; per `PropertyShape` a blank-node `sh:property` with `sh:path`, optional `sh:datatype` (map `XsdDatatype` → `xsd:` qname), `sh:minCount`, optional `sh:maxCount`, optional `sh:pattern`. For `EnumShape`: a `sh:NodeShape` + `sh:targetClass` + `sh:in` pointing to a well-formed RDF list (`rdf:first`/`rdf:rest`/`rdf:nil`) of the case IRIs — the head of `sh:in` MUST equal the first list cell (the orphaned-list bug). Use deterministic blank-node labels (`bn_{i}_...`). Skeleton:

```fsharp
module Frank.Validation.Shapes
open System
open VDS.RDF
open VDS.RDF.Shacl
open Frank.Semantic

let private xsd = function
    | XsdInteger -> "xsd:integer" | XsdLong -> "xsd:long" | XsdDecimal -> "xsd:decimal"
    | XsdDouble -> "xsd:double" | XsdBoolean -> "xsd:boolean" | XsdString -> "xsd:string" | XsdDateTime -> "xsd:dateTime"

let private intLit (g: IGraph) (n: int) : INode =
    g.CreateLiteralNode(string n, UriFactory.Create "http://www.w3.org/2001/XMLSchema#integer")

let private addProperty (g: IGraph) (classNode: INode) (ri: int) (pi: int) (p: PropertyShape) : unit =
    let bn = g.CreateBlankNode(sprintf "bn_%d_%d" ri pi)
    Triples.assert3 g classNode (Triples.qnameNode g "sh:property") bn
    Triples.assert3 g bn (Triples.qnameNode g "sh:path") (Triples.uriNode g p.Path.AbsoluteUri)
    p.Datatype |> Option.iter (fun d -> Triples.assert3 g bn (Triples.qnameNode g "sh:datatype") (Triples.qnameNode g (xsd d)))
    Triples.assert3 g bn (Triples.qnameNode g "sh:minCount") (intLit g p.MinCount)
    p.MaxCount |> Option.iter (fun m -> Triples.assert3 g bn (Triples.qnameNode g "sh:maxCount") (intLit g m))
    p.Pattern |> Option.iter (fun pat -> Triples.assert3 g bn (Triples.qnameNode g "sh:pattern") (g.CreateLiteralNode pat))

let private addRdfList (g: IGraph) (ri: int) (members: INode list) : INode =
    // returns the head node; builds rdf:first/rdf:rest/rdf:nil with bn_{ri}_in_{idx}
    let rdfFirst = Triples.qnameNode g "rdf:first"
    let rdfRest = Triples.qnameNode g "rdf:rest"
    let rdfNil = Triples.uriNode g "http://www.w3.org/1999/02/22-rdf-syntax-ns#nil"
    let count = List.length members
    let cell i = g.CreateBlankNode(sprintf "bn_%d_in_%d" ri i)
    members |> List.iteri (fun i m ->
        let c = cell i
        Triples.assert3 g c rdfFirst m
        Triples.assert3 g c rdfRest (if i = count - 1 then rdfNil else cell (i + 1)))
    cell 0

let private addShape (g: IGraph) (ri: int) (shape: ShapeDecl) : unit =
    let classIri = match shape with RecordShape(c, _) | EnumShape(c, _) -> c.AbsoluteUri
    let classNode = Triples.uriNode g classIri
    Triples.assert3 g classNode (Triples.qnameNode g "rdf:type") (Triples.qnameNode g "sh:NodeShape")
    Triples.assert3 g classNode (Triples.qnameNode g "sh:targetClass") classNode
    match shape with
    | RecordShape(_, props) -> props |> List.iteri (fun pi p -> addProperty g classNode ri pi p)
    | EnumShape(_, cases) ->
        let members = NonEmptyList.toList cases |> List.map (fun u -> Triples.uriNode g u.AbsoluteUri)
        let head = addRdfList g ri members
        Triples.assert3 g classNode (Triples.qnameNode g "sh:in") head

let toShapesGraph (shapes: ShapeDecl list) : ShapesGraph =
    let g = new Graph() :> IGraph
    g.NamespaceMap.AddNamespace("sh", UriFactory.Create "http://www.w3.org/ns/shacl#")
    g.NamespaceMap.AddNamespace("xsd", UriFactory.Create "http://www.w3.org/2001/XMLSchema#")
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
    shapes |> List.iteri (fun ri s -> addShape g ri s)
    ShapesGraph(g)
```
> Verify the exact dotNetRdf 3.5.1 SHACL API while implementing: `ShapesGraph(IGraph)` ctor, `.Validate(IGraph) : Report`, `Report.Conforms : bool`. Adjust to the real API; do NOT stub. This `Shapes.toShapesGraph` is the SINGLE SHACL graph-builder — there is no `buildShapesGraph` anywhere (it never gets created in this remediation).

`GeneratedValidationResolver.fs` — clone `GeneratedLinkedDataResolver`, reading static `shapesGraph : ShapesGraph`:
```fsharp
namespace Frank.Validation
open System
open System.Reflection
open VDS.RDF.Shacl
open Frank.GeneratedModuleReflection
module GeneratedValidationResolver =
    let private buildConfig (t: Type) : Result<ValidationConfig, string> =
        match readStaticProp<ShapesGraph> "shapesGraph" t with
        | Ok s -> Ok { Shapes = s }
        | Error e -> Error e
    let resolveFromType (t: Type) : Result<ValidationConfig, string> = buildConfig t
    let resolveGeneratedConfig (assemblies: Assembly[]) : Result<ValidationConfig, string> =
        assemblies |> findSinglePublicType "GeneratedValidation" |> Result.bind buildConfig
```
> Confirm the `Frank.GeneratedModuleReflection` module/namespace path the LinkedData resolver opens, and reference it the same way (it is a shared reflection helper).

Add the project to `Frank.sln` (`dotnet sln Frank.sln add src/Frank.Validation/Frank.Validation.fsproj`). Create `test/Frank.Validation.Tests` mirroring `test/Frank.LinkedData.Tests` (Program.fs Expecto entry, fsproj referencing Frank.Validation + dotNetRdf.Shacl + Frank.Semantic). Add a `ResolverTests.fs` mirroring the LinkedData resolver test (a stub type exposing `static member shapesGraph` → resolves; an empty type → Error).

- [ ] **Step 4: Run — passes.** `... dotnet test test/Frank.Validation.Tests/`. Expected: Shapes conformance + resolver tests PASS.

- [ ] **Step 5: sln build + fantomas + commit.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet build Frank.sln && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Validation.Tests/ && dotnet fantomas --check src/Frank.Validation/ test/Frank.Validation.Tests/`
```bash
git add src/Frank.Validation/ test/Frank.Validation.Tests/ Frank.sln
git commit -m "feat(validation): Frank.Validation — Shapes.toShapesGraph SHACL interpreter + resolver (no buildShapesGraph)"
```

---

### Task 5: `ValidationEmitter` — project enriched model → `ShapeDecl`, emit via AstRender

**Files:** Create `src/Frank.Cli.Core/ValidationEmitter.fs` (fsproj after `LinkedDataEmitter.fs`); test `test/Frank.Cli.Core.Tests/ValidationEmitterTests.fs`.

**Interfaces:**
- Consumes: `AstRender.*` (tupleAppExpr/intExpr/someExpr/uriExpr/recordExpr/listExpr/rawExpr/valueDecl/formatModule); `Frank.Semantic.{ShapeDecl,PropertyShape,XsdDatatype,NonEmptyList,enrichTypes}`; `FcsTypecheck.typecheckTwoSources`.
- Produces: `ValidationEmitter.emit : moduleName:string -> registry:VocabularyRegistry -> lock:LockFile -> typesByName:Map<string,TypeInfo> -> Result<string,string>`; internal `projectShapes : ResolvedModel -> Result<ShapeDecl list, string>`.

**Notes:** A record resource → `RecordShape(classIri, props)` where each field-with-Iri yields a `PropertyShape` (`Datatype` from the field's enriched `TypeName` via an `xsdOf` map; `MinCount = if IsOptional then 0 else 1`; `MaxCount = if IsCollection then None else Some 1`; `Pattern` from `registry.ConstraintPatterns`). A nullary-union resource (all cases nullary, each with an IRI) → `EnumShape(classIri, NonEmptyList of case IRIs)`. **Fail-closed:** a shaped record field with `Iri = Some` but empty `TypeName` (never enriched) → `Error` (never a guessed datatype, never `urn:frank:`). Union payload-field datatypes are OUT of scope (nullary `sh:in` only). Pipeline: `ResolvedModel.build registry lock |> Result.bind (enrichTypes typesByName) |> Result.bind projectShapes |> Result.map (renderShapes moduleName)`.

- [ ] **Step 1: Failing tier-1 + tier-3 tests** `ValidationEmitterTests.fs` (build a TicTacToe-shaped registry/lock + `typesByName` with a record field `position:int`, an `Option<string>`, a `string list`, and a nullary-union `Status`; use the REAL normalized `TypeName` strings — confirm via a quick `Extractor`/print or reuse the strings the existing Semantic enrichTypes tests use):

```fsharp
test "projectShapes yields a RecordShape with datatype/cardinality from enriched types (tier 1)" {
    let shapes = ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName |> okOrFail
    Expect.stringContains shapes "RecordShape(System.Uri \"https://schema.org/MoveAction\"" "record node shape"
    Expect.stringContains shapes "Some XsdInteger" "int → XsdInteger"
    Expect.stringContains shapes "EnumShape(System.Uri \"https://schema.org/GameStatusType\"" "nullary union → EnumShape"
    Expect.isFalse (shapes.Contains "urn:frank:") "no synthetic URIs"
}
test "fail-closed: shaped field with no type info → Error" {
    match ValidationEmitter.emit "T.GeneratedValidation" registry lock Map.empty with
    | Error _ -> () | Ok _ -> failtest "expected Error when a shaped field has no enriched type"
}
test "emitted GeneratedValidation compiles against Frank.Semantic/Frank.Validation (tier 3)" {
    let src = ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName |> okOrFail
    let domainSrc = // stand-ins for ShapeDecl/PropertyShape/XsdDatatype/NonEmptyList (Frank.Semantic) + Shapes.toShapesGraph (Frank.Validation) + a stub ShapesGraph type
        "..." // match the REAL field names/signatures (Task 2 + Task 4); mirror the LinkedData tier-3 stub approach
    Expect.isEmpty (FcsTypecheck.typecheckTwoSources domainSrc src) "emitted Validation module compiles"
}
test "deterministic: two emits byte-identical" {
    let a = ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
    let b = ValidationEmitter.emit "T.GeneratedValidation" registry lock typesByName
    Expect.equal a b "deterministic"
}
```

- [ ] **Step 2: Run — fails. Step 3: implement `ValidationEmitter.fs`.** Projection + `xsdOf : string -> XsdDatatype option` keyed on the REAL enriched `TypeName` strings (confirm them — they are normalized FCS forms like `int`, `string`, etc.; reuse what `enrichTypes`/`classifyType` produce). Render: `RecordShape`/`EnumShape` via `AstRender.tupleAppExpr`; `PropertyShape` via `recordExpr` (Path=`uriExpr`, Datatype=`someExpr (rawExpr "XsdInteger")`/`rawExpr "None"`, MinCount=`intExpr`, MaxCount=`someExpr (intExpr 1)`/`rawExpr "None"`, Pattern=`someExpr (strExpr pat)`/`rawExpr "None"`); the `shapes` value via `valueDecl "shapes" "ShapeDecl list" (listExpr ...)` and `valueDecl "shapesGraph" "VDS.RDF.Shacl.ShapesGraph" (appExpr "Shapes.toShapesGraph" (rawExpr "shapes"))`; opens `[ "Frank.Semantic"; "Frank.Validation" ]`. Add `<Compile Include="ValidationEmitter.fs" />` after `LinkedDataEmitter.fs`.

- [ ] **Step 4: Run — passes. Step 5: full Cli.Core suite + fantomas + commit.**
```bash
git add src/Frank.Cli.Core/ValidationEmitter.fs src/Frank.Cli.Core/Frank.Cli.Core.fsproj test/Frank.Cli.Core.Tests/ValidationEmitterTests.fs
git commit -m "feat(cli): ValidationEmitter emits typed ShapeDecl via AstRender (no string concat, no buildShapesGraph)"
```

---

## Self-Review

- **Spec coverage:** delivers the Validation typed artifact (spec: `ShapeDecl` in `Frank.Semantic`, `Shapes.toShapesGraph` interpreter in `Frank.Validation`, emitter via Fabulous.AST). `buildShapesGraph` is never created — the interpreter is the sole SHACL builder (spec AC #3, structurally). Illegal SHACL states unrepresentable (AC #2). Fail-closed datatype (no urn:frank:). Reuses Plan 3's `Triples`.
- **Placeholder scan:** the tier-3 `domainSrc` (Task 5 Step 1) is described, not written — the implementer constructs it to match the real Task 2/4 signatures (same approach proven in Plan 3 Task 4). The `xsdOf` real-TypeName strings must be confirmed against `enrichTypes` output, not guessed.
- **Type consistency:** `ShapeDecl`/`PropertyShape`/`XsdDatatype`/`NonEmptyList` identical across Task 2 (def), Task 4 (interpreter), Task 5 (emitter literal). `Shapes.toShapesGraph` signature consistent Task 4 ↔ Task 5.

## Next — Plan 5 (MSBuild wiring, deferred)

`GenerateValidationTask` + `Extractor.extractTypeInfosFromSources` (supply `typesByName` at build time) + `FrankGenerateValidation`/`FrankInjectGeneratedValidationFile` targets + FCS compile gate in `Frank.Cli.MSBuild.Tests`. Then wire the sample (`useValidation`) — the V3 runtime middleware/CE/422 path is separate runtime work.
