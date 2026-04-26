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

- Deniélou, P. & Yoshida, N. (2012). [Multiparty Asynchronous Session Types Meet Communicating Automata](https://doi.org/10.1007/978-3-642-28869-2_10). *ECOOP '12*. — Bridges MPST and communicating finite-state automata; ancestor to the projection algorithm Frank adopts.

- Capecchi, S., Giachino, E., Yoshida, N. (2010). [Global Escape in Multiparty Sessions](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.FSTTCS.2010.338). *FSTTCS '10*. — Exception-like escape mechanism for session-type hierarchies.

- Demangeon, F. & Honda, K. (2012). [Nested Protocols in Session Types](https://doi.org/10.1007/978-3-642-32940-1_20). *CONCUR '12*. — Protocol composition via nested session types; ancestor to Frank's hierarchical protocol nesting.

- Majumdar, R., Mukund, M., Stutz, P., Zufferey, D. (2021). [Generalising Projection in Asynchronous Multiparty Session Types](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.CONCUR.2021.35). *CONCUR '21*. — Sound but not complete predecessor to the CAV 2023 complete projection algorithm.

- Li, Z., Stutz, P., Wies, T., Zufferey, D. (2023). [Complete Multiparty Session Type Projection with Automata](https://arxiv.org/abs/2305.17079). *CAV '23*. — Complete automata-theoretic projection of global protocols to per-role local types. The projection algorithm Frank's protocol-types track adopts.

- Yoshida, N., Gheri, L., et al. (2021). [Communicating Finite State Machines and an Extensible Toolchain for MPST (nuScr)](https://doi.org/10.1007/978-3-030-86593-1_2). *ESSOS '21*. — Extended MPST toolchain with nested-protocol support; Scribble variant.

- Gheri, L. & Yoshida, N. (2023). [Hybrid Multiparty Session Types](https://arxiv.org/abs/2302.01979). *POPL '23*. — Session types with compositionality via subprotocols.

- Udomsrirungruang, N. & Yoshida, N. (2025). [Top-Down or Bottom-Up? Complexity Analyses of Synchronous Multiparty Session Types](https://mrg.cs.ox.ac.uk/publications/top-down-or-bottom-up-complexity-analyses-of-synchronous-multiparty-session-types/main.pdf). *POPL '25*. — Complexity bounds for synchronous MPST verification, relevant to Frank's build-time projection budget.

- Zhou, F., Ferreira, F., Hu, R., Neykova, R., Yoshida, N. (2020). [Statically Verified Refinements for Multiparty Protocols](https://arxiv.org/abs/2009.06541). *OOPSLA '20*. — Refinement-typed session-type API generated from Scribble on F\*. Closest existing cousin to Frank on .NET.

- [Scribble](http://www.scribble.org/) — Protocol description language for multiparty session types. Frank accepts Scribble syntax as an alternate intake format (Appendix R of the v7.4.0 protocol-types spec).

- [nuScr](https://nuscr.dev) — Modern MPST authoring tool and toolchain. Frank's protocol algebra accepts nuScr-style intake.

- [MPST at Imperial College](https://mrg.doc.ic.ac.uk/publications/multiparty-asynchronous-session-types/) — Research group and publications.

## Effects, Sessions, and Their Correspondence

- Plotkin, G.D. & Pretnar, M. (2009). [Handlers of Algebraic Effects](https://doi.org/10.1007/978-3-642-00590-9_7). *ESOP '09*. — Algebraic effect handlers; theoretical anchor for Frank's effect-as-message encoding.

- Lindley, S., McBride, C., McLaughlin, C. (2017). [Do Be Do Be Do](https://doi.org/10.1145/3009837.3009897). *POPL '17*. — Frank, the language: session types embedded in a monadic functional language via effect handlers. Namesake and inspiration.

- Orchard, D. & Yoshida, N. (2016). [Effects as Sessions, Sessions as Effects](https://doi.org/10.1145/2837614.2837634). *POPL '16*. — Theoretical anchor for Frank's effect-to-message wiring; isomorphism between effect systems and session types.

- Orchard, D. & Yoshida, N. (2015). [Using Session Types as an Effect System](https://arxiv.org/abs/1602.03591). *PLACES '15*. — Session types recast as an effect system.

- Hillerström, D., Lindley, S., Atkey, R., Sivaramakrishnan, K.C. (2017). [Continuation-Passing Style for Effect Handlers](https://drops.dagstuhl.de/entities/document/10.4230/LIPIcs.FSCD.2017.18). *FSCD '17*. — CPS encoding of effect handlers; precursor to Frank's delimited-continuation codegen contingency.

- Forster, Y., Kammar, O., Lindley, S., Pretnar, M. (2019). [On the Expressive Power of User-Defined Effects](https://doi.org/10.1017/S0956796819000121). *JFP* 29. — Expressiveness bounds on algebraic effects; relevant to Frank's effect-composition design.

- Danvy, O. & Filinski, A. (1990). [Abstracting Control](https://doi.org/10.1145/91556.91622). *LFP '90*. — Delimited continuations; foundation for Frank's contingency codegen pattern.

## Tagless Final and Free Monads in F#

- Carette, J., Kiselyov, O., Shan, C.-c. (2009). [Finally Tagless, Partially Evaluated](https://doi.org/10.1017/S0956796809007205). *JFP* 19(5). — Foundational tagless-final / HKT encoding pattern; Frank uses the witness-object variant.

- Yallop, J. & White, L. (2014). [Lightweight Higher-Kinded Polymorphism](https://www.cl.cam.ac.uk/~jdy22/papers/lightweight-higher-kinded-polymorphism.pdf). *FLOPS '14*. — Witness-object / branded-type HKT encoding for languages without native support; the pattern Frank adopts for effect encoding.

- Lämmel, R. & Peyton Jones, S. (2005). [Scrap Your Boilerplate with Class](https://www.microsoft.com/en-us/research/publication/scrap-your-boilerplate-with-class/). *ICFP '05*. — Recursive type-class dictionary pattern for generic traversals.

- Reynolds, J.C. (1998). [Definitional Interpreters for Higher-Order Programming Languages](https://doi.org/10.1023/A:1010027404223). *Higher-Order & Symbolic Computation* 11(4). — Foundational definitional-interpreter pattern underlying free-monad / tagless-final DSL encoding.

- McBride, C. & Paterson, R. (2008). [Applicative Programming with Effects](https://doi.org/10.1017/S0956796807006326). *JFP* 18(1). — Applicative functor framing; bounds on the witness-object encoding.

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

## Coalgebraic and Denotational Foundations

- Keizer, B., Basold, H., Pérez, J.A. (2021). [Coalgebraic Semantics of Multiparty Session Types](https://arxiv.org/abs/2011.05712). *ESOP '21*. — Coalgebraic view of session types; the framing Frank's foundational algebra adopts.

- Keizer, B., Basold, H., Pérez, J.A. (2022). [Coalgebraic Semantics of Multiparty Session Types](https://doi.org/10.1145/3527633). *TOPLAS* 44(1). — Extended journal version of the ESOP paper.

- Gibbons, J. (2022). [Continuation-Passing Style, Defunctionalisation, Accumulations, and Associativity](https://arxiv.org/abs/2111.10413). *Programming Journal* 6(1). — Defunctionalisation and CPS transformations; theoretical anchor for codegen strategies.

## Concurrent Systems and Actors

- Reppy, J.H. (1999). *Concurrent Programming in ML*. Cambridge University Press. — Foundational synchronous-combinators pattern; informs Frank's CML-style codegen contingency.

- Reppy, J.H., Russo, C.V., Xiao, Y. (2009). [Parallel Concurrent ML](https://doi.org/10.1145/1631687.1596588). *ICFP '09*. — Parallel extensions to CML; reference for hierarchical parallel-region codegen.

- [Hopac](https://github.com/Hopac/Hopac) — F# port of the Concurrent ML model. Pattern reference for Frank's synchronous-combinator codegen contingency.

- [Proto.Actor](https://github.com/asynkron/protoactor-dotnet) — .NET actor framework with supervision strategies. Candidate runtime backend for Frank.

- [Akka.NET](https://getakka.net/) — Port of Akka to .NET; mature supervision model, applicable as a Frank runtime backend.

- [Microsoft Orleans](https://learn.microsoft.com/en-us/dotnet/orleans/overview) — Virtual actors (grains) on .NET; alternative supervision model for distributed Frank deployments.

- [Dapr Actors](https://docs.dapr.io/developing-applications/building-blocks/actors/actors-overview/) — Sidecar-based language-agnostic actors; bridge to non-.NET Frank participants.

## Semantic Web Standards

- [ALPS Specification](http://alps.io/spec/drafts/draft-01.html) — Application-Level Profile Semantics. Frank's primary semantic vocabulary for describing application state and transitions.

- [RDF 1.2](https://www.w3.org/TR/rdf12-concepts/) — Resource Description Framework. Foundation for Frank's linked-data surface.

- [SHACL](https://www.w3.org/TR/shacl/) — Shapes Constraint Language. Used by Frank's validation surface for constraint checking.

- [PROV-O](https://www.w3.org/TR/prov-o/) — W3C Provenance Ontology. Used by Frank's provenance surface for agent/activity/entity triples.

- [JSON-LD 1.1](https://www.w3.org/TR/json-ld11/) — JSON for Linking Data. One of Frank's content-negotiation formats.

## Resource-Oriented and Hypermedia Design

- Fielding, R.T. (2000). [Architectural Styles and the Design of Network-based Software Architectures](https://www.ics.uci.edu/~fielding/pubs/dissertation/top.htm). Doctoral dissertation, UC Irvine. — REST formulation. Frank implements the resource-oriented architectural style Fielding describes.

- Oliveira, M.G.B., Turine, M.A.S., Masiero, P.C. (2001). "A Statechart-Based Model for Hypermedia Applications (HMBS)." *ACM TOIS* 19(1), 28–52. — Formalizes the statecharts-to-hypermedia binding. Frank generalizes HMBS for multi-party protocols.

- Richardson, L. & Amundsen, M. (2013). [RESTful Web APIs](https://www.oreilly.com/library/view/restful-web-apis/9781449359713/). O'Reilly. — Practical hypermedia API design; Frank's representation-agnosticism aligns with this pedagogy.

- Wlaschin, S. [Designing with Types: Making Illegal States Unrepresentable](https://fsharpforfunandprofit.com/posts/designing-with-types-making-illegal-states-unrepresentable/) — Encoding constraints at the type level. Frank applies this principle at the hypermedia layer via affordance derivation.

- [JSON:API Specification](https://jsonapi.org/) — Hypermedia format with typed relations. Frank supports JSON:API rendering via bridge packages.

- [Siren](https://github.com/kevinswiber/siren) — Hypermedia format with actions.

## HTTP Standards

- RFC 9110 — HTTP Semantics. `Allow` header (§10.2.1), method safety (§9.2.1), conditional requests.

- RFC 8288 — Web Linking. Link header format, relation types. Frank's Link header generation follows this spec.

- RFC 9457 — Problem Details for HTTP APIs. Recommended format for Frank's error responses (409, 403, 404).

- RFC 6570 — URI Template. Route template format used by JSON Home and Link headers.

- [JSON Home](https://mnot.github.io/I-D/json-home/) — Home documents for HTTP APIs. Frank's discovery surface implements this.

## Protocol Tooling Across Languages

- [Rumpsteak](https://github.com/zakcutner/rumpsteak) — Generates Rust APIs from Scribble. Precedent for multi-language codegen from MPST protocols.

- [Ferrite](https://github.com/ferrite-rs/ferrite) — Session types in Rust via the typestate pattern. Precedent for embedded-DSL protocol encoding.

- Neykova, R., Yoshida, N., et al. [STMonitor / MPST-TS](https://doi.org/10.1145/3446804.3446854). *CC '21*. — Routed MPST for TypeScript.

- [Effpi](https://github.com/alcestes/effpi) — Session types integrated with Akka Typed for Scala. Runtime-backend precedent for Frank.

## F# Code Generation Precedents

- [FSharp.GrpcCodeGenerator](https://github.com/Arshia001/FSharp.GrpcCodeGenerator) — F#-specific code generation from `.proto` files via MSBuild. Solved file ordering, packaging split, and design-time build integration for F#.

- [FsGrpc.Tools](https://github.com/dmgtech/fsgrpc) — MSBuild target structure for invoking external code generators before `CoreCompile` with incremental build support. `InsertPosition="0"` pattern for F# compilation ordering.

- [Myriad](https://github.com/MoiraeSoftware/Myriad) — MSBuild-driven F# code generation via text templating. Considered and rejected for Frank in favour of CLI-driven generation with deterministic emission.

- [Fantomas.Core](https://github.com/fsprojects/fantomas) — F# source formatter; usable to emit clean code from syntax trees. Used by Frank for code emission.

- [Fabulous.AST](https://github.com/edgarfgp/Fabulous.AST) — Computation-expression DSL for constructing F# AST on Fantomas. Candidate for Frank's actor-template emission pipeline.

- [F# Compiler Service (FCS)](https://github.com/dotnet/fsharp/tree/main/src/Compiler/Service) — Analysis and metaprogramming API for F#. Used by Frank's CLI for type extraction.

- [F# Language Suggestion #864](https://github.com/fsharp/fslang-suggestions/issues/864) — Pending source-generator feature; Frank relies on CLI + MSBuild targets instead.

- [Roslyn Source Generators (C#)](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/source-generators-overview) — First-class code generation in C#. Frank generates C# consumers as needed for interop.

## Verification and Analysis

- [Z3 SMT Solver](https://github.com/Z3Prover/z3) — Satisfiability modulo theories solver. Used by Frank for protocol verification.

- [F\* Verification Assistant](https://fstar-lang.org) — Dependently-typed proof assistant for .NET. Candidate for Frank's deferred verification pipeline.

## HTTP Framework Precedents

- [Webmachine](https://github.com/webmachine/webmachine) — Erlang HTTP resource framework with [decision flowchart](https://github.com/webmachine/webmachine/wiki/Diagram). Protocol-level state machine.

- [Freya](https://github.com/xyncro/freya) — F# HTTP machine framework (archived). Used [Hephaestus](https://github.com/xyncro/hephaestus) graph engine and [Aether](https://github.com/xyncro/aether) optics with [Hekate](https://github.com/xyncro/hekate) graph data structure.

- [Liberator](https://clojure-liberator.github.io/liberator/) — Clojure port of the Webmachine approach.

## Talks and Articles

- Amundsen, M. (2018). [Using Web Sequence Diagrams with your APIs](https://mamund.site44.com/talks/2018-09-restfest/2018-09-restfest-wsd.pdf). RESTFest 2018. — WSD-first API design workflow.

- Riley, R. (2018). [State Transitions through Sequence Diagrams](https://wizardsofsmart.wordpress.com/2018/12/05/state-transitions-through-sequence-diagrams/). F# Advent 2018. — Early Frank statechart-from-WSD work.

## Graph Algorithms

- Tarjan, R. (1972). "Depth-first search and linear graph algorithms." *SIAM Journal on Computing*, 1(2), 146–160. — Strongly connected component detection. Used for livelock analysis.
