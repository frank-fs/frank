# DU-Aware Convention Extraction — Design

Date: 2026-06-19
Branch: `v7.3.2-rebuild`
Status: design (brainstormed, awaiting plan)

## Thesis

A discriminated union's **structure is certain knowledge** — FCS hands us the exact
algebraic shape (sum of N cases, each case's name, payload fields, generic arity).
Convention extraction must therefore treat a DU as a *sum* and emit that structure
mechanically, then **join** the type/case/field name-tokens against the entities the
loaded vocabulary *declares* (classes, properties, individuals). It must never *infer*
domain meaning the vocabulary does not supply.

The consequence: **the floor is `structure × vocabulary richness`, not a property of the
F# type.** The same `XMove` is floor-derivable against a domain ontology that declares the
target entities and investment-only against generic schema.org that does not. This sharpens
the v7.3.2 thesis: against a generic vocab you must *invest*; enrich the vocab with a domain
ontology and the *same code* auto-confirms — the two honest paths are shown side by side.

## Problem

Today the extractor coerces every union case into a flat record-field slot, and the
convention engine fuzzy-matches those case names as if they were *properties*:

- `src/Frank.Cli.Core/Extractor.fs:70-82` — `unionCaseToFieldInfo` writes `uc.Name`
  (`"XMove"`, `"OMove"`) into `FieldInfo.Name`, joining payload types with `*`. The
  record-vs-DU distinction is lost the moment a `TypeInfo` is built.
- `src/Frank.Semantic/ConventionEngine.fs:151-162` — `fieldSimScore` runs Jaro-Winkler
  between those case names and vocabulary *properties*. `XMove` → `schema:orderedItem`,
  `OMove` → `schema:totalPaymentDue`. Garbage, because case names are matched in the wrong
  structural role.
- `src/Frank.Semantic/Mapping.fs:45-52` — `Mapping.Fields: FieldMapping list` is flat. A DU
  is a tree (`type → cases → payload fields`); the flat list cannot represent it, violating
  the project's no-flatten norm.

The bug is not "case names are meaningless." Case names are often the *best* signal (a
nullary enum `Red | Green | Amber` — the names *are* the values). The bug is matching them
in the wrong role with fuzzy similarity against an unrelated vocabulary.

## Definitions

### Three layers (only two carry uncertainty)

1. **Structural layer — certain, mechanical, no human, no vocab lookup.** FCS gives the exact
   sum/product shape. ADT→RDF structure is total: a record is a product (a class with
   properties); a union is a sum (a choice of cases); a nullary case is an enumerated value;
   a payload-carrying case is a variant. `Result<'T,'E> = Ok of 'T | Error of 'E` lives
   entirely here.
2. **Lexical layer — conservative join (the v7.3.2 exact-confirm rule lives here, and only
   here).** "Does this name-token exactly match a declared vocab entity?" Exact → Confirmed;
   fuzzy → Proposed. This is the only correct home for string-similarity conservatism.
3. **Domain-investment layer — the genuine residue.** Meaning that neither structure nor an
   exact lexical hit can supply (e.g. "`XMove` means the agent is Player X" when the vocab
   declares no such link). LLM/human only.

The earlier "hard floor line" was an error: it applied layer-2 conservatism to layer-1
structure, deferring facts the type system already knows.

### The join

Convention is an **exact multi-token join** of name-tokens against declared vocab entities
in their correct structural role:

- type name tokens → **classes**
- record field tokens → **properties**
- **nullary** case tokens → **individuals** (`owl:NamedIndividual` / `skos:Concept`)
- **payload-carrying** case tokens → **subclasses**

A mapping **Confirms only when every token resolves exactly** in its role. Any fuzzy hit →
`Proposed`. No hit → `Unresolved`. Convention composes only over entities the vocabulary
*declares*; it never synthesizes a linking property the tokens do not name. The full
"a Move by Player X" assertion (subclass `XMove` linked via an unnamed `agent` property to
individual `Player:X`) is investment unless the vocabulary models it directly.

### Status model (no new status)

Cases reuse the existing four `MappingStatus` values — `Confirmed | Proposed | Unresolved |
Excluded`. The branch/choice is exposed **structurally** (via `Shape = Union`), not via a
status marker. `finalize` decides residual cases exactly as it decides fields
(`LockFile.isDecided` unchanged). This supersedes the earlier "structured discriminant
marker" idea — the choice lives in the Shape, so no forcing status is needed.

## Solution

### 1. Tokenizer — multipart splitting

`ConventionEngine` already has `splitPascalCase`. Generalize and harden for multipart terms,
since case names commonly encode multiple tokens:

- `SquarePosition → [square, position]`
- `XMove → [x, move]` (single-capital prefix is its own token)
- `HTTPSConfig → [https, config]` (acronym run is one token; split before the trailing
  capitalized word)

Single-capital prefixes and acronym runs are the two new edges; everything else is the
existing camel/pascal split.

### 2. Extractor — stop coercing cases into fields

`src/Frank.Cli.Core/Extractor.fs`. Replace the `unionCaseToFieldInfo`-into-`Fields` path with
a sum-aware `TypeInfo`. `TypeInfo` gains a shape that distinguishes record from union:

```fsharp
type CaseInfo =
    { Name: string                 // "XMove"
      Payload: FieldInfo list }    // labeled/unlabeled payload fields; [] for nullary

type TypeShape =
    | Record of FieldInfo list
    | Union of CaseInfo list

type TypeInfo =
    { FullName: string
      Namespace: string
      LocalName: string
      Shape: TypeShape             // replaces the flat `Fields`
      Attributes: Map<string, string>
      DocComment: string option }
```

Payload `FieldInfo` is derived per the source-priority rule (see Assumptions): labeled
payload → label; unlabeled non-primitive → payload type-name tokens; unlabeled primitive →
no derivable field.

### 3. ConventionEngine — role-aware join

`src/Frank.Semantic/ConventionEngine.fs`. The engine must:

- Load the vocabulary's **individuals** in addition to classes and properties (new — today it
  reads only classes and properties).
