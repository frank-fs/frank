# Frank.Alps — ALPS profiles, authored, with room for hierarchy and orthogonality

**Date**: 2026-08-02
**Branch**: `worktree-alps`
**Status**: Draft — awaiting review

## Context

Supersedes [2026-07-28-frank-alps-protocol-design.md](2026-07-28-frank-alps-protocol-design.md) (branch `json-home`, not otherwise recoverable — the branch is gone, the doc survived only because it was committed to `master`). That doc's two prerequisites have since shipped: extensible `HandlerDefinition` (`{ Handler; Metadata: obj list }`, `src/Frank/HandlerDefinition.fs`) and `WebLink`/`IResponseLinkProvider` (`src/Frank/WebLink.fs`, app-wide and resource-scoped `Link` header advertisement), both proven in production by [Frank.JsonHome](2026-07-28-frank-jsonhome-design.md) and [Frank.Rdf](2026-07-30-frank-rdf-design.md).

Also carried forward: two comments on frank-fs/frank#471 (2026-08-02), written while [Frank.Provenance](2026-08-02-frank-provenance-design.md) was being designed in a parallel session, proposing how ALPS and Provenance interoperate without a dependency either direction — see *State-based filtering* below, which adopts that proposal directly.

### Prior attempt, mined not ported

`feature/v7.3.2`'s ancestor line (`v7.3.0`/`v7.3.1`, rolled back per `[[project_v3_rollback]]`) built a much larger system: `docs/superpowers/specs/2026-04-21-v740-protocol-types-design.md`, a generated-actor / hierarchical-statechart / multiparty-session-types / Z3-verified design. It was never completed and this design does not resume it — codegen was built before the model was proven (`[[feedback_outside_in_before_codegen]]`), and role/session-type machinery (`ProtocolType<'Role,'Scope,'Message,'Effect>`, the Li-et-al. global-automaton projection algorithm) added verification power this design deliberately does not need. What's mined, credited, and reused:

