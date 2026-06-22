# v7.3.2 Codegen Remediation — Fabulous.AST + Typed Artifacts (design)

**Date:** 2026-06-22
**Status:** design (approved in brainstorming; pending written-spec review)
**Supersedes:** the string-emission approach baked into the v7.3.2 emitter specs/plans (including `2026-06-20-v732-validation-vertical-design.md` §V2 "assert equal strings", "mirror LinkedDataEmitter").

## Thesis

All v7.3.2 codegen emits F# source by **building a Fabulous.AST tree that Fantomas formats**, never by string concatenation. The generated artifacts are **typed F# values** whose types make illegal RDF/SHACL/OWL states unrepresentable, interpreted into `IGraph`/`ShapesGraph` by library code. This is what the v7.3.2 codegen exists to prove before v7.4.0's `Frank.CodeGen` relies on the same method.

## Problem

The four v7.3.2 emitters in `src/Frank.Cli.Core/` — `DiscoveryEmitter`, `LinkedDataEmitter`, `SemanticModelEmitter`, `ValidationEmitter` — were built with string concatenation (`render*`/`assembleModule`), and the specs/plans encoded that choice. This violates the project's documented codegen principle (`docs/superpowers/specs/2026-04-21-v740-protocol-types-design.md`: "Fabulous.AST is the day-one emission target"; "do not concatenate strings; build ASTs"). No durable record of the Fabulous.AST mandate existed, so it drifted across the whole vertical. Secondary defects surfaced: `ValidationEmitter` carries a Rule-8 duplicate (`buildShapesGraph` re-encodes the SHACL rules in-process), and its runtime-validation test asserted a tautology (`report.Conforms |> not |> (fun _ -> true)` ≡ `true`).

## Governing principles

1. **Make illegal states unrepresentable.** A malformed shape/ontology must not be constructible. `IGraph` is rejected as the type structure — it is an untyped triple bag in which every illegal SHACL/OWL state (pathless property shape, orphaned `sh:in` list, dangling `rdfs:domain`, arbitrary `sh:datatype`) is representable.
2. **Build ASTs, not strings.** Fabulous.AST + Fantomas for all emission.
3. **Builds fail on incorrect types/values.** Generated values compile WITH the domain; the FCS gate proves compile-validity (and, for `SemanticModelEmitter`, cross-type drift).

## Architecture

### Term layer — merged into `Frank.Semantic`

`Frank.Semantic` is light (`dotNetRdf.Core` only, no FCS), multi-targeted net8/9/10, and already a consumer-app dependency, so the shared term layer lives there (no new package, no new consumer dependency, no cycle).

```fsharp
namespace Frank.Semantic

/// Non-empty by construction — an empty/orphaned list is unrepresentable.
type NonEmptyList<'T> = { Head: 'T; Tail: 'T list }
module NonEmptyList =
    let ofList = function [] -> None | x :: xs -> Some { Head = x; Tail = xs }
    let toList n = n.Head :: n.Tail

/// Closed set of xsd datatypes Frank maps F# primitives to. An arbitrary datatype IRI is unrepresentable.
type XsdDatatype =
    | XsdInteger | XsdLong | XsdDecimal | XsdDouble | XsdBoolean | XsdString | XsdDateTime
```

