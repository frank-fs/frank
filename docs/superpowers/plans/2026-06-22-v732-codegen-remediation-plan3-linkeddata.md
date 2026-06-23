# v7.3.2 Codegen Remediation — Plan 3: LinkedData typed `OntologyDecl`

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Migrate `LinkedDataEmitter` from emitting imperative `g.Assert(...)` graph-construction code to emitting a **typed `OntologyDecl` value** interpreted into an `IGraph` by library code in `Frank.LinkedData`. The OWL/RDFS shapes become illegal-state-resistant typed data; the emitter builds them via Fabulous.AST.

**Architecture:** `Frank.Semantic` gains the data DUs (`OntologyDecl`/`ClassDecl`/`PropertyDecl`) + a generic triple-assertion helper (`Triples`). `Frank.LinkedData` gains an `Ontology` interpreter (`toGraph`/`toJsonLdContext`) and a `Frank.Semantic` reference. `AstRender` gains `valueDecl` (a plain `let name: T = expr` module declaration). `LinkedDataEmitter` projects `ResolvedModel → OntologyDecl`, then emits `let ontology : OntologyDecl = {...}` + `let graph = Ontology.toGraph ontology` + `let jsonLdContext = Ontology.toJsonLdContext ontology`. The existing resolver (reads static `graph`/`jsonLdContext`) is unchanged.

**Tech Stack:** F# net8/9/10, Fabulous.AST 1.10.0, dotNetRdf.Core 3.5.1, Expecto, FSharp.Compiler.Service.

## Global Constraints

- Worktree root (ABSOLUTE; cwd RESETS between Bash calls): `/Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation`. `cd` to it first in every command; confirm `git branch --show-current` is `v732-codegen-remediation`.
- `DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1` on every `dotnet` command.
- Suites by path: `test/Frank.Cli.Core.Tests` (baseline 167), `test/Frank.Semantic.Tests`, `test/Frank.LinkedData.Tests`. **Run `dotnet fantomas --check` on BOTH the changed `src/` files AND the changed `test/` files** before each commit (the pre-commit hook + merge sequence require fantomas-clean; test files are easy to forget).
- No F# CODE STRUCTURE assembled by string concatenation; emission via Fabulous.AST. Leaf tokens (IRIs, type names) as strings fed to widgets are fine. The `Ontology.toJsonLdContext` builds a JSON `@context` STRING at runtime (that is runtime data, not codegen) — allowed.
- `Frank.Semantic` must stay light: it may reference only `dotNetRdf.Core` (already does). The `Triples` helper uses `VDS.RDF.IGraph`. Do NOT add `dotNetRdf.Shacl` to `Frank.Semantic`.
- Illegal states unrepresentable: `PropertyDecl.Domain` and `.Iri` are required (`Uri`, not option) — no dangling `rdfs:domain`; `ClassDecl.Iri` required.
- Commit after each task with the exact `git add` list.

## Reference facts (verified from the worktree)

- `LinkedDataConfig = { Graph: IGraph; JsonLdContext: string }`; `GeneratedLinkedDataResolver` reads static `graph: IGraph` and `jsonLdContext: string` — the generated module MUST keep those two bindings.
- `ResolvedResource`: `ClassIri: Uri option`, `EquivalentClass: Uri option`, `SeeAlso: Uri list`, `Fields: ResolvedField list` (each `Iri: Uri option`). `ResolvedModel`: `Prefixes: Map<string,Uri>`, `Using: Set<string>`.
- The current `LinkedDataEmitter` (string-based) emits, per class with a ClassIri: `rdf:type owl:Class`, optional `owl:equivalentClass`, `rdfs:seeAlso` per SeeAlso, and per field-with-Iri `rdf:type rdf:Property` + `rdfs:domain <class>`. `jsonLdContext` = `{"@context":[<using base IRIs, trailing-slash-trimmed>]}`.
- `Frank.LinkedData` compile order: `LinkedDataTypes.fs`, `LinkedDataMiddleware.fs`, `GeneratedLinkedDataResolver.fs`, `Frank.LinkedData.fs`.
- `Frank.Semantic` compile order ends: `Mapping.fs`, `LockFile.fs`, `ResolvedModel.fs`, `ConventionEngine.fs`, `VocabFetcher.fs`.
- AstRender (Plan 1+2) exposes: `strExpr`, `noneExpr`, `someStrExpr`, `uriExpr` (→ `Uri "x"`), `recordExpr`, `listExpr`, `appExpr`, `rawExpr`, `parenExpr`, `unionDecl`, `matchFunction`, `formatTypedValueModule`, `formatModule : moduleName -> leadingComment:string option -> opens:string list -> decls:ModuleDeclItem list -> string`, and the `ModuleDeclItem` DU (`UnionDecl`/`BindingDecl`).