- **`protocolState`/`availableInStates` ALPS `ext` elements** (PR #214/#207, canonical URIs minted in PR #165 under `https://frank-fs.github.io/alps-ext/`) — real, shipped, spec-faithful use of ALPS's own extension mechanism to carry state information in the wire format. This design continues that URI namespace for its own new `ext` markers (`from`, `initial`, `orthogonal`) rather than inventing a new one.
- **`Transition<'State,'Message> = { FromState; Message; ToState }`** — not from the rolled-back line at all, but from an earlier, simpler source: [wizardsofsmart.wordpress.com, 2018](https://wizardsofsmart.wordpress.com/2018/12/05/state-transitions-through-sequence-diagrams/), which this repo already built tooling around once (`007-wsd-lexer-parser-ast`, also rolled back). `ProtocolTransition` below is this shape, generalized to ALPS descriptors.
- **Per-role profile projection** (PRs #172, #178, #274, #169 — projection operator, projected-conneg middleware, role-based affordance projection, typed extension vocabulary) is *not* reused mechanically. This design's answer to multi-party protocols (see *Multi-party protocols* below) is deliberately weaker and simpler: independently hand-authored per-role documents, no unifying global protocol type, no projection algorithm, no duality/consistency verification. The old work solved a harder problem than this design takes on.

### Reference specifications

| Spec | Version | Media type |
|---|---|---|
| Application-Level Profile Semantics | [draft-amundsen-richardson-foster-alps-07](https://datatracker.ietf.org/doc/draft-amundsen-richardson-foster-alps/) | `application/alps+json` |
| The 'profile' Link Relation Type | [RFC 6906](https://www.rfc-editor.org/rfc/rfc6906) | — |
| Web Linking | [RFC 8288](https://www.rfc-editor.org/rfc/rfc8288) (draft-07 supersedes -04's RFC 5988 reference) | — |

Confirmed against the actual spec text (fetched during design, not assumed from memory): `def` was added between draft-04 and draft-07; `tag` (on `descriptor`/`doc`/`ext`/`link`) and `contentType` (on `doc`) are also -07 additions. There is no built-in ALPS mechanism for composing or importing across documents beyond descriptor `href` (a descriptor inherits all attributes from the descriptor its `href` points to; local properties override); nesting (§2.2.4) is general — any descriptor type may nest under any other, siblings and recursive children inherit visibility, and a nested descriptor "SHOULD NOT" be assumed reachable from outside its parent unless referenced via another descriptor's `href`.

## Goals

1. Author ALPS profiles as hand-authored F# `Descriptor` values — via plain `|>` combinators or an equivalent `descriptor { }` computation expression (see *Two authoring surfaces*) — never derived from CLR types or view templates (see *Why vocabulary is authored, not derived* in the superseded doc; unchanged).
2. Full draft-07 field coverage — `id`, `name`, `type`, `def`, `doc` (with `href`/`format`/`contentType`), `ext` (with `href`/`value`/`tag`), `href` (descriptor inheritance), `link` (with `title`/`tag`), `rel`, `rt`, `tag`, nested `descriptor`.
3. Compile-time-checked references wherever there is something in-process to check against (`rt`, `from`, `href`, `contains`); plain strings/URIs only where there is nothing to check (`hrefExternal`, a descriptor's own `id`, `def`).
4. Leave room, in the wire format and the type model, for hierarchical (composite/substate) and orthogonal (parallel-region) protocol structure to be *authored* today, independent of whether anything ever executes it — without a breaking rewrite if execution is pursued later.
5. A seam (`CurrentStateResolver`) for state-based affordance filtering, wired by the consuming application at composition time — zero dependency on `Frank.Provenance` or any other package, mirroring `Frank.Provenance`'s own `ActivityTypeResolver` seam pointed the other direction.
6. Two HTTP exposures: one static, auth-filtered, app-wide profile, and one per-resource, content-negotiated, auth-*and*-state-filtered excerpt.

## Non-goals

- **Codegen or reflection-based derivation of descriptors from CLR types**, in any form.
- **A runtime state-machine executor, generated actors, Z3 verification, or multiparty session types.** Mined from, but not resuming, the rolled-back `v740` line. `Frank.Alps` produces a document and a read-only derived graph; nothing in this package executes a transition or owns "what state is a resource actually in" — that stays external (e.g., `Frank.Provenance`).
- **Hard dependency on `Frank.Provenance`, `Frank.Rdf`, or `Frank.OpenApi`.**
- **Per-resource or multi-document profiles in v1.** One application-wide profile at `/.well-known/alps.json`. The `href`/`hrefExternal` split exists specifically so this can be added later without changing the `Descriptor` type — see *Descriptor references*.
- **Conjunctive (AND-guard) or multi-region-fan-out transitions.** `ProtocolTransition` expresses one `FromState`/`ToState` pair per edge; a transition requiring multiple orthogonal regions to simultaneously satisfy a guard, or one that enters several regions at once, needs a distinct wrapper type layered alongside `ProtocolTransition` — not designed here (see *Reviewed against Harel's formalism*).
- **History states, guard conditions, entry/exit actions, run-to-completion/event-queue semantics.** All additive-if-ever-pursued (per the same review), none designed here.
- **Automatic multi-party projection.** No unifying `ProtocolType<'Role,...>`, no derivation/projection algorithm. See *Multi-party protocols*.
- **`CurrentStateResolver` consuming orthogonal regions.** `initial` and `regions` are themselves in scope and fully designed (see *Composite states*) — this bullet is narrower: `CurrentStateResolver` and the filtering predicate stay single-state-scoped in v1, so a region authored via `regions` isn't yet something the resolver can report as "active" for filtering purposes. Resolver-returns-a-set/existential-match, the consumption-side counterpart, is future work (see *State-based filtering*).

## The design

### Package structure

`src/Frank.Alps/`, multi-targeting `net8.0;net9.0;net10.0` matching Frank core, `Frank.Rdf`, and `Frank.Provenance`. Depends on `Frank` only (`WebLink`, `HandlerDefinition.Metadata`, both already shipped). No dependency on `Frank.Rdf` or `Frank.Provenance`.

### Descriptor type

```fsharp
[<RequireQualifiedAccess>]
type DescriptorType =
    | Semantic
    | Safe
    | Unsafe
    | Idempotent

type DocFormat =
    | Text
    | Html
    | Asciidoc
    | Markdown

type Doc =
    { Value: string
      Href: Uri option
      Format: DocFormat option
      ContentType: string option
      Tag: string list }

type Link =
    { Href: Uri
      Rel: string
      Title: string option
      Tag: string list }

type Ext =
    { Id: string
      Href: Uri option
      Value: string option
      Tag: string list }

type Descriptor =
    { Id: string
      Name: string option
      Type: DescriptorType
      Def: Uri option
      Doc: Doc option
      Ext: Ext list
      InheritsFrom: DescriptorRef option   // descriptor 'href' — inheritance, not a web link
      Rt: Descriptor option
      Rel: string option
      Tag: string list
      Link: Link list
      Descriptors: Descriptor list }        // nested 'descriptor' array — see Nesting below

and DescriptorRef =
    | Local of Descriptor        // in-process, compile-checked
    | External of Uri            // foreign document, nothing to check against
```

Field-by-field mapping from draft-07 §2.2 to combinators:

| Spec property | Combinator | Notes |
|---|---|---|
| `id` | constructor arg (`semantic "x"`) | mints the name, always a string — nothing to check |
| `type` | `semantic` / `safe` / `unsafe` / `idempotent` | default `semantic` if omitted, matching the spec |
| `name` | `named "x"` | rare — only for describing a pre-existing design, per §2.2.11 |
| `def` | `def "iri"` | |
| `doc` | `doc "text"` (shorthand) / `docWith { Value; Href; Format; ContentType; Tag }` (full) | |
| `ext` | `ext "id" "value"` (shorthand) / `extWith { Id; Href; Value; Tag }` (full) | author-specific extension data |
| `href` (inheritance) | `href (target: Descriptor)` / `hrefExternal (uri: Uri)` | see *Descriptor references* |
| `link` (RFC 8288 web link) | `link "href" "rel"` (shorthand) / `linkWith { Href; Rel; Title; Tag }` | distinct from `href` — arbitrary links, e.g. `rel="tag-doc"` per §2.2.14's guidance for documenting tag vocabularies |
| `rel` | `rel "x"` | |
| `rt` | `rt (target: Descriptor)` | descriptor-typed, dangling references are compile errors |
| `tag` | `tag "x y z"` | |
| nested `descriptor` | `contains [ children ]` | deliberately untyped by child `DescriptorType` — see *Nesting* |

### Two authoring surfaces — plain combinators and a CE

Every combinator above (`doc`/`def`/`tag`/`rel`/`contains`/`rt`/`from`/`href`/`hrefExternal`/`link`/`ext`/`initial`/`regions`) is a plain `... -> Descriptor -> Descriptor` function, pipeable with `|>`, exactly as shown throughout this document. `semantic`/`safe`/`unsafe`/`idempotent` are plain `string -> Descriptor` constructors, unchanged.

Alongside that, a `DescriptorBuilder` — a **separate type from `Descriptor`**, not `Descriptor` doubling as its own builder — offers the same vocabulary as a computation expression:

```fsharp
[<Sealed>]
type DescriptorBuilder =
    new: id: string -> DescriptorBuilder
    member Yield: 'a -> Descriptor       // seeds Id = id, Type = Semantic (the spec's own default), everything else empty
    member Zero: unit -> Descriptor
    member Run: d: Descriptor -> Descriptor

    [<CustomOperation("semantic")>]   member Semantic: d: Descriptor -> Descriptor
    [<CustomOperation("safe")>]       member Safe: d: Descriptor -> Descriptor
    [<CustomOperation("unsafe")>]     member Unsafe: d: Descriptor -> Descriptor
    [<CustomOperation("idempotent")>] member Idempotent: d: Descriptor -> Descriptor
    // ... doc / def / tag / rel / contains / rt / from / href / hrefExternal / link / ext / initial / regions,
    // one [<CustomOperation>] each, same names and shapes as the plain combinators above

val descriptor: id: string -> DescriptorBuilder
```

```fsharp
let listProducts = descriptor "listProducts" { safe; rt product }
let product       = descriptor "product" { contains [ productId; productName; price ] }   // Type unstated, defaults semantic

// unchanged, still available:
let productName = semantic "productName" |> doc "Display name"
```

`semantic`/`safe`/`unsafe`/`idempotent` are reused as zero-argument custom operations *inside* a `descriptor { }` block, setting `Type` rather than constructing a value — this has direct precedent in F#'s own `query { }` builder (`distinct` is exactly a state-only custom operation). Custom-operation names resolve against the builder in scope only inside `{ }`; outside it, the same names resolve to the unrelated plain functions — no collision, no shadowing. Net result: one new top-level name (`descriptor`), `Descriptor` stays a plain, CE-machinery-free data type used everywhere else in this design (`ProtocolGraph`, `StateComposition`, pattern matching), and both surfaces produce the identical `Descriptor` value.

### Descriptor references — `href` vs `hrefExternal`

Two functions, not one SRTP-overloaded `href`. F# has no ad-hoc overloading for `let`-bound functions (unlike C# methods); an SRTP `inline` version would work but introduces an idiom used nowhere else in this codebase for a single combinator's ergonomics — not worth it.

- **`href (target: Descriptor)`** — for a descriptor value in scope in this process. Compile-checked, same discipline as `rt`. This is what a *future* per-resource or per-role profile (not built in v1 — see Non-goals) would use to reuse a descriptor authored elsewhere in the same codebase, even if the two profiles render to different served documents; the renderer decides at serialization time whether to emit `href="#id"` (same document) or `href="<document-uri>#id"` (different document) — a rendering concern, not an authoring one.
- **`hrefExternal (uri: Uri)`** — for a descriptor in a genuinely external ALPS document this codebase does not own (e.g. a published third-party vocabulary profile). Nothing to check against, so it's a bare URI — the same reasoning that makes a descriptor's own `id` a string.

Neither has a real caller in v1 (one document, nothing external consumed yet); both exist now so `Descriptor` doesn't need a breaking field change when multi-document or foreign-vocabulary use shows up.

### Nesting — `contains`, deliberately general

`contains: Descriptor list -> Descriptor -> Descriptor` populates `Descriptors`. Per draft-07 §2.2.4, ALPS nesting is general — any descriptor type may nest under any other — so `contains` is not restricted to semantic children. Today it's used for compound-representation grouping (`product |> contains [productId; productName; price]`); the same mechanism, unchanged, is what composite/substate hierarchy (below) builds on. No type-system change was needed to leave this room — the signature was already general enough.

### Composite states — `initial` and `regions`

Neither has a native ALPS property. Both ride the `ext` mechanism, continuing the `https://frank-fs.github.io/alps-ext/` namespace `protocolState`/`availableInStates` already established (PR #165/#214). Any ALPS-agnostic reader sees an unrecognized `ext` element and ignores it per the spec's tolerant-reader posture — documents stay fully spec-valid either way.

```fsharp
val initial: Descriptor -> Descriptor      // ext id: .../initial — marks the default child within a contains list
val regions: Descriptor list -> Descriptor -> Descriptor   // = contains + ext id .../orthogonal on the parent
```

```fsharp
let waitingForPlayer = semantic "waitingForPlayer" |> initial
let inProgress        = semantic "inProgress"
let openState          = semantic "open" |> contains [ waitingForPlayer; inProgress ]

let network = semantic "network" |> doc "Connectivity region"
let session = semantic "session" |> doc "Auth region"
let inGame  = semantic "inGame"  |> regions [ network; session ]
```

`regions` does not change `Descriptor`'s shape — same `Descriptors: Descriptor list` field as `contains`, plus the marker `ext`. Validated at profile-construction time: at most one `initial` per `contains` list (ambiguity is a real authoring bug, caught the same way a `safe` transition bound to POST is rejected at startup in the existing type/method validation).

A small derived read-model exposes the distinction:

```fsharp
type StateComposition =
    | Leaf
    | Alternatives of Descriptor list   // OR — contains, no orthogonal marker
    | Regions of Descriptor list        // AND — regions

module StateComposition =
    val ofDescriptor: Descriptor -> StateComposition
    val initialChild: Descriptor -> Descriptor option   // meaningful only for Alternatives
```

This is purely expressive. You can author the full hierarchical-and-parallel shape today, independent of any runtime — consumption (state-based filtering, diagram export) is separate and, for orthogonal regions, deliberately not built yet (see *State-based filtering*).

### State-based filtering

```fsharp
val from: Descriptor list -> Descriptor -> Descriptor   // one or more source-state descriptors
type CurrentStateResolver = resourceIri: string -> Uri option
```

`from` marks a `safe`/`unsafe`/`idempotent` transition as valid only from the given state(s). A transition with no `from` is never filtered by state — graceful degradation, the same instinct as `Frank.Provenance`'s `ActivityTypeResolver` returning `None`, and consistent with semantic descriptors never being filtered by authorization today. When a transition declares multiple `from` states, serialization emits one `protocolState`/`availableInStates` `ext` pair per declared state (draft-07 explicitly allows `ext` to be an array, §2.2.6) — not a single space-joined value.

`CurrentStateResolver` is a plain function the consuming application wires at composition time — no project reference to `Frank.Provenance` or any other package. Proposed by the parallel Frank.Provenance design session (frank-fs/frank#471 comments, 2026-08-02): when supplied, the natural implementation queries `Frank.Provenance`'s store (e.g. a future `ProvenanceQuery.Latest`); when absent, or when it returns `None`, state filtering simply does not apply.

Matching walks `contains` ancestry, not exact equality: "resolved state `X` satisfies edge `FromState F`" means `X = F` or `X` is a (possibly transitive) child of `F` via `contains`. This is the correct, complete answer for Harel state-configuration semantics (being in a substate means being in all its ancestors) — confirmed by an independent Harel-formalism review during design (see *Reviewed against Harel's formalism*).

### Derived protocol graph

```fsharp
type ProtocolTransition =
    { FromState: Descriptor
      Transition: Descriptor
      ToState: Descriptor }

module ProtocolGraph =
    val ofProfile: Descriptor list -> ProtocolTransition list
```

`ofProfile` folds over authored descriptors; a transition declaring **both** `from` and `rt` yields one `ProtocolTransition` edge per element of `from` (so `t |> from [A; B] |> rt C` yields two edges). A transition missing either produces no edge — it remains valid ALPS, simply outside this graph. This is the shape from [wizardsofsmart.wordpress.com, 2018](https://wizardsofsmart.wordpress.com/2018/12/05/state-transitions-through-sequence-diagrams/) (`Transition<'State,'Message> = { FromState; Message; ToState }`), which this repo already built tooling around once. `from` takes a list at the authoring site (one real HTTP transition is often valid from several source states); the derived edge type never does — one pair per edge.

### Resources implement transitions; `type` is validated

Unchanged from the superseded doc: `resource { get (handler { handle h; binds SomeCatalog.transition }) }`; `binds` writes via `HandlerDefinition.addMetadata`; a transition's `type` is authored and validated at startup against its bound HTTP method (`safe` → GET/HEAD, `idempotent` → PUT/DELETE, `unsafe` → POST) rather than derived, so a real design error is caught rather than silently accepted.

### HTTP surface

Two exposures — an addition versus the superseded doc, made necessary by `CurrentStateResolver` taking a `resourceIri` (a specific instance), which the app-wide document has no notion of:

1. **App-wide profile** — `GET /.well-known/alps.json`. All descriptors from `useAlps [...]`. Filtered by principal only (existing `IAuthorizationService` mechanism, unchanged); never state-filtered — there is no resource instance in scope. Advertised via `Link: rel="profile"` app-wide (`WebLink.useAppWideLinks`).
2. **Per-resource, content-negotiated excerpt** — `GET /games/{id}` with `Accept: application/alps+json` returns the subset of transitions `binds`-bound at that endpoint, filtered by *both* principal and `CurrentStateResolver "games/{id}"` (contains-ancestry match, above). Advertised via `WebLink.useResourceScopedLinks`. Mirrors `Frank.Rdf`/`Frank.Provenance`'s own inline, content-negotiated pattern exactly.

Semantic descriptors are never filtered by either mechanism in either exposure — vocabulary, not capability, unchanged principle extended to state. `Cache-Control: private, no-cache` and `Vary: Authorization` apply to both exposures whenever either filter is active, unchanged from the superseded doc's rule.

### Multi-party protocols

Not a new authoring primitive. A multi-party protocol is expressed as **N independently hand-authored ALPS documents, one per role's local perspective**, sharing vocabulary via `href` where roles reference the same states or messages. No unifying global protocol type, no projection/derivation algorithm, no automatic duality or consistency verification between roles — that is deliberately weaker than the rolled-back line's `ProtocolType<'Role,...>`/Li-et-al.-projection design, traded for never requiring codegen or a proof obligation before any of it is useful. This rides on the same multi-document support already deferred (*Non-goals* — per-resource/multi-document profiles); nothing new to build for it specifically. If cross-role consistency checking is ever wanted, the existing *paired analyzer* idea (below) is the natural place — profile-vs-profile drift, not just profile-vs-template.

### The paired analyzer

Unchanged from the superseded doc: `Frank.Analyzers` (FSharp.Analyzers.SDK) inspecting authored profiles against view-engine templates or format serializers for descriptor-id drift. The authored-values design makes this tractable — descriptors are ordinary F# bindings with literal ids, resolvable from the syntax tree without evaluation, and `rt`/`from`/`href` references are already compiler-checked. Its own work item; not built here.

### Reviewed against Harel's formalism

An independent review (conducted during design, against Harel 1987 and SCXML) of `ProtocolTransition`, `contains`-ancestry matching, and the deferred set-valued-resolver plan for orthogonal regions found:

- **Hierarchy** (`contains`-ancestry matching): sound and complete for state-configuration semantics as designed; the one gap is default-initial-substate entry, closed here by `initial` (additive, no shape change was needed).
- **Parallelism**: the deferred plan (`CurrentStateResolver` returning a set, existential match) correctly reaches independent-region OR filtering ("show this transition if *any* active region matches") but does **not** reach genuine Harel compound transitions — a conjunctive AND-guard across regions, or a transition fanning out to enter several regions at once. Those need a distinct wrapper type (e.g. a future `CompoundProtocolTransition`) layered alongside `ProtocolTransition`, not a change to it. Recorded here as the explicit scope boundary of *State-based filtering*, so it isn't mistaken later for covering AND-semantics it structurally cannot reach.
- History states, guards, entry/exit actions, run-to-completion/event-queue semantics: all additive-if-ever-pursued; none force a breaking change to what's designed here.

## Error handling and edge cases

| Situation | Behaviour |
|---|---|
| `initial` applied to more than one child of the same `contains` list | Fails at profile-construction time. |
| `href`/`rt`/`from`/`contains` target does not exist | Compile error — these are descriptor-typed, not strings. |
| `hrefExternal` target is unreachable or wrong | Not checked — same as `def`, nothing to check against. |
| `CurrentStateResolver` absent, or returns `None` | No state filtering; only authorization filtering applies. |
| Resolved state matches nothing, not even via `contains` ancestry | Transition dropped from the per-resource excerpt only, never from the app-wide document. |
| A `safe` transition bound to POST (or other type/method mismatch) | Fails at startup (registration-time), not on first request — unchanged. |
| `useAlps` configured with no `IAuthorizationService`/no resolver configured at all | App-wide document serves unfiltered; per-resource excerpt serves auth-filtered only. Neither is an error. |
| A transition declares `from` but has no `rt` (or vice versa) | Valid ALPS descriptor; produces no `ProtocolGraph` edge. |
| `regions` children with no further `contains` substructure | Valid — a region can be a single leaf state. |

## Testing

Mirrors `Frank.Rdf`/`Frank.Provenance`'s established pattern (`TestHost`, `JsonElement` inspection in the style of the prior `UnifiedAlpsGeneratorTests.fs`, not string comparison):

- **Per-combinator**: `doc`/`def`/`tag`/`rel`/`contains`/`rt`/`from`/`href`/`hrefExternal`/`link`/`ext`/`initial`/`regions` each produce the correct fields on the resulting `Descriptor`.
- **Authoring-surface parity**: the same profile built via plain `|>` combinators and via `descriptor { }` produces structurally equal `Descriptor` values; `semantic`/`safe`/`unsafe`/`idempotent` used as custom operations set `Type` correctly, and an unset `Type` defaults to `Semantic` matching draft-07 §2.2.16.
- **Serialization**: full round-trip against draft-07's JSON shape; `href` local-fragment (`#id`) vs. `hrefExternal` full-URI emission; `protocolState`/`availableInStates` ext auto-emission exactly when `from`+`rt` are both present (one pair per declared `from` state), absent otherwise.
- **`ProtocolGraph.ofProfile`**: correct edge set for `from`+`rt` combinations, including the one-edge-per-source-state expansion; zero edges for transitions missing either.
- **`StateComposition`**: `Alternatives` vs. `Regions` classification; `initialChild` resolution; construction-time rejection of multiple `initial` markers in one `contains` list.
- **Filtering**: authorization-only, state-only (including `contains`-ancestry matches across composite/substate), both, neither; resolver absent vs. `None`; semantic descriptors never filtered under any combination.
- **`type`-vs-method validation**: startup rejection of a mismatched binding, unchanged from the superseded doc.
- **HTTP surface**: app-wide document + `Link: rel="profile"`; per-resource excerpt + resource-scoped `Link` header + correct cache headers when either filter is active; both against a `TestHost`-backed resource.

## Future work (separate)

- **Per-resource / multi-document profiles.** `href`/`hrefExternal` exist now specifically to make this additive later.
- **`CompoundProtocolTransition`** for conjunctive AND-region guards and multi-region-fan-out targets — explicitly not reachable by the current `ProtocolTransition`/existential-match design (*Reviewed against Harel's formalism*).
- **`CurrentStateResolver` returning a set of concurrently active states**, with the filtering predicate changed to existential match — the actual consumption-side counterpart to `regions`, deferred (*State-based filtering* / *Composite states*).
- **Role-projected statecharts as independently authored per-role documents** (*Multi-party protocols*) — no code to write until a real multi-role consumer exists.
- **`Frank.Analyzers` paired analyzer** — profile-vs-template drift now; profile-vs-profile (cross-role) consistency later, if wanted.
- **History states, guard conditions, entry/exit actions, run-to-completion semantics** — all additive if ever pursued; none designed here.

## Sources

- ALPS draft-07: https://datatracker.ietf.org/doc/html/draft-amundsen-richardson-foster-alps-07 (fetched and diffed against draft-04 during design, not assumed from memory)
- RFC 6906 (profile link relation): https://www.rfc-editor.org/rfc/rfc6906
- RFC 8288 (Web Linking): https://www.rfc-editor.org/rfc/rfc8288
- Superseded design: [2026-07-28-frank-alps-protocol-design.md](2026-07-28-frank-alps-protocol-design.md)
- [Frank.JsonHome design](2026-07-28-frank-jsonhome-design.md), [Frank.Rdf design](2026-07-30-frank-rdf-design.md), [Frank.Provenance design](2026-08-02-frank-provenance-design.md) — prerequisite/sibling packages, established HTTP-exposure and seam patterns.
- frank-fs/frank#471 and its 2026-08-02 comments — `CurrentStateResolver` proposal.
- Rolled-back prior art (reference only): `docs/superpowers/specs/2026-04-21-v740-protocol-types-design.md` (`feature/v7.3.2`), PR #214/#207 (`protocolState`/`availableInStates` ext), PR #165 (namespaced ext URIs), PRs #172/#178/#274/#169 (per-role projection, not reused).
- https://wizardsofsmart.wordpress.com/2018/12/05/state-transitions-through-sequence-diagrams/ — origin of `ProtocolTransition`'s shape; `007-wsd-lexer-parser-ast` built tooling around it once, also rolled back.
- `WebLink`/resource-scoped `Link` mechanism: `src/Frank/WebLink.fs`. `HandlerDefinition.Metadata` → `Endpoint.Metadata` flow: `src/Frank/HandlerDefinition.fs`.