Plus one low-level triple-assertion helper over `VDS.RDF.IGraph` (the only shared graph-building plumbing; satisfies Rule 8 at the term layer). `Iri = System.Uri` (no new wrapper; IRIs arrive pre-validated from the lock; absolute-IRI validation is `System.Uri`'s runtime job).

### Data DUs — in `Frank.Semantic`

The data DUs live in `Frank.Semantic` (not the runtime packages) so the emitter in `Frank.Cli.Core` — which already references `Frank.Semantic` — can construct **real typed values** for testing, without `Frank.Cli.Core` depending on any runtime package.

```fsharp
namespace Frank.Semantic
open System

// ── SHACL (Validation) ──
/// Path is REQUIRED (non-option Uri) — a pathless property shape can't exist.
type PropertyShape =
    { Path: Uri
      Datatype: XsdDatatype option   // None = domain type → no sh:datatype (valid, not an error)
      MinCount: int                  // 0 optional · 1 required
      MaxCount: int option           // None = unbounded (collection)
      Pattern: string option }

/// A node shape is EITHER a record OR a nullary-union enum — never both, never neither.
type ShapeDecl =
    | RecordShape of targetClass: Uri * properties: PropertyShape list
    | EnumShape   of targetClass: Uri * cases: NonEmptyList<Uri>   // sh:in — never empty/orphaned

// ── OWL/RDFS (LinkedData) ──
/// Domain REQUIRED — no dangling rdfs:domain.
type PropertyDecl = { Iri: Uri; Domain: Uri }

type ClassDecl =
    { Iri: Uri
      EquivalentClass: Uri option   // owl:equivalentClass
      SeeAlso: Uri list             // rdfs:seeAlso
      Properties: PropertyDecl list }

type OntologyDecl =
    { Classes: ClassDecl list
      ContextBases: Uri list }      // typed @context bases; interpreter renders the JSON
```

Illegal states unrepresentable: pathless property shape (Path required), orphaned/empty `sh:in` (`NonEmptyList`), node shape that is neither record nor enum (total DU), arbitrary `sh:datatype` (closed enum; `None` for domain types), dangling `rdfs:domain` (Domain required), class without IRI (Iri required).

### Interpreters — in the runtime packages

The interpreters need `dotNetRdf.Shacl` / return `IGraph`, which `Frank.Semantic` must not pull, so they live in the runtime packages.

```fsharp
// Frank.Validation
module Shapes =
    /// THE single place SHACL triples are built. Total over ShapeDecl, correct by construction.
    val toShapesGraph : ShapeDecl list -> VDS.RDF.Shacl.ShapesGraph

// Frank.LinkedData
module Ontology =
    /// THE single place OWL/RDFS triples are built. Total over OntologyDecl.
    val toGraph         : OntologyDecl -> VDS.RDF.IGraph
    val toJsonLdContext : OntologyDecl -> string
```

`Frank.Validation` and `Frank.LinkedData` gain a reference to `Frank.Semantic`. Accepted layering note: those runtime libs gain visibility into build-time code (`ConventionEngine`, `LockFile`) they do not use — a mild smell, acceptable because `Frank.Semantic` is the semantic-discovery foundation and is runtime-shippable; extracting a leaf later is trivial.

### `buildShapesGraph` elimination

`Shapes.toShapesGraph` is the single SHACL graph-builder. `ValidationEmitter.renderModule` (string) **and** `ValidationEmitter.buildShapesGraph` (in-process duplicate) are both deleted — the Rule-8 duplication is resolved structurally. Tests call the interpreter; the generated file calls the interpreter.

### Emitter restructure — two pure halves

Each emitter splits into:
1. **Projection** `ResolvedModel -> ShapeDecl list` (per emitter) — pure, no AST, no strings; produces real typed values.
2. **AST renderer** `ShapeDecl list -> F# source` via Fabulous.AST — pure; term-rendering (`Uri`/`XsdDatatype`/`NonEmptyList`/record literals → AST) shared across emitters.

### Generated artifacts

```fsharp
// GeneratedValidation.fs
module TicTacToe.GeneratedValidation
open System
open Frank.Semantic
open Frank.Validation
let shapes : ShapeDecl list =
    [ RecordShape(Uri "https://schema.org/MoveAction",
        [ { Path = Uri "https://schema.org/position"
            Datatype = Some XsdInteger; MinCount = 1; MaxCount = Some 1; Pattern = None } ])
      EnumShape(Uri "https://schema.org/GameStatusType",
        { Head = Uri "https://schema.org/ActiveActionStatus"
          Tail = [ Uri "https://schema.org/CompletedActionStatus" ] }) ]
let shapesGraph : VDS.RDF.Shacl.ShapesGraph = Shapes.toShapesGraph shapes

// GeneratedLinkedData.fs
module TicTacToe.GeneratedLinkedData
open System
open Frank.Semantic
open Frank.LinkedData
let ontology : OntologyDecl =
    { Classes =
        [ { Iri = Uri "https://schema.org/MoveAction"; EquivalentClass = None; SeeAlso = []
            Properties = [ { Iri = Uri "https://schema.org/position"; Domain = Uri "https://schema.org/MoveAction" } ] } ]
      ContextBases = [ Uri "https://schema.org" ] }
let graph : VDS.RDF.IGraph = Ontology.toGraph ontology
let jsonLdContext : string = Ontology.toJsonLdContext ontology
```

Resolver static names (`shapesGraph`, `graph`, `jsonLdContext`, `discoveryConfig`) are preserved so existing resolvers are unchanged.

## Per-emitter remediation

- **Discovery** (mechanism-only) — Fabulous.AST builds the same `DiscoveryConfig` record literal. No DU change, no behavior change. Done first to derisk Fabulous.AST mechanics.
- **Validation** (typed artifact) — projection → `ShapeDecl` → AST + `Shapes.toShapesGraph` interpreter; `buildShapesGraph` deleted. Template for graph emitters.
- **LinkedData** (typed artifact) — mirrors Validation: `OntologyDecl` + `Ontology.toGraph`/`toJsonLdContext`.
- **SemanticModel** (mechanism-only) — Fabulous.AST builds the `SemanticResource` DU decl + `iri`/`clrType`/`<type>CaseIri` `match` functions; `typeof<…>`/`typedefof<…<_>>` and case-constructor patterns via Fabulous.AST's raw-expression escape hatch. Anti-drift guard semantics preserved. Done last (trickiest AST).

## Test strategy — three tiers (substring assertions are not the gate)

| Tier | What | Where |
|---|---|---|
| 1 Projection | `Expect.equal (project model) [ RecordShape(…) ]` — typed-value equality | `Frank.Cli.Core.Tests` |
| 2 Semantic | feed hand-built DUs to the interpreter; run real SHACL validation (conforms / not-conforms) or assert specific triples | `Frank.Validation.Tests`, `Frank.LinkedData.Tests` |
| 3 Compile gate | FCS-compile the emitted source + domain types → zero errors (#324 AT1; drift for SemanticModel) | `Frank.Cli.MSBuild.Tests` |

Plus determinism (emit twice → byte-identical; Fantomas is deterministic). Substring checks may remain only as cheap smoke, never the correctness gate. Tier 2 replaces the tautological conformance test with real `Conforms` assertions (focus node in `sh:in` list conforms; absent does not).

## Tooling

- **Fabulous.AST 1.10.0** restores cleanly and pulls **Fantomas.Core/FCS 7.0.1** — same 7.0.x line as the pinned Fantomas 7.0.5 CLI; no version conflict (verified by spike 2026-06-22). If the generated file ever fails `fantomas --check`, pin `Fantomas.Core` to 7.0.5.

## Scope, branch, order

- **Worktree** `v732-codegen-remediation` from `master`. Cherry-pick V1 `enrichTypes` (`169fe69d`, `ResolvedField.TypeName/IsOptional/IsCollection`) — needed unchanged by the `ShapeDecl` projection (datatype/cardinality). Park `validation-vertical` (its string V2 + tautological tests superseded).
- **Order:** (1) `Frank.Semantic` foundation — term layer + data DUs + `enrichTypes`; (2) shared term→AST rendering; (3) Discovery; (4) Validation (deletes `buildShapesGraph`); (5) LinkedData; (6) SemanticModel; (7) wire each generated file + its MSBuild FCS gate.
- **Then** the validation vertical's runtime work (V3 middleware, V5 sample, V6 E2E) resumes on the corrected base — downstream of this remediation.
- **Issues:** update #323/#324/#326 + the SemanticModel issue acceptance criteria to mandate Fabulous.AST + typed artifacts; umbrella note under #336. (Drafted and shown before editing — outward-facing.)

## Acceptance criteria (falsifiable)

1. No emitter contains string-concatenation source assembly (`render*`/`assembleModule` returning hand-built F#); each emits via Fabulous.AST. (grep + review)
2. `ShapeDecl`/`OntologyDecl` + term layer compile in `Frank.Semantic`; an empty `sh:in` (`NonEmptyList`), a pathless property shape, or a node shape that is neither record nor enum **does not compile**.
3. `ValidationEmitter.buildShapesGraph` and `renderModule` no longer exist; `Shapes.toShapesGraph` is the sole SHACL builder.
4. Tier-2: a generated/equivalent `ShapesGraph` validates data — focus node present in `sh:in` ⇒ `Conforms = true`; absent ⇒ `false`; no `RdfException`.
5. Tier-3: emitted `GeneratedValidation.fs`/`GeneratedLinkedData.fs`/`GeneratedDiscovery.fs`/`GeneratedSemanticModel.fs` FCS-compile against the domain with zero errors; renaming a mapped domain type breaks the `GeneratedSemanticModel` compile.
6. Determinism: two builds of each generated file are byte-identical.
7. No `urn:frank:` in any generated source.
8. All existing suites green; `fantomas --check src/` clean.

## Out of scope

- Validation runtime features V3 (middleware), V5 (sample), V6 (E2E) — resume after the remediation, on the corrected base.
- Cross-type drift detection for Validation/LinkedData — stays `SemanticModelEmitter`'s job (their shapes reference IRIs, not F# type names).
- `Frank.Provenance` emitter — not yet built; created later per the same typed pattern (not part of this remediation).
- Reworking v7.4.0 `Frank.CodeGen` — already specced for Fabulous.AST; unaffected.

## Sources

- `docs/superpowers/specs/2026-04-21-v740-protocol-types-design.md` — the Fabulous.AST mandate.
- `docs/superpowers/specs/2026-06-20-v732-validation-vertical-design.md` — superseded string approach (§V2).
- `src/Frank.Cli.Core/{DiscoveryEmitter,LinkedDataEmitter,SemanticModelEmitter,ValidationEmitter}.fs` — current string emitters (projection logic reused; rendering replaced).
- Memory `feedback-codegen-fabulous-ast` — durable record of the mandate + the drift.