## File Structure

- **Modify** `src/Frank.Semantic/ResolvedModel.fs` (or a new `src/Frank.Semantic/Ontology.fs` added to the fsproj after `ResolvedModel.fs`) — add `PropertyDecl`/`ClassDecl`/`OntologyDecl` types + a `Triples` helper module. (Prefer a new `OntologyTypes.fs` to keep `ResolvedModel.fs` focused.)
- **Modify** `src/Frank.Semantic/Frank.Semantic.fsproj` — add the new file.
- **Modify** `src/Frank.Cli.Core/AstRender.fs` + `test/Frank.Cli.Core.Tests/AstRenderTests.fs` — add `valueDecl`.
- **Create** `src/Frank.LinkedData/Ontology.fs` — the interpreter; add to fsproj BEFORE `GeneratedLinkedDataResolver.fs` (it is independent; place after `LinkedDataTypes.fs`).
- **Modify** `src/Frank.LinkedData/Frank.LinkedData.fsproj` — add `Frank.Semantic` ProjectReference + the new file.
- **Create** `test/Frank.LinkedData.Tests/OntologyTests.fs` (if the test project exists; else add to the existing LinkedData test project) — semantic interpreter tests.
- **Modify** `src/Frank.Cli.Core/LinkedDataEmitter.fs` — projection → `OntologyDecl`; emit via AstRender.
- **Modify** `test/Frank.Cli.Core.Tests/LinkedDataEmitterTests.fs` — tier-1 projection + tier-3 compile-gate; reconcile substrings.

---

### Task 1: `Frank.Semantic` — `OntologyDecl` types + `Triples` helper

**Files:**
- Create: `src/Frank.Semantic/OntologyTypes.fs`
- Modify: `src/Frank.Semantic/Frank.Semantic.fsproj` (add after `ResolvedModel.fs`)
- Test: `test/Frank.Semantic.Tests/OntologyTypesTests.fs` (+ its fsproj)

**Interfaces — Produces:**
```fsharp
namespace Frank.Semantic
open System
type PropertyDecl = { Iri: Uri; Domain: Uri }
type ClassDecl = { Iri: Uri; EquivalentClass: Uri option; SeeAlso: Uri list; Properties: PropertyDecl list }
type OntologyDecl = { Classes: ClassDecl list; ContextBases: Uri list }
// Triples: generic dotNetRdf assertion primitives (shared by LinkedData + later Validation interpreters)
module Triples =
    val uriNode   : VDS.RDF.IGraph -> string -> VDS.RDF.INode
    val qnameNode : VDS.RDF.IGraph -> string -> VDS.RDF.INode   // resolves a prefixed name via g.ResolveQName
    val assert3   : VDS.RDF.IGraph -> VDS.RDF.INode -> VDS.RDF.INode -> VDS.RDF.INode -> unit
```

- [ ] **Step 1: Write the failing test.** `test/Frank.Semantic.Tests/OntologyTypesTests.fs`:

```fsharp
module Frank.Semantic.Tests.OntologyTypesTests
open System
open Expecto
open Frank.Semantic
open VDS.RDF

[<Tests>]
let tests =
    testList "OntologyTypes + Triples" [
        test "Triples.assert3 adds a triple resolvable by predicate/object" {
            let g = new Graph() :> IGraph
            g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
            g.NamespaceMap.AddNamespace("owl", UriFactory.Create "http://www.w3.org/2002/07/owl#")
            let s = Triples.uriNode g "https://schema.org/Game"
            let p = Triples.qnameNode g "rdf:type"
            let o = Triples.qnameNode g "owl:Class"
            Triples.assert3 g s p o
            Expect.isNonEmpty (g.GetTriplesWithPredicateObject(p, o) |> Seq.toList) "owl:Class triple present"
        }
        test "OntologyDecl is constructible with required Iri/Domain" {
            let d : OntologyDecl =
                { Classes = [ { Iri = Uri "https://schema.org/Game"; EquivalentClass = None; SeeAlso = []
                                Properties = [ { Iri = Uri "https://schema.org/position"; Domain = Uri "https://schema.org/Game" } ] } ]
                  ContextBases = [ Uri "https://schema.org" ] }
            Expect.equal d.Classes.Head.Properties.Head.Domain (Uri "https://schema.org/Game") "domain required + set"
        }
    ]
```

