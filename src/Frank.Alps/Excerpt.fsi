namespace Frank.Alps

open System
open Microsoft.AspNetCore.Http

/// Answers "what state is this specific resource in", if the application supplies one -- a plain
/// function wired at composition time, no dependency on `Frank.Provenance` or any other package. The
/// natural implementation queries a provenance/event store; absent, or returning `None`, means state
/// filtering simply does not apply (design doc, *State-based filtering*).
type CurrentStateResolver = string -> Uri option

module Excerpt =
    /// Whether the resolver's returned `current` state satisfies an authored `from`-state `candidate`,
    /// walking `contains` ancestry: `candidate` matches directly via `Def`, or any of its (recursively
    /// nested) children does. A `candidate` with no `Def` anywhere in its own subtree can never match.
    val satisfiesState: current: Uri -> candidate: Descriptor -> bool

module Alps =
    /// Serves the ALPS excerpt for the *specific resource* the current request's endpoint belongs to:
    /// every HTTP method's `binds`-bound descriptor sharing this endpoint's route pattern
    /// (`EndpointSurface.descriptorsForRoute`), filtered by principal and, if `resolver` is `Some`, by
    /// `CurrentStateResolver`. Wire this into a `negotiate { }` block's `accepts "application/alps+json"`
    /// case -- this is not automatic middleware (design doc, *HTTP surface*).
    val excerpt: resolver: CurrentStateResolver option -> RequestDelegate
