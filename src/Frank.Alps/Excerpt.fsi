namespace Frank.Alps

open System
open Microsoft.AspNetCore.Http

/// Answers "what states is this specific resource concurrently in", if the application supplies one --
/// a plain function wired at composition time, no dependency on `Frank.Provenance` or any other package.
/// One element per active orthogonal region (design doc, *State-based filtering*); an empty list means
/// state filtering simply does not apply for this resource, the same as the old `None`. A `from`-state
/// candidate is satisfied if it is satisfied by *any* element of the returned list (existential/OR
/// match across regions) -- this reaches independent-region OR filtering correctly but does not reach
/// conjunctive AND-guards or multi-region fan-out targets; see frank-fs/frank#489 for that.
type CurrentStateResolver = string -> Uri list

module Excerpt =
    /// Whether the resolver's returned `current` state satisfies an authored `from`-state `candidate`,
    /// walking `contains` ancestry: `candidate` matches directly via `Def`, or any of its (recursively
    /// nested) children does. A `candidate` with no `Def` anywhere in its own subtree can never match.
    val satisfiesState: current: Uri -> candidate: Descriptor -> bool

    /// Evaluates a StateGuard against a resolver's active-state Uri list. `State`/`Predicate` use the
    /// existing contains-ancestry match (`satisfiesState`); `All`/`Any`/`Not` fold structurally.
    val satisfiesGuard: activeStates: Uri list -> guard: StateGuard -> bool

    /// Same derivation `ProtocolGraph.deriveGuard` uses -- kept independent (no cross-module dependency
    /// for a five-line rule) rather than shared, since ofProfile derives from a Descriptor list and this
    /// filters Descriptors directly from a different entry point (descriptorsForRoute).
    val deriveGuard: d: Descriptor -> StateGuard option

module Alps =
    /// Serves the ALPS excerpt for the *specific resource* the current request's endpoint belongs to:
    /// every HTTP method's `binds`-bound descriptor sharing this endpoint's route pattern
    /// (`EndpointSurface.descriptorsForRoute`), filtered by principal and, if `resolver` is `Some`, by
    /// `CurrentStateResolver`. Wire this into a `negotiate { }` block's `accepts "application/alps+json"`
    /// case -- this is not automatic middleware (design doc, *HTTP surface*).
    val excerpt: resolver: CurrentStateResolver option -> RequestDelegate