- [ ] **Step 2: Run — fails.** Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ --filter "OntologyTypes"`

- [ ] **Step 3: Implement `OntologyTypes.fs`.**

```fsharp
namespace Frank.Semantic

open System
open VDS.RDF

type PropertyDecl = { Iri: Uri; Domain: Uri }

type ClassDecl =
    { Iri: Uri
      EquivalentClass: Uri option
      SeeAlso: Uri list
      Properties: PropertyDecl list }

type OntologyDecl =
    { Classes: ClassDecl list
      ContextBases: Uri list }

/// Generic RDF triple-assertion primitives over an IGraph. Shared by the LinkedData
/// (and later Validation) interpreters — the single place raw triples are built.
module Triples =
    let uriNode (g: IGraph) (iri: string) : INode = g.CreateUriNode(UriFactory.Create iri)
    let qnameNode (g: IGraph) (qname: string) : INode = g.CreateUriNode(g.ResolveQName qname)
    let assert3 (g: IGraph) (s: INode) (p: INode) (o: INode) : unit = g.Assert(Triple(s, p, o)) |> ignore
```

Add `<Compile Include="OntologyTypes.fs" />` to `src/Frank.Semantic/Frank.Semantic.fsproj` immediately after `ResolvedModel.fs`. Add `OntologyTypesTests.fs` to `test/Frank.Semantic.Tests`' fsproj.

- [ ] **Step 4: Run — passes.** Same filter as Step 2.

- [ ] **Step 5: Full Semantic suite + fantomas + commit.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Semantic.Tests/ && dotnet fantomas --check src/Frank.Semantic/OntologyTypes.fs test/Frank.Semantic.Tests/OntologyTypesTests.fs`
Expected: PASS; fantomas clean.

```bash
git add src/Frank.Semantic/OntologyTypes.fs src/Frank.Semantic/Frank.Semantic.fsproj test/Frank.Semantic.Tests/OntologyTypesTests.fs test/Frank.Semantic.Tests/*.fsproj
git commit -m "feat(semantic): OntologyDecl types + Triples helper (typed LinkedData artifact)"
```

---

### Task 2: `AstRender.valueDecl` — a plain typed `let` module declaration

**Files:**
- Modify: `src/Frank.Cli.Core/AstRender.fs`
- Modify: `test/Frank.Cli.Core.Tests/AstRenderTests.fs`

**Interfaces — Produces:** `AstRender.valueDecl : name:string -> typeName:string -> value:WidgetBuilder<Expr> -> ModuleDeclItem`

- [ ] **Step 1: Failing round-trip test** in `AstRenderTests.fs`:

```fsharp
test "formatModule renders multiple typed value bindings" {
    let decls =
        [ AstRender.valueDecl "ontology" "OntologyDecl" (AstRender.recordExpr [ "Classes", AstRender.listExpr []; "ContextBases", AstRender.listExpr [] ])
          AstRender.valueDecl "graph" "VDS.RDF.IGraph" (AstRender.appExpr "Ontology.toGraph" (AstRender.rawExpr "ontology")) ]
    let src = AstRender.formatModule "T.GeneratedLinkedData" None [ "Frank.Semantic"; "Frank.LinkedData" ] decls
    Expect.stringContains src "let ontology: OntologyDecl =" "typed ontology binding"
    Expect.stringContains src "let graph: VDS.RDF.IGraph = Ontology.toGraph ontology" "graph binding calls interpreter"
}
```

- [ ] **Step 2: Run — fails** (`valueDecl` undefined). Run: `... dotnet test test/Frank.Cli.Core.Tests/ --filter "AstRender"`

- [ ] **Step 3: Implement.** Append to `AstRender.fs`:

```fsharp
/// A plain typed value binding as a module declaration: let <name>: <typeName> = <value>
let valueDecl (name: string) (typeName: string) (value: WidgetBuilder<Expr>) : ModuleDeclItem =
    BindingDecl(Value(name, value, typeName))
```

- [ ] **Step 4: Run — passes.** Same filter.

