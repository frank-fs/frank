namespace Frank.Builder

open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Matching

/// Routing-layer counterpart to the framework's own `AcceptsMatcherPolicy`
/// (request Content-Type / Consumes) -- this one dispatches by response
/// representation, keyed on the `Accept` request header and each candidate
/// endpoint's `ProducesMediaTypeMetadata`. Registered as a `MatcherPolicy`
/// singleton by `webHost { }` (`WebHostBuilder.fs`) unconditionally; a no-op
/// for any app with no `ProducesMediaTypeMetadata`-tagged endpoints.
[<Sealed>]
type FrankProducesMatcherPolicy =
    inherit MatcherPolicy
    new: unit -> FrankProducesMatcherPolicy
    override Order: int
    interface IEndpointSelectorPolicy