- For a `Record`, behave as today (type→class, fields→properties).
- For a `Union`, emit the sum structurally, then per case:
  - nullary case → join its tokens against **individuals**;
  - payload case → join its tokens against **subclasses**, and join each payload `FieldInfo`
    against **properties**.
- Confirm a case only when all of its tokens resolve exactly in role; else Proposed; else
  Unresolved. Never fuzzy-match a case name against a *property* (the current garbage path).

### 4. Mapping / LockFile — sum-aware shape (no flattening)

`src/Frank.Semantic/Mapping.fs`. Replace flat `Fields` with a shape DU mirroring the
extractor:

```fsharp
type CaseMapping =
    { Name: string                  // "XMove" — the real F# constructor name
      Iri: string option            // individual (nullary) or subclass (payload) IRI
      Confidence: float
      Source: MappingSource
      Status: MappingStatus
      Payload: FieldMapping list }  // payload property mappings; [] for nullary

type MappingShape =
    | Record of FieldMapping list
    | Union of CaseMapping list

type Mapping =
    { FSharpType: string
      Iri: string option            // the type's class IRI
      Confidence: float
      Source: MappingSource
      Status: MappingStatus
      Alternates: string list
      Shape: MappingShape }         // replaces `Fields`
```

`LockFile` serialization extends to the shape; `isDecided` is unchanged and applies per case.
`ResolvedModel.build` filters `Excluded` for both fields and cases. This is a lock-schema
change — acceptable: pre-1.0, fully regenerable from source.

### 5. Codegen — per-type case match function (anti-drift over constructors)

`src/Frank.Cli.Core/SemanticModelEmitter.fs`. The generated model keeps `SemanticResource`
(one case per mapped *type*) with `iri`/`clrType`. For each mapped **union**, additionally
emit a match function over the **real F# constructors**:

```fsharp
let moveCaseIri (m: Move) : System.Uri =
    match m with
    | XMove _ -> System.Uri("https://example.org/tictactoe#XMove")
    | OMove _ -> System.Uri("https://example.org/tictactoe#OMove")
```