- [ ] **Step 5: Fantomas + commit.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && dotnet fantomas --check src/Frank.Cli.Core/AstRender.fs test/Frank.Cli.Core.Tests/AstRenderTests.fs` (format first if needed)
```bash
git add src/Frank.Cli.Core/AstRender.fs test/Frank.Cli.Core.Tests/AstRenderTests.fs
git commit -m "feat(cli): AstRender.valueDecl — typed value binding module decl"
```

---

### Task 3: `Frank.LinkedData` — `Ontology` interpreter

**Files:**
- Create: `src/Frank.LinkedData/Ontology.fs`
- Modify: `src/Frank.LinkedData/Frank.LinkedData.fsproj` (add `Frank.Semantic` ProjectReference + `Ontology.fs` after `LinkedDataTypes.fs`)
- Test: `test/Frank.LinkedData.Tests/OntologyTests.fs` (+ its fsproj if needed)

**Interfaces — Produces:**
```fsharp
module Frank.LinkedData.Ontology
val toGraph         : Frank.Semantic.OntologyDecl -> VDS.RDF.IGraph
val toJsonLdContext : Frank.Semantic.OntologyDecl -> string
```

- [ ] **Step 1: Failing semantic test** `test/Frank.LinkedData.Tests/OntologyTests.fs`:

```fsharp
module Frank.LinkedData.Tests.OntologyTests
open System
open Expecto
open Frank.Semantic
open Frank.LinkedData
open VDS.RDF

let private sampleOntology : OntologyDecl =
    { Classes =
        [ { Iri = Uri "https://schema.org/Game"; EquivalentClass = None; SeeAlso = []
            Properties = [ { Iri = Uri "https://schema.org/position"; Domain = Uri "https://schema.org/Game" } ] } ]
      ContextBases = [ Uri "https://schema.org/" ] }

[<Tests>]
let tests =
    testList "Ontology interpreter" [
        test "toGraph emits owl:Class for each class" {
            let g = Ontology.toGraph sampleOntology
            let rdfType = g.CreateUriNode(UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")
            let owlClass = g.CreateUriNode(UriFactory.Create "http://www.w3.org/2002/07/owl#Class")
            Expect.isNonEmpty (g.GetTriplesWithPredicateObject(rdfType, owlClass) |> Seq.toList) "owl:Class present"
        }
        test "toGraph emits rdfs:domain for each property" {
            let g = Ontology.toGraph sampleOntology
            let domain = g.CreateUriNode(UriFactory.Create "http://www.w3.org/2000/01/rdf-schema#domain")
            let cls = g.CreateUriNode(UriFactory.Create "https://schema.org/Game")
            Expect.isNonEmpty (g.GetTriplesWithPredicateObject(domain, cls) |> Seq.toList) "domain → class present"
        }
        test "toJsonLdContext lists external bases (trailing slash trimmed)" {
            let ctx = Ontology.toJsonLdContext sampleOntology
            Expect.stringContains ctx "\"https://schema.org\"" "base IRI present, slash trimmed"
            Expect.stringContains ctx "@context" "is a @context document"
        }
    ]
```

- [ ] **Step 2: Run — fails** (Ontology/project missing). Run: `... dotnet test test/Frank.LinkedData.Tests/ --filter "Ontology"` (if the test project does not exist, create it mirroring an existing `Frank.LinkedData.Tests` setup; if `Frank.LinkedData.Tests` already exists, add the file).

- [ ] **Step 3: Implement `Ontology.fs`.**

```fsharp
module Frank.LinkedData.Ontology

open System
open System.Text
open VDS.RDF
open Frank.Semantic

let private rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"
let private rdfs = "http://www.w3.org/2000/01/rdf-schema#"
let private owl = "http://www.w3.org/2002/07/owl#"

let private addClass (g: IGraph) (c: ClassDecl) : unit =
    let subj = Triples.uriNode g c.Iri.AbsoluteUri
    Triples.assert3 g subj (Triples.qnameNode g "rdf:type") (Triples.qnameNode g "owl:Class")
    match c.EquivalentClass with
    | Some e -> Triples.assert3 g subj (Triples.qnameNode g "owl:equivalentClass") (Triples.uriNode g e.AbsoluteUri)
    | None -> ()
    for s in c.SeeAlso do
        Triples.assert3 g subj (Triples.qnameNode g "rdfs:seeAlso") (Triples.uriNode g s.AbsoluteUri)
    for p in c.Properties do
        let pNode = Triples.uriNode g p.Iri.AbsoluteUri
        Triples.assert3 g pNode (Triples.qnameNode g "rdf:type") (Triples.qnameNode g "rdf:Property")
        Triples.assert3 g pNode (Triples.qnameNode g "rdfs:domain") (Triples.uriNode g p.Domain.AbsoluteUri)

let toGraph (ontology: OntologyDecl) : IGraph =
    let g = new Graph() :> IGraph
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create rdf)
    g.NamespaceMap.AddNamespace("rdfs", UriFactory.Create rdfs)
    g.NamespaceMap.AddNamespace("owl", UriFactory.Create owl)
    for c in ontology.Classes do
        addClass g c
    g

