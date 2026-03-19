# Spec Pipeline: Bidirectional Design Specifications

The spec pipeline enables a design-first development workflow for Frank applications. Start from a design document in any supported format, use LLM-assisted tooling to generate a working implementation, then extract specifications from the running application to verify and refine the design. The pipeline is bidirectional — every format works as both input and output.

## The Workflow

```
Design Spec ──→ LLM-assisted codegen ──→ Running Frank app
     ↑                                         │
     │                                         │
     └──── Compare / Refine ←── Extract spec ──┘
```

A developer sketches an API as a sequence diagram (WSD), a statechart (SCXML), or an affordance profile (ALPS). The LLM parses the spec into a typed AST and generates F# code — state DUs, transition functions, guard stubs, handler wiring. The developer fills in business logic, builds, and runs the application. The running application can then serve its current spec in any supported format via content negotiation, allowing comparison against the original design.

This is a **verification loop**, not a one-shot generator. The extracted spec reflects the implementation as-built, including any refinements made during development. Comparing the extracted spec against the original design reveals intentional divergence (refinements) and unintentional drift (bugs or missed requirements). The comparison is not automated — an LLM or human reviews the two artifacts and judges what to do.

## Format Roles

Three formats form the core trio that together describe a complete application specification. Each models a complementary facet of the same system:

| Format | Models | Intentionally Omits |
|--------|--------|---------------------|
| **WSD** | Workflow topology: states, transitions, ordering, parameters | Semantic meaning, HTTP types, data schemas |
| **SCXML** | Executable statechart: states, transitions, guards, data model, history, invocation | Semantic meaning, HTTP types |
| **ALPS** | Vocabulary: semantic descriptors, transition types (`safe`/`unsafe`/`idempotent`), return types | Workflow ordering (by design — [FAQ A.2](http://alps.io/spec/drafts/draft-01.html)) |

WSD supplies the workflow constraints that ALPS intentionally omits. SCXML provides the executable state machine definition that WSD describes only topologically. ALPS provides the semantic meaning that both WSD and SCXML lack. Together they give a complete picture: what happens (WSD), how it executes (SCXML), and what it means (ALPS).

Two additional formats add complementary value:

| Format | Role |
|--------|------|
| **XState JSON** | JavaScript-ecosystem interop, Stately Studio visualization and simulation |
| **smcat** | Lightweight text-based state machine notation with visualization |

### Cross-Validation

The formats describe overlapping aspects of the same system, which enables cross-validation:

- Every XState/SCXML event name must exist as an ALPS transition descriptor
- Every ALPS transition target must exist as an XState/SCXML state
- WSD actor lifelines must correspond to ALPS state descriptors
- Guard annotations in WSD must appear in SCXML/XState guard definitions

The cross-format validator checks these invariants and reports inconsistencies. See `Validation/Validator.fs` and `Validation/Pipeline.fs` in `Frank.Statecharts`.

## Architecture

```
         Input Direction (Spec → Code)
         ─────────────────────────────
         WSD ──→ WSD AST ──→ ┐
        SCXML ──→ SCXML AST ──→ ├──→ Typed representation ──→ LLM generates F# code
         ALPS ──→ ALPS AST ──→ ┘
        smcat ──→ smcat AST ──→ ┘
  XState JSON ──→ (trivial) ──→ ┘

         Output Direction (Code → Spec)
         ──────────────────────────────
         StateMachineMetadata ──→ WSD Generator ──→ WSD text
                              ──→ SCXML Generator ──→ SCXML XML
                              ──→ ALPS Generator ──→ ALPS JSON/XML
                              ──→ XState Generator ──→ XState JSON
                              ──→ smcat Generator ──→ smcat text
```

Each format has its own parser and generator. The parser produces a typed AST; the generator consumes `StateMachineMetadata` (the runtime's reflection type) and produces the format's text/XML/JSON representation.

### Input Direction: LLM-Assisted Code Generation

The "Spec → Code" direction is **intentionally LLM-assisted, not deterministic**. Each parser produces a typed representation that becomes structured context for an LLM prompt. The LLM generates F# code: state DUs, transition functions, guard stubs, handler wiring into Frank's `statefulResource` CE.

This is a deliberate design choice:

- **No template lock-in.** A deterministic generator embeds opinions about code structure that may not match the developer's preferences. An LLM adapts to the project's existing patterns.
- **Typed ASTs are the public interface.** The parser output is stable; the code generation strategy can evolve independently. A developer who wants to build a different tool — a deterministic generator, a type provider, a VS Code extension — can consume the same typed ASTs.
- **Correctness comes from the verification loop.** Rather than trying to guarantee correct generation, the pipeline extracts the spec back from the running code and compares. This is more robust than template-based guarantees.

### Output Direction: Spec Extraction

The output direction reads `StateMachineMetadata` from a running Frank application (or from compiled assemblies via reflection) and generates spec documents. This is deterministic — the generators produce canonical representations of the current implementation.

The extracted specs serve multiple purposes:

- **Verification**: compare against the original design spec
- **Documentation**: serve via content negotiation (`GET /` with `Accept: application/scxml+xml`)
- **Visualization**: feed into Stately Studio (XState), smcat renderer, app-state-diagram (ALPS)
- **Interop**: export to tools in other ecosystems

## WSD Guard Extension

For per-user state discrimination (e.g., turn-based games), WSD is extended with guard annotations using existing `note` syntax. This preserves compatibility with websequencediagrams.com rendering:

```
note over WaitingForX: [guard: role=PlayerX]
WaitingForX->+WaitingForO: makeMove(position)
note over WaitingForO: [guard: role=PlayerO]
WaitingForO->+WaitingForX: makeMove(position)
```

Guards flow into ALPS via `ext` elements (ALPS's designated extension point) and into SCXML/XState as native guard definitions.

## WSD → ALPS Mapping

Starting from [Amundsen's onboarding example](https://mamund.site44.com/talks/2018-09-restfest/2018-09-restfest-wsd.pdf):

| WSD Arrow | WSD Meaning | ALPS Type | HTTP |
|-----------|-------------|-----------|------|
| `->` solid forward | Sync call, activates target | `unsafe` | POST |
| `-->` dashed forward | Async/optional, activates target | `unsafe` | POST |
| `->-` solid deactivates | Return from activation | `safe` | GET |
| `-->-` dashed deactivates | Async return | `safe` | GET |

WSD parameters become ALPS semantic descriptors. WSD actor lifelines become ALPS state descriptors with available transitions nested inside. WSD target states become ALPS `rt` (return type).

## Supported Formats

All formats are fully implemented with bidirectional support (parse and generate):

| Format | Parser | Generator | Module |
|--------|--------|-----------|--------|
| **WSD** | Lexer + recursive descent | From `StateMachineMetadata` | `Frank.Statecharts.Wsd` |
| **ALPS** | JSON + XML | JSON + XML | `Frank.Statecharts.Alps` |
| **SCXML** | `System.Xml.Linq`-based | W3C-compliant XML | `Frank.Statecharts.Scxml` |
| **smcat** | Lexer + label parser | Text serializer | `Frank.Statecharts.Smcat` |
| **XState JSON** | Deserializer | Serializer | `Frank.Statecharts.XState` |

All parsers produce a shared `StatechartDocument` AST (`Frank.Statecharts.Ast`). The cross-format validator and unified CLI operate on this common representation.

Implementation was tracked in [#57](https://github.com/frank-fs/frank/issues/57) with sub-issues per format (#90, #91, #93, #94, #95, #97, #98, #100, #111, #112).

## Round-Trip Expectations

A design spec fed through the pipeline (spec → code → extract spec) will not produce an identical document. Implementation refines the design: states may be renamed, guards added, transitions adjusted. The extracted spec reflects the implementation as-built.

What *should* be preserved is **structural equivalence** at the state machine level: the same states exist, the same transitions connect them, the same guards protect them. Surface differences (naming, ordering, comments, formatting) are expected and acceptable. The cross-validator checks structural invariants; an LLM or human judges semantic equivalence.

## CLI Integration

`frank-cli` provides commands organized into three tiers:

### Unified Commands (top-level)

- `frank-cli extract` — single-pass extraction of both semantic (OWL/SHACL) and statechart metadata from a Frank application
- `frank-cli generate` — generate spec documents in any supported format (WSD, ALPS, SCXML, smcat, XState, affordance-map, or all) from cached extraction state
- `frank-cli status` — report extraction state and cache staleness
- `frank-cli help` — command discovery with fuzzy matching

### Statechart Commands

- `frank-cli statechart extract` — extract state machines from Frank applications
- `frank-cli statechart generate` — generate spec documents from statechart ASTs
- `frank-cli statechart validate` — cross-validate spec formats against each other
- `frank-cli statechart parse` — parse a single spec document and output its AST

### Semantic Commands

- `frank-cli semantic extract` — extract OWL ontologies and SHACL shapes from F# types
- `frank-cli semantic validate` — validate semantic definitions against vocabularies
- `frank-cli semantic compile` — compile OWL/SHACL to runtime shape graphs
- `frank-cli semantic clarify` — identify ambiguities requiring human input
- `frank-cli semantic diff` — compare semantic artifacts across time
- `frank-cli semantic openapi-validate` — validate OpenAPI specs against semantic constraints

## References

- [Mike Amundsen: Using Web Sequence Diagrams with your APIs (RESTFest 2018)](https://mamund.site44.com/talks/2018-09-restfest/2018-09-restfest-wsd.pdf)
- [State Transitions through Sequence Diagrams (F# Advent 2018)](https://wizardsofsmart.wordpress.com/2018/12/05/state-transitions-through-sequence-diagrams/)
- [ALPS Specification](http://alps.io/spec/drafts/draft-01.html)
- [SCXML W3C Recommendation](https://www.w3.org/TR/scxml/) — State Chart XML standard
- [smcat (state machine cat)](https://github.com/sverweij/state-machine-cat) — lightweight state machine notation
- [XState / Stately](https://stately.ai/) — state machine validation and visual editing
- [app-state-diagram (ASD)](https://github.com/alps-asd/app-state-diagram) — generates state diagrams from ALPS
- [Frank.Statecharts](STATECHARTS.md) — Runtime state machine library
- [Semantic Resources](SEMANTIC-RESOURCES.md) — Agent-legible application architecture