Exhaustiveness is the anti-drift mechanism: renaming, removing, or adding a case breaks
compilation of the generated module — you cannot drift a confirmed case mapping silently.
Payload-case subclass IRIs and nullary-case individual IRIs emit through the same match.

## Acceptance Tests

Falsifiable input → output pairs. Library-level (extraction + lock + codegen), plus one
build-level anti-drift check.

- **AT1 — nullary enum, individuals declared.** Vocab declares individuals `Red`, `Green`,
  `Amber`. Input `type Light = Red | Green | Amber`. Output: lock `Mapping` with
  `Shape = Union` of three `CaseMapping`, each `Status = Confirmed`, `Payload = []`, `Iri`
  pointing at the matching individual. **Falsifies:** any case `Proposed`/`Unresolved`, or a
  case matched to a property.

- **AT2 — payload DU, generic vocab (residue path).** Vocab = schema.org (no `Player`,
  no `X`/`O` individuals). Input `type Move = XMove of SquarePosition | OMove of
  SquarePosition`. Output: `Shape = Union`; cases `XMove`/`OMove` `Unresolved` (no declared
  subclass/individual for `x`/`o`); **no** case mapped to `schema:orderedItem` or any
  property. Payload tokens `[square, position]` join against properties (Confirmed only if
  both exact, else Proposed). **Falsifies:** reappearance of the `XMove → schema:orderedItem`
  garbage, or a Confirmed case with no exact declared target.

- **AT3 — payload DU, domain ontology (floor path).** Vocab declares subclasses `XMove`,
  `OMove` (and/or individuals as appropriate). Same input as AT2. Output: cases `Confirmed`
  against the declared subclasses; same source code, no edits — only the vocabulary changed.
  **Falsifies:** the thesis claim "floor = structure × vocab richness" if the cases do not
  flip to Confirmed purely from the richer vocab.

- **AT4 — anti-drift build break.** Generate the model for a confirmed union, then rename a
  case in the F# source without regenerating. **Expected:** the generated `…CaseIri` match
  fails to compile (non-exhaustive / unknown constructor). **Falsifies:** a green build after
  a case rename.

- **AT5 — generic/recursive graceful handling.** Input `type Result<'T,'E> = Ok of 'T |
  Error of 'E` and `type Tree = Leaf | Node of Tree * Tree`. Output: both map structurally
  (`Shape = Union`); type-variable payloads (`'T`, `'E`) and recursive payloads (`Tree`)
  produce no property derivation and no infinite descent. **Falsifies:** a crash, a hang, or
  an invented property for a type variable.

## Assumptions (baked in; flagged for spec review)

1. **Payload property source priority:** labeled payload → label tokens
   (`of position: SquarePosition` → `position`); unlabeled non-primitive → payload
   *type-name* tokens (`of SquarePosition` → `[square, position]`); unlabeled primitive
   (`of string`) → no derivable property, payload `Unresolved`.
2. **Exact-confirm over all tokens:** a case/payload mapping Confirms only when every token
   hits exactly in role; any fuzzy → Proposed; no hit → Unresolved.
3. **Generics:** type-variable payloads carry no lexical signal → case maps structurally
   (subclass), payload property Unresolved. Generic arity is already tracked for `clrType`.
4. **Recursion:** bounded walk; a recursive payload references the type's own class IRI; no
   infinite descent.
5. **No synthesized links:** convention never invents a linking property the tokens do not
   name. The full "Move by Player X" composition is investment unless the vocabulary declares
   it directly.

## Sources

- Code map (this session): `Extractor.fs:70-82`, `ConventionEngine.fs:151-162,347-399`,
  `Mapping.fs:36-52`, `LockFile.fs:12-21`, `SemanticModelEmitter.fs:30-81`.
- #372 progressive-enhancement decisions: exact-token auto-confirm, `MappingStatus.Excluded`,
  `finalize`, decided-gate (`docs/plans/372-progressive-enhancement.md`).
- v7.3.2 semantic-discovery design: `docs/superpowers/specs/2026-04-20-v732-semantic-discovery-design.md`.
- Project norms: no-flatten (TransitionStep), anti-drift coupling, exact-match honesty.