let toJsonLdContext (ontology: OntologyDecl) : string =
    let items =
        ontology.ContextBases
        |> List.map (fun u -> "\"" + u.AbsoluteUri.TrimEnd('/') + "\"")
        |> String.concat ","
    "{\"@context\":[" + items + "]}"
```

> The `toJsonLdContext` string-builds a JSON document — that is RUNTIME data emission, not F# codegen; it is allowed (it mirrors the old emitter's `@context` exactly). Add `<ProjectReference Include="../Frank.Semantic/Frank.Semantic.fsproj" />` to `Frank.LinkedData.fsproj` and `<Compile Include="Ontology.fs" />` after `LinkedDataTypes.fs`.

- [ ] **Step 4: Run — passes.** Same filter as Step 2.

- [ ] **Step 5: Full LinkedData suite + fantomas + commit.**

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.LinkedData.Tests/ && dotnet fantomas --check src/Frank.LinkedData/Ontology.fs test/Frank.LinkedData.Tests/OntologyTests.fs`
```bash
git add src/Frank.LinkedData/Ontology.fs src/Frank.LinkedData/Frank.LinkedData.fsproj test/Frank.LinkedData.Tests/
git commit -m "feat(linkeddata): Ontology interpreter — OntologyDecl to IGraph + @context"
```

---

### Task 4: `LinkedDataEmitter` — project to `OntologyDecl`, emit via AstRender

**Files:**
- Modify: `src/Frank.Cli.Core/LinkedDataEmitter.fs`
- Modify: `test/Frank.Cli.Core.Tests/LinkedDataEmitterTests.fs`

**Interfaces:**
- Consumes: `AstRender.*` (valueDecl/recordExpr/listExpr/uriExpr/someStrExpr/noneExpr/appExpr/rawExpr/parenExpr/formatModule); `Frank.Semantic.{OntologyDecl,ClassDecl,PropertyDecl}`; `FcsTypecheck.typecheckTwoSources`.
- Produces: `emit` unchanged (`moduleName -> registry -> lock -> Result<string,string>`); internal `projectOntology : ResolvedModel -> OntologyDecl`.

