# References

Works cited across Frank documentation and issues, organized by category.

## Statechart Theory

- Harel, D. (1987). "Statecharts: A Visual Formalism for Complex Systems." *Science of Computer Programming*, 8(3), 231–274. — AND-state semantics, hierarchical decomposition, history pseudo-states. Foundation for the entire statechart runtime.

- [SCXML W3C Recommendation](https://www.w3.org/TR/scxml/) — State Chart XML standard. Frank's SCXML parser/generator targets this spec. Entry/exit ordering, parallel states, history semantics.

- [XState / Stately](https://stately.ai/) — State machine validation and visual editing. One of Frank's supported output formats.

- [smcat (state machine cat)](https://github.com/sverweij/state-machine-cat) — Lightweight state machine notation. One of Frank's supported input/output formats.

- [app-state-diagram (ASD)](https://github.com/alps-asd/app-state-diagram) — Generates state diagrams from ALPS profiles.

## Session Types and MPST

- Honda, K., Yoshida, N., Carbone, M. (2008). [Multiparty Asynchronous Session Types](https://doi.org/10.1145/1328438.1328472). *POPL '08*. — Foundation for Frank's role projection and MPST model.

- Wadler, P. (2012). [Propositions as Sessions](https://doi.org/10.1145/2364527.2364568). *ICFP '12*. — Curry-Howard correspondence between classical linear logic and session types. Informs Frank's dual derivation.

- Wadler, P. (2014). [Propositions as Types](https://doi.org/10.1145/2699407). *Communications of the ACM*. — Accessible overview of the propositions-as-types correspondence including sessions.

- Lindley, S. & Morris, J.G. (2015). [A Semantics for Propositions as Sessions](https://doi.org/10.1007/978-3-662-46669-8_23). *ESOP '15*. — Operational semantics for GV (functional session types).

- Gay, S.J. & Vasconcelos, V.T. (2010). [Linear Type Theory for Asynchronous Session Types](https://doi.org/10.1017/S0956796809990268). *JFP*. — Linear types for async sessions.

- [Scribble](http://www.scribble.org/) — Protocol description language for multiparty session types.

- [MPST at Imperial College](https://mrg.doc.ic.ac.uk/publications/multiparty-asynchronous-session-types/) — Research group and publications.

## Tagless Final and Free Monads in F#

- Azariah, J. (2025). [Tagless Final in F#](https://johnazariah.github.io/2025/12/12/tagless-final-01-froggy-tree-house.html) — 6-part series, FsAdvent 2025:
  1. [Froggy Tree House](https://johnazariah.github.io/2025/12/12/tagless-final-01-froggy-tree-house.html) — Intro to tagless-final in F#
  2. [Maps, Branches, and Choices](https://johnazariah.github.io/2025/12/12/tagless-final-02-maps-branches-choices.html) — Branching in DSLs
  3. [Goals, Threats, and Getting Stuck](https://johnazariah.github.io/2025/12/12/tagless-final-03-goals-threats.html) — Constraints in DSLs
  4. [A Surprising New DSL: Elevators](https://johnazariah.github.io/2025/12/12/tagless-final-04-elevators.html) — Domain DSL for state machines
  5. [Verifying the Elevator](https://johnazariah.github.io/2025/12/12/tagless-final-05-verifying-elevators.html) — Swap interpreter for verification
  6. [Code as Model](https://johnazariah.github.io/2025/12/12/tagless-final-06-model-verification.html) — Safety interpreter explores state space for bugs

- Azariah, J. (2018). [A Tale of Two Languages: F# and Q#](https://johnazariah.github.io/2018/12/04/tale-of-two-languages.html) — Free monad in production F# for quantum gate optimization. Demonstrates program-as-data inspection use case.

- Azariah, J. [Free monad gist](https://gist.github.com/johnazariah/a5785f754c978a3e12df5509dbafaf41) — Production F# free monad with trampolining (stackless, right-associative bind rewriting).

- Wlaschin, S. [13 Ways of Looking at a Turtle, Way 13](https://fsharpforfunandprofit.com/posts/13-ways-of-looking-at-a-turtle-2/#way13) — Minimal free monad (interpreter pattern) for small instruction sets in F#.

- Seemann, M. (2017). [F# Free Monad Recipe](https://blog.ploeh.dk/2017/08/07/f-free-monad-recipe/) — Continuation-threading approach for free monads in F#.

- Haynes, H. (2025). [Delimited Continuations](https://clef-lang.com/docs/design/concurrency/delimited-continuations/) (SpeakEZ/Clef) — `let!` as `shift`, CE builder as `reset`. Informs the Transition CE design.

- Ducasse, S. et al. [Seaside framework](https://github.com/SeasideSt/Seaside) — `callcc` for web flow: statechart state as suspended continuation.

## Semantic Web Standards

- [ALPS Specification](http://alps.io/spec/drafts/draft-01.html) — Application-Level Profile Semantics. Frank's primary semantic vocabulary for describing application state and transitions.

- [RDF 1.2](https://www.w3.org/TR/rdf12-concepts/) — Resource Description Framework. Foundation for Frank.LinkedData.

- [SHACL](https://www.w3.org/TR/shacl/) — Shapes Constraint Language. Used by Frank.Validation for constraint validation.

- [PROV-O](https://www.w3.org/TR/prov-o/) — W3C Provenance Ontology. Used by Frank.Provenance for agent/activity/entity triples.

- [JSON-LD](https://www.w3.org/TR/json-ld11/) — JSON for Linking Data. One of Frank.LinkedData's content negotiation formats.

## HTTP Standards

- RFC 9110 — HTTP Semantics. `Allow` header (§10.2.1), method safety (§9.2.1), conditional requests.

- RFC 8288 — Web Linking. Link header format, relation types. Frank's Link header generation follows this spec.

- RFC 9457 — Problem Details for HTTP APIs. Recommended format for Frank's error responses (409, 403, 404).

- RFC 6570 — URI Template. Route template format used by JSON Home and Link headers.

- [JSON Home](https://mnot.github.io/I-D/json-home/) — Home documents for HTTP APIs. Frank.Discovery implements this.

## F# Code Generation Precedents

- [FSharp.GrpcCodeGenerator](https://github.com/Arshia001/FSharp.GrpcCodeGenerator) — F#-specific code generation from `.proto` files via MSBuild. Solved file ordering, packaging split, and design-time build integration for F#. Reference for #284 (Frank.Statecharts.Tools).

- [FsGrpc.Tools](https://github.com/dmgtech/fsgrpc) — MSBuild target structure for invoking external code generators before `CoreCompile` with incremental build support. `InsertPosition="0"` pattern for F# compilation ordering.

## HTTP Framework Precedents

- [Webmachine](https://github.com/webmachine/webmachine) — Erlang HTTP resource framework with [decision flowchart](https://github.com/webmachine/webmachine/wiki/Diagram). Protocol-level state machine.

- [Freya](https://github.com/xyncro/freya) — F# HTTP machine framework (archived). Used [Hephaestus](https://github.com/xyncro/hephaestus) graph engine and [Aether](https://github.com/xyncro/aether) optics with [Hekate](https://github.com/xyncro/hekate) graph data structure.

- [Liberator](https://clojure-liberator.github.io/liberator/) — Clojure port of the Webmachine approach.

## Talks and Articles

- Amundsen, M. (2018). [Using Web Sequence Diagrams with your APIs](https://mamund.site44.com/talks/2018-09-restfest/2018-09-restfest-wsd.pdf). RESTFest 2018. — WSD-first API design workflow.

- Riley, R. (2018). [State Transitions through Sequence Diagrams](https://wizardsofsmart.wordpress.com/2018/12/05/state-transitions-through-sequence-diagrams/). F# Advent 2018. — Early Frank statechart-from-WSD work.

## Graph Algorithms

- Tarjan, R. (1972). "Depth-first search and linear graph algorithms." *SIAM Journal on Computing*, 1(2), 146–160. — Strongly connected component detection. Used for livelock analysis in FRANK206.
