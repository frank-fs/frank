# Frank.Validation

**Date**: 2026-08-03
**Branch**: `validation`
**Status**: Draft — awaiting review

## Context

[Frank.Rdf](2026-07-30-frank-rdf-design.md) and [Frank.Provenance](2026-08-02-frank-provenance-design.md) have both shipped, establishing this package family's house style: a hand-authored target shape first, a closed vocabulary layer built on `Frank.Rdf`'s `Doc`/`Node`/`Description`, no codegen, no reflection-driven type→IRI mapping. Both design docs named `Frank.Validation` (SHACL) as the next sub-project.

### Prior attempt

`feature/v7.3.2` (now invalidated, `[[project_v3_rollback]]`) carried `docs/superpowers/plans/2026-06-22-v732-codegen-remediation-plan4-validation.md` ("Plan 4"). Its actual failure mode — confirmed against this session's own history of correcting the same mistake twice more before landing here — was building codegen machinery (`enrichTypes` reflection, `ValidationEmitter`, an MSBuild `GenerateValidationTask`) *before* ever hand-authoring a single shape, exactly the pattern `[[feedback_outside_in_before_codegen]]` names. What Plan 4 got right, and what carries forward unchanged: a typed `ShapeDecl` model ("illegal SHACL states unrepresentable") and a `Shapes.toShapesGraph : ShapeDecl list -> ShapesGraph` interpreter — hand-authored declarations totaled into a graph, no derivation. `Frank.Provenance`'s own design doc credits this shape explicitly as "never actually the problem."

That the earlier attempt also under-covered SHACL itself — a separate defect from the codegen mistake, not the same one — surfaced during this design's own review: an initial draft of this doc scoped the constraint vocabulary down to whatever one sample handler needed (datatype, class, cardinality, pattern), which is the wrong lens for a general-purpose validation library. **This package's coverage target is SHACL itself, not any particular consuming application** — verified against the actual installed `dotNetRdf.Shacl` assembly (see *Data model*), which implements SHACL Core in full plus SPARQL-based constraint components and the complete property-path grammar. Scoping the type system to less than that would be re-guessing at the target the same way Plan 4 guessed at representation before proving it.

### Reference specifications