- [ ] **Step 1: Failing tier-1 projection test** in `LinkedDataEmitterTests.fs` (reuse the file's fixtures + `okOrFail`):

```fsharp
test "projectOntology yields a typed ClassDecl per class-mapped resource with required domains (tier 1)" {
    let model = ResolvedModel.build fixtureRegistry fixtureLock |> okOrFail
    let onto = LinkedDataEmitter.projectOntology model
    Expect.isNonEmpty onto.Classes "at least one class"
    let gameClass = onto.Classes |> List.find (fun c -> c.Iri.AbsoluteUri = "https://schema.org/Game")
    Expect.all gameClass.Properties (fun p -> p.Domain.AbsoluteUri = "https://schema.org/Game") "every property domain is its class"
    Expect.isNonEmpty onto.ContextBases "context bases present"
}
```

- [ ] **Step 2: Failing tier-3 compile-gate test:**

```fsharp
test "emitted GeneratedLinkedData compiles against Frank.Semantic/Frank.LinkedData (tier 3)" {
    let src = LinkedDataEmitter.emit "Probe.GeneratedLinkedData" fixtureRegistry fixtureLock |> okOrFail
    // domainSrc: minimal stand-ins for the types the emitted module references.
    let domainSrc =
        "namespace Frank.Semantic\nopen System\n" +
        "type PropertyDecl = { Iri: Uri; Domain: Uri }\n" +
        "type ClassDecl = { Iri: Uri; EquivalentClass: Uri option; SeeAlso: Uri list; Properties: PropertyDecl list }\n" +
        "type OntologyDecl = { Classes: ClassDecl list; ContextBases: Uri list }\n" +
        "namespace Frank.LinkedData\nmodule Ontology =\n    let toGraph (_: Frank.Semantic.OntologyDecl) : VDS.RDF.IGraph = null\n    let toJsonLdContext (_: Frank.Semantic.OntologyDecl) : string = \"\"\n"
    let diagnostics = FcsTypecheck.typecheckTwoSources domainSrc src
    Expect.isEmpty diagnostics "emitted LinkedData module compiles cleanly"
}
```

> Implementer: the `domainSrc` stand-in must match the REAL `OntologyDecl`/`ClassDecl`/`PropertyDecl` field names/types (Task 1) and the `Ontology.toGraph`/`toJsonLdContext` signatures (Task 3). If `typecheckTwoSources` cannot resolve `VDS.RDF.IGraph`, add the dotNetRdf reference resolution the existing harness uses, or assert against the real referenced assemblies if simpler. Confirm the stand-in matches reality before relying on it.

- [ ] **Step 3: Run — fails.** Run: `... dotnet test test/Frank.Cli.Core.Tests/ --filter "tier"`

- [ ] **Step 4: Rewrite `LinkedDataEmitter.fs`.** Keep `buildContext`'s prefix-resolution LOGIC but reshape it to produce typed `Uri list` (ContextBases) instead of a JSON string. Delete `esc`/`assertTriple`/`uriNode`/`qnameNode`/`typeTriples`/`fieldTriples`/`collectTriples`/`namespaceSetup`/`assembleModule`. New shape:

```fsharp
module Frank.Cli.Core.LinkedDataEmitter
open System
open Frank.Semantic
open Frank.Semantic.LockFile

// Resolve the external base IRIs for the @context from the model's Using set + Prefixes.
let private contextBases (model: ResolvedModel) : Result<Uri list, string> =
    let rec loop remaining acc =
        match remaining with
        | [] -> Ok(List.rev acc)
        | prefix :: rest ->
            match Map.tryFind prefix model.Prefixes with
            | None -> Error $"using prefix '{prefix}' not found in Prefixes"
            | Some baseUri -> loop rest (baseUri :: acc)
    loop (Set.toList model.Using) []

let private toClassDecl (r: ResolvedResource) : ClassDecl option =
    r.ClassIri
    |> Option.map (fun classUri ->
        let props =
            r.Fields
            |> List.choose (fun f -> f.Iri |> Option.map (fun iri -> { Iri = iri; Domain = classUri }))
        { Iri = classUri; EquivalentClass = r.EquivalentClass; SeeAlso = r.SeeAlso; Properties = props })

let internal projectOntology (model: ResolvedModel) : OntologyDecl =
    { Classes = model.Resources |> List.choose toClassDecl
      ContextBases = [] }   // filled by emit via contextBases (kept separate so projectOntology stays pure/total)

// ── Rendering via AstRender ────────────────────────────────────────────────
let private uriField (name: string) (u: Uri) = name, AstRender.appExpr "System.Uri" (AstRender.strExpr u.AbsoluteUri)
let private optUriField (name: string) (u: Uri option) =
    name, (match u with
           | Some v -> AstRender.appExpr "Some" (AstRender.parenExpr (AstRender.appExpr "System.Uri" (AstRender.strExpr v.AbsoluteUri)))
           | None -> AstRender.noneExpr)
let private uriListField (name: string) (us: Uri list) =
    name, AstRender.listExpr (us |> List.map (fun u -> AstRender.appExpr "System.Uri" (AstRender.strExpr u.AbsoluteUri)))

let private propExpr (p: PropertyDecl) =
    AstRender.recordExpr [ uriField "Iri" p.Iri; uriField "Domain" p.Domain ]

let private classExpr (c: ClassDecl) =
    AstRender.recordExpr
        [ uriField "Iri" c.Iri
          optUriField "EquivalentClass" c.EquivalentClass
          uriListField "SeeAlso" c.SeeAlso
          "Properties", AstRender.listExpr (c.Properties |> List.map propExpr) ]

let private ontologyExpr (onto: OntologyDecl) =
    AstRender.recordExpr
        [ "Classes", AstRender.listExpr (onto.Classes |> List.map classExpr)
          uriListField "ContextBases" onto.ContextBases ]

let emit (moduleName: string) (registry: VocabularyRegistry) (lock: LockFile) : Result<string, string> =
    if String.IsNullOrWhiteSpace moduleName then
        invalidArg (nameof moduleName) "moduleName must not be empty"
    match ResolvedModel.build registry lock with
    | Error e -> Error e
    | Ok model ->
        match contextBases model with
        | Error e -> Error e
        | Ok bases ->
            let onto = { projectOntology model with ContextBases = bases }
            let decls =
                [ AstRender.valueDecl "ontology" "OntologyDecl" (ontologyExpr onto)
                  AstRender.valueDecl "graph" "VDS.RDF.IGraph" (AstRender.appExpr "Ontology.toGraph" (AstRender.rawExpr "ontology"))
                  AstRender.valueDecl "jsonLdContext" "string" (AstRender.appExpr "Ontology.toJsonLdContext" (AstRender.rawExpr "ontology")) ]
            Ok(AstRender.formatModule moduleName None [ "Frank.Semantic"; "Frank.LinkedData" ] decls)
```

> Notes: `projectOntology` returns `ContextBases = []` and `emit` overrides it with the resolved bases — keeps the pure projection total and the IRI-resolution (which can `Error`) in `emit`. The generated module keeps the `graph` and `jsonLdContext` static bindings the resolver reads — verify their names are exactly `graph` and `jsonLdContext`. `System.Uri "x"` literal needs no `open`. Confirm the emitted `Some (System.Uri "x")` (parenExpr) is valid for the `EquivalentClass` field.

- [ ] **Step 5: Run tier tests — pass.** Same filter as Step 3.

- [ ] **Step 6: Reconcile substring tests + full Cli.Core suite.** The old tests asserted `g.Assert`/`owl:Class`/`CreateUriNode` strings from the imperative output; the new output is a typed `OntologyDecl` value + interpreter calls — those imperative-output substrings are GONE by design. Update them to assert the new reality (the IRIs still appear in the `OntologyDecl` literal; `owl:Class` no longer appears in the emitter output — it moved to the interpreter, covered by Task 3's semantic tests). Keep the `no urn:frank:` assertion and IRI-presence assertions; do not delete tests — re-point them at the typed output or at the interpreter test if the assertion belongs there. Print the emitted source to see reality.

Run: `cd /Users/ryanr/Code/frank/.claude/worktrees/v732-codegen-remediation && DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=1 dotnet test test/Frank.Cli.Core.Tests/ && dotnet fantomas --check src/Frank.Cli.Core/LinkedDataEmitter.fs test/Frank.Cli.Core.Tests/LinkedDataEmitterTests.fs`

- [ ] **Step 7: Commit.**

```bash
git add src/Frank.Cli.Core/LinkedDataEmitter.fs test/Frank.Cli.Core.Tests/LinkedDataEmitterTests.fs
git commit -m "refactor(cli): LinkedDataEmitter emits typed OntologyDecl via AstRender; interpreter builds the graph"
```

---

## Self-Review

- **Spec coverage:** Plan 3 delivers the LinkedData typed artifact (spec: `OntologyDecl` in `Frank.Semantic`, interpreter in `Frank.LinkedData`, emitter builds typed value via Fabulous.AST). The `Triples` helper (spec's shared term-layer plumbing) lands here and is reused by Plan 4 (Validation). AC #1 (no string-concat of code), illegal-states (required `Domain`/`Iri`). The `Some (System.Uri x)` parenExpr need is already solved (Plan 2).
- **Placeholder scan:** none. The `domainSrc` stand-in in Task 4 is a real, specified compile-gate input (matches the Task 1/3 signatures); the implementer must confirm it matches reality.
- **Type consistency:** `OntologyDecl`/`ClassDecl`/`PropertyDecl` field names identical across Task 1 (definition), Task 3 (interpreter), Task 4 (emitter literal + domainSrc). `Ontology.toGraph`/`toJsonLdContext` signatures consistent Task 3 ↔ Task 4.

## Next

- **Plan 4:** Validation fresh build — `XsdDatatype`/`NonEmptyList`/`ShapeDecl` in `Frank.Semantic` (reusing `Triples`); `Shapes.toShapesGraph` interpreter in a new `Frank.Validation`; `ValidationEmitter` via AstRender; cherry-pick `enrichTypes` (`169fe69d`); MSBuild task + targets + resolver; tier-2 SHACL conformance tests.
