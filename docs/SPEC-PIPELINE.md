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

A cross-format validator (#91) checks these invariants and reports inconsistencies.

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

## Implementation Phases

The pipeline is tracked in [#57](https://github.com/frank-fs/frank/issues/57) with sub-issues per format.

### Priority Order

1. **WSD Parser (#90)** — critical path, already partially exists in [wsd-gen F# fork](https://github.com/panesofglass/wsd-gen/tree/fsharp)
2. **WSD Generator + Cross-Validator (#91)** — depends on #90, enables the verification loop for WSD
3. **ALPS (#97), SCXML (#98), smcat (#100)** — bidirectional, depend only on the core runtime (#87). Can proceed in parallel with each other and with #90
4. **frank-cli commands (#94)** — depends on all format issues; provides `extract`, `generate`, `validate`, `import` subcommands
5. **Automatic ETag from state (#93)** — parallel with everything; no pipeline dependency

### Dependency Graph

```
#90 WSD Parser ──────────────→ #91 WSD Generator + Cross-Validator ┐
(P0, critical path)            (P1)                                 │
                                                                    ├──→ #94 frank-cli Commands
#87 Core Runtime ────────┬───→ #97 ALPS Parser + Generator ────────┤    (P3)
(prerequisite, done)     ├───→ #98 SCXML Parser + Generator ───────┤
                         ├───→ #100 smcat Parser + Generator ──────┘
                         └───→ #93 ETag Generation
                               (P2, parallel)
```

## Round-Trip Expectations

A design spec fed through the pipeline (spec → code → extract spec) will not produce an identical document. Implementation refines the design: states may be renamed, guards added, transitions adjusted. The extracted spec reflects the implementation as-built.

What *should* be preserved is **structural equivalence** at the state machine level: the same states exist, the same transitions connect them, the same guards protect them. Surface differences (naming, ordering, comments, formatting) are expected and acceptable. The cross-validator checks structural invariants; an LLM or human judges semantic equivalence.

## CLI Integration

`frank-cli` (#94) provides commands that wrap the pipeline for common workflows:

- `frank extract` — generate spec documents from a compiled Frank application
- `frank generate` — generate F# scaffolding from a spec document (LLM-assisted)
- `frank validate` — cross-validate spec documents against each other and/or a running app
- `frank import` — parse a spec document and produce the typed AST (useful for tooling)

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