| Spec | Version | Media type |
|---|---|---|
| SHACL Core | [W3C Recommendation](https://www.w3.org/TR/shacl/#core-components) | — (constraint vocabulary) |
| SHACL-SPARQL | [W3C Recommendation, §5-6](https://www.w3.org/TR/shacl/#sparql-constraints) | — |
| RDF 1.1 / JSON-LD 1.1 | inherited from [Frank.Rdf](2026-07-30-frank-rdf-design.md) | `application/ld+json` |
| SPARQL 1.1 Query Language | [W3C Recommendation](https://www.w3.org/TR/sparql11-query/) | — |
| dotNetRDF / dotNetRdf.Shacl | 3.5.x | — (library, not a wire spec) |

## Goals

1. A typed, hand-authored SHACL model covering **SHACL Core in full** (value type, cardinality, value range, string-based, property pair, logical, shape-based, closedness, and target constraints) plus **SPARQL-based constraint components** and the **full property-path grammar** — "illegal states unrepresentable" per case, matching Plan 4's credited design, extended to the vocabulary's actual breadth rather than one consumer's needs.
2. A closed `Shacl` interpreter (`toDoc`/`toShapesGraph`) projecting that model onto `Frank.Rdf`'s `Doc`/`describe`/blank-node machinery — the single graph-builder, no parallel triple model, no `buildShapesGraph` duplicate.
3. A typed `validate` wrapper over dotNetRDF's raw `Report`, never exposing `VDS.RDF.Shacl.Validation.Result` directly to callers.
4. `resource { useValidation }` middleware validating `application/ld+json` request bodies, 422 on violation with a dual-path response (real `sh:ValidationReport` graph vs. Problem Details), matching the content-negotiation pattern `Frank.Rdf` already proved.
5. An authoring surface with two parts, following `Frank.Alps`'s `DescriptorBuilder` precedent exactly: plain, curried, pipe-able functions as the real model (`ShapeSpecFunctions`, mirroring `DescriptorFunctions`), and CE sugar over them (`shape { }`/`property { }`, mirroring `DescriptorBuilder`) wherever a builder has enough real operations to earn its keep — not wherever one might hypothetically be added later.

## Non-goals

- **Codegen or reflection-based type→shape mapping**, in any form — the whole reason this design exists.
- **SHACL-JS** (`sh:js`, JavaScript-backed constraint components) — genuinely infeasible today, not a scope call: there is no JavaScript execution engine anywhere in this codebase's dependency graph, unlike SPARQL-based constraints which reuse infrastructure `Frank.Provenance` already proved out.
- **Non-validating shape characteristics** (`sh:name`, `sh:description`, `sh:order`, `sh:group`, `sh:defaultValue`) — these describe a shape for UI/documentation generation and never affect conformance. Out of scope because this package's stated purpose is validation, not because no current consumer needs them; a future `Frank.Validation.Presentation`-shaped need would be a different package's concern, not a reason to grow this one's `PropertyShapeSpec`.
- **A functional dependency on `Frank.Provenance`.** Same architectural family (typed model → vocabulary functions → `Doc`, `ProjectReference` to `Frank.Rdf` only), zero coupling — matching `Frank.Provenance`'s own non-goal about sibling packages. A consuming application recording a provenance entry on validation failure wires that itself.
- **Compact-form or `@context`-bearing request bodies.** Expanded-form only, matching `Frank.Rdf`'s own stance — and it means no document-loader/context-fetch surface at all in the request-validation path.
- **Durable shape storage or a shape registry service.** Shapes are values, authored and held by the consuming application; this package has no opinion on where a `ShapeDecl list` lives between requests.

## Package shape

`src/Frank.Validation/`, targeting `net8.0;net9.0;net10.0` (matching Frank core, `Frank.Rdf`, `Frank.Provenance`). Depends on `Frank`, `Frank.Rdf`, `dotNetRdf.Core` (transitive via `Frank.Rdf`), and `dotNetRdf.Shacl` 3.5.1 (the SHACL `ShapesGraph`/`Report`/constraint-component engine — confirmed present: `ClassConstraintComponent`, `DatatypeConstraintComponent`, `NodeKindConstraintComponent`, `MinCount`/`MaxCountConstraintComponent`, `MinExclusive`/`MinInclusive`/`MaxExclusive`/`MaxInclusiveConstraintComponent`, `MinLength`/`MaxLengthConstraintComponent`, `PatternConstraintComponent`, `LanguageInConstraintComponent`, `UniqueLangConstraintComponent`, `Equals`/`Disjoint`/`LessThan`/`LessThanOrEqualsConstraintComponent`, `NodeConstraintComponent`, `QualifiedValueShape(Disjoint)`, `HasValueConstraintComponent`, `InConstraintComponent`, `And`/`Or`/`Not`/`XoneConstraintComponent`, `ClosedConstraintComponent`, `SparqlConstraintComponent`/`SparqlAskValidator`, and `VDS.RDF.Shacl.Paths` — `SequencePath`/`AlternativePath`/`InversePath`/`ZeroOrMorePath`/`OneOrMorePath`/`ZeroOrOnePath` — all verified directly against the installed assembly).

```
ShapeTypes.fs               XsdDatatype, NodeKind, Severity, TargetSpec, PropertyPath, PropertyConstraint,
                             SparqlConstraint, PropertyShapeSpec, NodeShapeSpec, ShapeDecl, NonEmptyList
ShapeSpec.fs                ShapeSpecFunctions -- plain curried functions, the real authoring model
ShapeBuilder.fs             shape{ }/property{ } CEs -- sugar over ShapeSpecFunctions
Shacl.fs                    the interpreter: toDoc, toShapesGraph, reportToDoc
Validation.fs                Violation, ValidationOutcome, validate
ResourceBuilderExtensions.fs  `useValidation shapesGraph` on resource{ } -- attaches ValidationMetadata
                               (internal, mirrors ResourceLinkProvider) via ResourceBuilder.AddMetadata.
                               Type extension, not a Frank core change -- same mechanism Frank.JsonHome's
                               ResourceBuilderExtensions.fs and Frank.OpenApi's WebHostBuilderExtensions.fs
                               already use for rel/hrefVar/docs/useOpenApi.
WebHostBuilderExtensions.fs   `useValidation` on webHost{ } -- registers the one app-wide interceptor
                               middleware (app.Use(fun ctx next -> ...), mirroring WebLink.useResourceScopedLinks'
                               ctx.GetEndpoint().Metadata.GetMetadata<T>() pattern) that actually buffers,
                               parses, and validates POST/PUT/PATCH application/ld+json bodies against
                               whichever resource's ValidationMetadata matched.
```

Each `.fs` gets a matching `.fsi`, per `CLAUDE.md`. `ResourceBuilder`/`WebHostBuilder` are both `[<Sealed>]` in Frank core, but F#'s `type X with [<CustomOperation>] member ...` extension syntax adds real custom operations to a sealed type from another assembly — confirmed against `Frank.JsonHome/ResourceBuilderExtensions.fs` (`rel`/`hrefVar`/`docs`/...) and `Frank.OpenApi/WebHostBuilderExtensions.fs` (`useOpenApi`). No Frank core change needed; `link`'s move into core itself was a deliberate exception because it was shared by `Frank.Rdf`, `Frank.JsonHome`, and more — not the default for a single package's own operation.

## The design

### Data model (`ShapeTypes.fs`)

```fsharp
[<Struct; RequireQualifiedAccess>]
type XsdDatatype = Integer | Long | Decimal | Double | Boolean | String | DateTime

[<Struct; RequireQualifiedAccess>]
type NodeKind = BlankNode | Iri | Literal | BlankNodeOrIri | BlankNodeOrLiteral | IriOrLiteral

[<Struct; RequireQualifiedAccess>]
type Severity = Violation | Warning | Info

type NonEmptyList<'T> = { Head: 'T; Tail: 'T list }
module NonEmptyList =
    val ofList: 'T list -> NonEmptyList<'T> option
    val toList: NonEmptyList<'T> -> 'T list

/// sh:path -- not always a single predicate. The full SHACL property-path grammar,
/// matching dotNetRdf.Shacl.Paths (SequencePath/AlternativePath/InversePath/
/// ZeroOrMorePath/OneOrMorePath/ZeroOrOnePath).
[<RequireQualifiedAccess>]
type PropertyPath =
    | Predicate of Uri
    | Inverse of PropertyPath
    | Sequence of NonEmptyList<PropertyPath>
    | Alternative of NonEmptyList<PropertyPath>
    | ZeroOrMore of PropertyPath
    | OneOrMore of PropertyPath
    | ZeroOrOne of PropertyPath

[<RequireQualifiedAccess>]
type TargetSpec =
    | Class of Uri
    | Node of Node
    | SubjectsOf of Uri
    | ObjectsOf of Uri

/// An author-supplied SPARQL ASK query as a SHACL-SPARQL constraint (sh:sparql).
/// The query text is written by the shape's author (a developer), never derived
/// from request input -- same trust boundary as a hand-written sh:pattern regex.
type SparqlConstraint =
    { Query: string
      Message: string option
      Prefixes: (string * string) list }

[<RequireQualifiedAccess>]
type PropertyConstraint =
    | Class of Uri
    | Datatype of XsdDatatype
    | NodeKind of NodeKind
    | MinCount of int
    | MaxCount of int
    | MinExclusive of Literal
    | MinInclusive of Literal
    | MaxExclusive of Literal
    | MaxInclusive of Literal
    | MinLength of int
    | MaxLength of int
    | Pattern of pattern: string * flags: string option
    | LanguageIn of NonEmptyList<string>
    | UniqueLang of bool
    | Equals of Uri
    | Disjoint of Uri
    | LessThan of Uri
    | LessThanOrEquals of Uri
    | Node of ShapeDecl                    // recursive: value must conform to another shape
    | QualifiedValueShape of shape: ShapeDecl * minCount: int option * maxCount: int option * disjoint: bool
    | HasValue of Value
    | AllowedValues of NonEmptyList<Value>  // sh:in -- renamed only to dodge the `in` keyword
    | Sparql of SparqlConstraint

and PropertyShapeSpec =
    { Path: PropertyPath
      Constraints: PropertyConstraint list
      Severity: Severity option    // None = sh:Violation, SHACL's own default
      Message: string option }

and NodeShapeSpec =
    { Targets: TargetSpec list    // empty is valid -- a shape referenced only via sh:node/
                                   // sh:qualifiedValueShape (never independently targeted) has none
      Properties: PropertyShapeSpec list
      Closed: bool
      IgnoredProperties: Uri list
      Severity: Severity option
      Message: string option }

/// A total DU over every top-level SHACL shape form this package supports:
/// a plain node shape, a nullary-union sh:in shorthand, and the four logical
/// combinators, each composing other ShapeDecls rather than duplicating their fields.
and ShapeDecl =
    | RecordShape of NodeShapeSpec
    | EnumShape of targetClass: Uri * cases: NonEmptyList<Uri>
    | And of NonEmptyList<ShapeDecl>
    | Or of NonEmptyList<ShapeDecl>
    | Not of ShapeDecl
    | Xone of NonEmptyList<ShapeDecl>
```

`Node`/`Literal`/`Value` are `Frank.Rdf`'s own types, reused directly throughout (range/`in`/`hasValue` constraints all need literal-or-node values) — no parallel value model, same discipline `Frank.Provenance` followed.

`Datatype`/`Class`/`NodeKind` aren't mutually type-exclusive within `PropertyConstraint list` (a caller could list more than one value-type constraint); SHACL itself permits this (they combine, not override), so nothing here needs to forbid it.

### Authoring surface

**`ShapeSpec.fs`** — plain curried functions, kept to the ones that construct a genuinely new value or combine data non-trivially. Simple field mutation on an already-existing value doesn't get a named counterpart here — see the CE section below for why:

```fsharp
module ShapeSpecFunctions =
    val ofPath: PropertyPath -> PropertyShapeSpec
    /// The one general-purpose accumulator every per-constraint CE operation is
    /// sugar over. Because PropertyConstraint is already a closed, named DU, this
    /// IS the plain-function API for adding a constraint -- `p |> addConstraint
    /// (PropertyConstraint.Datatype XsdDatatype.Integer)` -- with no need for a
    /// same-named `datatype` wrapper function duplicating what the DU case name
    /// already says.
    val addConstraint: PropertyConstraint -> PropertyShapeSpec -> PropertyShapeSpec

    val recordShape: TargetSpec list -> PropertyShapeSpec list -> ShapeDecl
    val enumShape: targetClass: Uri -> head: Uri -> tail: Uri list -> ShapeDecl
    /// Convenience for the common single-class-target case: `targetClass uri = [ TargetSpec.Class uri ]`.
    val targetClass: Uri -> TargetSpec list
```

**`ShapeBuilder.fs`** — CE sugar, mirroring `Frank.Alps`'s `DescriptorBuilder` and (even more directly — it lands on `origin/master` mid-design, rebased in) `Frank.Provenance`'s new `ProvBuilder`: one builder per node type, constructor takes the already-built `initial` value directly (`ProvBuilder(initial: Description)`, entry functions doing `ProvBuilder(Prov.activity id)`) rather than re-deriving a default from a raw identity parameter inside `Yield`/`Zero` — `Yield`/`Zero` both just return `initial`. Nesting is via a plain list passed to one op (matching `contains`/`regions`/`from`, not a repeated single-item op). Following `DescriptorBuilder`'s own split, not every custom operation delegates to a named function: `properties`/`closed`/`severity`/`message` are simple field mutations, inlined directly in the member body (the same category as `DescriptorBuilder`'s `Semantic`/`Safe`/`Initial`, which have no `DescriptorFunctions` counterpart either), while every per-constraint operation is one line of `addConstraint` (the same shape as `ProvBuilder`'s `d |> Prov.wasGeneratedBy activity`):

```fsharp
[<CustomOperation("datatype")>]
member _.Datatype(p, dt: XsdDatatype) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.Datatype dt)

[<CustomOperation("minCount")>]
member _.MinCount(p, n: int) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.MinCount n)

[<CustomOperation("node")>]
member _.NodeOp(p, s: ShapeDecl) : PropertyShapeSpec = p |> addConstraint (PropertyConstraint.Node s)

[<CustomOperation("severity")>]
member _.SeverityOp(p, sev: Severity) : PropertyShapeSpec = { p with Severity = Some sev }
```
```fsharp
[<CustomOperation("properties")>]
member _.Properties(d, props: PropertyShapeSpec list) : ShapeDecl =
    match d with
    | RecordShape n -> RecordShape { n with Properties = n.Properties @ props }
    | other -> other   // shape{ } only ever builds RecordShape via Yield/Zero, so this arm is unreachable in practice

[<CustomOperation("closed")>]
member _.Closed(d, ignoredProperties: Uri list) : ShapeDecl =
    match d with
    | RecordShape n -> RecordShape { n with Closed = true; IgnoredProperties = ignoredProperties }
    | other -> other
```

Every other constraint operation (`ofClass`, `nodeKind`, `maxCount`, `minLength`/`maxLength`, `minExclusive`/`minInclusive`/`maxExclusive`/`maxInclusive`, `pattern`/`patternWithFlags`, `languageIn`, `uniqueLang`, `equalsPath`, `disjoint`, `lessThan`, `lessThanOrEquals`, `qualifiedValueShape`, `hasValue`, `allowedValues`, `sparqlConstraint`, and `ShapeBuilder`'s `message`) follows the same one-line `addConstraint (PropertyConstraint.Case ...)` or direct-field-update shape as the four shown above.

```fsharp
[<Sealed>]
type PropertyShapeBuilder =
    new: initial: PropertyShapeSpec -> PropertyShapeBuilder
    member Yield: 'a -> PropertyShapeSpec
    member Zero: unit -> PropertyShapeSpec
    member Run: PropertyShapeSpec -> PropertyShapeSpec

    [<CustomOperation("datatype")>]         member Datatype: PropertyShapeSpec * XsdDatatype -> PropertyShapeSpec
    [<CustomOperation("ofClass")>]          member OfClass: PropertyShapeSpec * Uri -> PropertyShapeSpec
    [<CustomOperation("nodeKind")>]         member NodeKindOp: PropertyShapeSpec * NodeKind -> PropertyShapeSpec
    [<CustomOperation("minCount")>]         member MinCount: PropertyShapeSpec * int -> PropertyShapeSpec
    [<CustomOperation("maxCount")>]         member MaxCount: PropertyShapeSpec * int -> PropertyShapeSpec
    [<CustomOperation("minLength")>]        member MinLength: PropertyShapeSpec * int -> PropertyShapeSpec
    [<CustomOperation("maxLength")>]        member MaxLength: PropertyShapeSpec * int -> PropertyShapeSpec
    [<CustomOperation("minExclusive")>]     member MinExclusive: PropertyShapeSpec * Literal -> PropertyShapeSpec
    [<CustomOperation("minInclusive")>]     member MinInclusive: PropertyShapeSpec * Literal -> PropertyShapeSpec
    [<CustomOperation("maxExclusive")>]     member MaxExclusive: PropertyShapeSpec * Literal -> PropertyShapeSpec
    [<CustomOperation("maxInclusive")>]     member MaxInclusive: PropertyShapeSpec * Literal -> PropertyShapeSpec
    [<CustomOperation("pattern")>]          member Pattern: PropertyShapeSpec * string -> PropertyShapeSpec
    [<CustomOperation("patternWithFlags")>] member PatternWithFlags: PropertyShapeSpec * string * string -> PropertyShapeSpec
    [<CustomOperation("languageIn")>]       member LanguageIn: PropertyShapeSpec * NonEmptyList<string> -> PropertyShapeSpec
    [<CustomOperation("uniqueLang")>]       member UniqueLang: PropertyShapeSpec * bool -> PropertyShapeSpec
    [<CustomOperation("equalsPath")>]       member EqualsPath: PropertyShapeSpec * Uri -> PropertyShapeSpec
    [<CustomOperation("disjoint")>]         member Disjoint: PropertyShapeSpec * Uri -> PropertyShapeSpec
    [<CustomOperation("lessThan")>]         member LessThan: PropertyShapeSpec * Uri -> PropertyShapeSpec
    [<CustomOperation("lessThanOrEquals")>] member LessThanOrEquals: PropertyShapeSpec * Uri -> PropertyShapeSpec
    [<CustomOperation("node")>]             member NodeOp: PropertyShapeSpec * ShapeDecl -> PropertyShapeSpec
    [<CustomOperation("qualifiedValueShape")>] member QualifiedValueShape: PropertyShapeSpec * ShapeDecl * int option * int option * bool -> PropertyShapeSpec
    [<CustomOperation("hasValue")>]         member HasValue: PropertyShapeSpec * Value -> PropertyShapeSpec
    [<CustomOperation("allowedValues")>]    member AllowedValues: PropertyShapeSpec * NonEmptyList<Value> -> PropertyShapeSpec
    [<CustomOperation("sparqlConstraint")>] member SparqlConstraintOp: PropertyShapeSpec * SparqlConstraint -> PropertyShapeSpec
    [<CustomOperation("severity")>]         member SeverityOp: PropertyShapeSpec * Severity -> PropertyShapeSpec
    [<CustomOperation("message")>]          member MessageOp: PropertyShapeSpec * string -> PropertyShapeSpec

/// `property path { ... } = PropertyShapeBuilder(ofPath path) { ... }` -- mirrors `ProvBuilder`'s
/// `let activity id = ProvBuilder(Prov.activity id)`.
val property: path: PropertyPath -> PropertyShapeBuilder

[<Sealed>]
type ShapeBuilder =
    new: initial: ShapeDecl -> ShapeBuilder
    member Yield: 'a -> ShapeDecl
    member Zero: unit -> ShapeDecl
    member Run: ShapeDecl -> ShapeDecl

    [<CustomOperation("properties")>] member Properties: ShapeDecl * PropertyShapeSpec list -> ShapeDecl
    [<CustomOperation("closed")>]     member Closed: ShapeDecl * ignoredProperties: Uri list -> ShapeDecl
    [<CustomOperation("severity")>]   member SeverityOp: ShapeDecl * Severity -> ShapeDecl
    [<CustomOperation("message")>]    member MessageOp: ShapeDecl * string -> ShapeDecl

/// `shape targets { ... } = ShapeBuilder(recordShape targets []) { ... }`.
val shape: targets: TargetSpec list -> ShapeBuilder
```

`shape { }` takes a `TargetSpec list`, which is empty for a shape meant only to be referenced via `sh:node`/`sh:qualifiedValueShape` and never independently targeted. The common case (`TargetSpec.Class`-only) gets a convenience helper (`targetClass uri`) producing that single-element list, so the common path still reads as one argument.

`shape { }` earns a CE here (four real operations, matching the bar `DescriptorBuilder`'s eighteen operations set) where the earlier one-operation version didn't. `And`/`Or`/`Not`/`Xone` stay construction-only (`ShapeDecl.And { Head = ...; Tail = [...] }`) — they compose already-built `ShapeDecl` values, which is exactly a bare-list case like `EnumShape`, not a multi-field record needing its own builder.

Example, using every category at least once:

```fsharp
let personShape =
    shape (targetClass (Uri "https://schema.org/Person")) {
        properties [
            property (PropertyPath.Predicate(Uri "https://schema.org/email")) {
                datatype XsdDatatype.String
                pattern @"^\S+@\S+\.\S+$"
                minCount 1
            }
            property (PropertyPath.Predicate(Uri "https://schema.org/birthDate")) {
                datatype XsdDatatype.DateTime
                maxCount 1
            }
        ]
        closed []
    }

// personShape above has its own TargetSpec.Class -- it validates standalone AND nests here.
// A shape meant only for nesting (never independently targeted) would instead use `shape [] { ... }`.
let moveShape =
    shape (targetClass (Uri "https://schema.org/MoveAction")) {
        properties [
            property (PropertyPath.Predicate(Uri "https://schema.org/position")) {
                datatype XsdDatatype.Integer
                minCount 1
                maxCount 1
            }
            property (PropertyPath.Predicate(Uri "https://schema.org/agent")) {
                node personShape   // value must conform to personShape
                minCount 1
                maxCount 1
            }
        ]
    }
```

### Interpreter (`Shacl.fs`)

```fsharp
module Shacl =
    /// Projects a ShapeDecl list onto a Doc: one sh:NodeShape/sh:PropertyShape pair
    /// per shape via describe/property, blank nodes (Node.blank()) for anonymous
    /// property shapes and path expressions, well-formed rdf:first/rdf:rest lists
    /// for sh:in/sh:languageIn/multi-target/logical-combinator members -- the same
    /// well-formed-list discipline Plan 4 already got right for sh:in.
    val toDoc: ShapeDecl list -> Doc
    val toShapesGraph: ShapeDecl list -> VDS.RDF.Shacl.ShapesGraph   // toDoc >> Doc.toGraph >> ShapesGraph

    /// Projects a Violation list back onto a Doc as a real sh:ValidationReport --
    /// the inverse direction, used by the 422 dual-path response.
    val reportToDoc: Violation list -> Doc
```

This is the credited, reused piece of Plan 4, retargeted onto `Frank.Rdf`'s `Doc`/`describe`/blank-node machinery instead of a standalone `Triples` helper (satisfying "built on top of Frank.Rdf," which Plan 4 never did — it predated `Frank.Rdf`'s existence). Complex `PropertyPath` values become the corresponding `sh:alternativePath`/`sh:inversePath`/`sh:zeroOrMorePath`/etc. blank-node structures dotNetRDF's own path types expect; `SparqlConstraint` becomes an `sh:sparql` blank node with `sh:select`/`sh:ask` (whichever the query text parses as) plus declared prefixes.

### Validation execution (`Validation.fs`)

```fsharp
type Violation =
    { FocusNode: Node
      ResultPath: PropertyPath option
      Severity: Severity
      Message: string
      ConstraintComponent: Uri
      SourceShape: Node }

[<RequireQualifiedAccess>]
type ValidationOutcome =
    | Conforms
    | Violates of Violation list

module Shacl =
    val validate: VDS.RDF.Shacl.ShapesGraph -> VDS.RDF.IGraph -> ValidationOutcome
```

A typed wrapper over dotNetRDF's `Report`/`Validation.Result`, matching how `Frank.Provenance` never exposes a raw `SparqlResultSet` without wrapping it in `SparqlQueryResult`.

### HTTP surface

Two pieces, both type extensions living in `Frank.Validation` (no Frank core change — see *Package shape*):

- **`resource { useValidation shapesGraph }`** — a `ResourceBuilder` extension. Attaches an internal `ValidationMetadata` (wrapping the `ShapesGraph`, mirroring `Frank`'s own internal `ResourceLinkProvider`) to the resource's endpoints via the existing public `ResourceBuilder.AddMetadata` static member — the same mechanism `Frank.JsonHome`'s `rel`/`hrefVar`/`docs` extensions already use. Declarative only; it does nothing at request time by itself.
- **`webHost { useValidation }`** — a `WebHostBuilder` extension, called once at app startup. Registers the one app-wide interceptor: `app.Use(fun ctx next -> ...)`, reading `ctx.GetEndpoint().Metadata.GetMetadata<ValidationMetadata>()` exactly the way `Frank`'s own `WebLink.useResourceScopedLinks` reads `ResourceLinkProvider` off the matched endpoint. When present, and the request is POST/PUT/PATCH with `Content-Type: application/ld+json`: buffers the body once (`ctx.Request.EnableBuffering()`; checked against `Content-Length` up front, and against a running byte count while reading in case `Content-Length` is absent, since it's client-supplied and not trustworthy alone) with a 413 short-circuit over the configured max; parses via `JsonLdParser().Load(store, reader)` (already proven in `RoundTripTests.fs` — no document loader needed, since expanded-form input carries no `@context` to resolve, see Non-goals), 400 on parse failure; runs `Shacl.validate`. On `Conforms`, the parsed graph is stashed on `HttpContext.Items` (so the handler doesn't re-parse) and `next.Invoke ctx` continues the pipeline. On `Violates`, short-circuits with 422 — never calls `next` — negotiated the same way `Frank.Rdf.Sample`'s `getGame` negotiates: `application/ld+json` gets `Shacl.reportToDoc violations |> Doc.writeJsonLd` (a real `sh:ValidationReport`), anything else gets `application/problem+json` with a flattened violations array (`focusNode`, `resultPath`, `severity`, `message`, `constraintComponent`). When no `ValidationMetadata` is present on the matched endpoint, or the method/content-type don't match, the middleware is a no-op pass-through — every request still flows through it (it's app-wide), so this path must stay cheap.

No existing body-buffering/413 helper exists anywhere in the current codebase to reuse (the earlier such helper was part of the rolled-back v7.3.0 work) — this is written fresh.

### Error handling and edge cases

| Situation | Behavior |
|---|---|
| `useValidation` on a resource with no POST/PUT/PATCH handler | No-op — nothing to guard |
| Body isn't `application/ld+json` on a validated method | Passes through unvalidated — this middleware only ever narrows `application/ld+json` |
| Malformed JSON-LD (parse failure) | 400 Problem Details, distinct from 422 — a parse failure isn't a SHACL violation |
| Body exceeds the size guard | 413, before parsing is attempted |
| Empty body on a validated method | Parses to an empty graph; conforms trivially unless a shape targets via `TargetSpec.Node`, which an empty graph can't satisfy either way |
| `Datatype`/`Class`/`NodeKind` combined on one property | Not mutually exclusive — SHACL combines them, matching the underlying spec, not forbidden here |
| `Sparql` constraint query fails to parse | Raised at `toShapesGraph` build time (shape-authoring time), never deferred to request-validation time — a malformed author-supplied query is a shape bug, not a per-request condition |
| Recursive `Node`/`QualifiedValueShape` cycle (shape A references shape B references shape A) | Not statically prevented — SHACL itself permits recursive shapes and defines validation over them; delegated entirely to `dotNetRdf.Shacl`'s own recursion handling, not reimplemented here |

### Testing

Mirrors `Frank.Rdf.Tests`/`Frank.Provenance.Tests`: `ShapeTypes` unit tests (record/DU construction, `NonEmptyList`); `Shacl.toDoc`/`toShapesGraph` triple-shape tests per constraint category (value type, cardinality, range, string-based, property pair, logical, shape-based, `sh:sparql`, property paths) — not string comparison; `Shacl.validate` conformance tests, both conforming and violating, per constraint kind, including at least one recursive `Node` case and one `And`/`Or`/`Not`/`Xone` composition; round-trip (`toDoc |> Doc.toJsonLd`, reparsed, isomorphic); HTTP surface via `TestHost` — conforms passes through, violates 422s, dual-path negotiation, 413/400 edge cases from the table above.

### Sample

`sample/Frank.Validation.Sample` — same `games` dict as `Frank.Rdf.Sample`/`Frank.Provenance.Sample`. `POST /games/{id}/moves` accepts a `schema:MoveAction` body (`schema:position: int`, `schema:agent` conforming to a nested `Person` shape — demonstrating `datatype`, `minCount`/`maxCount`, and recursive `node`), validated via `useValidation`, 422 dual-path on a bad move. A second shape demonstrates `closed`, to prove that constraint independent of the recursive one.

## Implementation order

1. **`ShapeTypes.fs`** — the full data model, unit tests for construction only (no serialization).
2. **`ShapeSpec.fs`** — plain functions, one test per function.
3. **`Shacl.fs`: `toDoc`/`toShapesGraph`** — one constraint category at a time (value type → cardinality → range → string-based → property pair → shape-based/recursive → logical → `sh:sparql` → property paths), each verified against the built `Graph`'s triples before moving to the next category.
4. **`Validation.fs`: `validate`** — conformance tests per category, reusing category groupings from step 3.
5. **`ShapeBuilder.fs`** — CE sugar over `ShapeSpec.fs`, once the underlying functions are proven.
6. **`Shacl.reportToDoc` + `ValidationMiddleware.fs`** — 422 dual-path, `useValidation`.
7. **tic-tac-toe-style sample** — `Frank.Validation.Sample`.

Each stage independently verifiable, matching how `Frank.Rdf` and `Frank.Provenance` were staged.

## Future work (separate)

- **SHACL-JS** — revisit only if a real consumer needs it and a .NET-embeddable JS engine becomes an acceptable dependency; not designed around speculatively.
- **Non-validating shape characteristics** (`sh:name`/`sh:description`/`sh:order`/`sh:group`/`sh:defaultValue`) — a presentation/documentation concern, potentially a separate package if ever needed.
- **`Frank.Provenance` integration** — recording a provenance entry on validation failure, left to the consuming application to wire, per Non-goals.
- **Shape registries / durable shape storage** — out of scope; shapes are values today.

## Sources

- W3C SHACL: https://www.w3.org/TR/shacl/
- [Frank.Rdf design](2026-07-30-frank-rdf-design.md) — foundation this package builds on.
- [Frank.Provenance design](2026-08-02-frank-provenance-design.md) — sibling package, architectural precedent (adjacent, not a dependency).
- `src/Frank.Alps/DescriptorBuilder.fs`/`.fsi` and `src/Frank.Provenance/ProvBuilder.fs`/`.fsi` — the CE-mirrors-plain-functions pattern this design's authoring surface follows; `ProvBuilder` landed on `origin/master` mid-design (rebased in) and is the more current instance of the same pattern.
- `src/Frank.JsonHome/ResourceBuilderExtensions.fs`/`.fsi` and `src/Frank.OpenApi/WebHostBuilderExtensions.fs`/`.fsi` — the type-extension mechanism `useValidation`'s two pieces follow, confirming custom operations can be added to `Frank`'s sealed `ResourceBuilder`/`WebHostBuilder` from another package without a Frank core change.
- `src/Frank/WebLink.fs` (`WebLink.useResourceScopedLinks`) — the app-wide-middleware-reads-endpoint-metadata pattern `useValidation`'s interceptor follows.
- `docs/superpowers/plans/2026-06-22-v732-codegen-remediation-plan4-validation.md` (prior attempt, reference only, not a starting point) — credited for the `ShapeDecl`/interpreter shape; not credited for scope or for building codegen first.
- Installed `dotNetRdf.Shacl` assembly — verified directly for constraint-component and property-path coverage (see *Package shape*).
