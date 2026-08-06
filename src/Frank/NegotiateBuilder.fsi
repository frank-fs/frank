namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// One representation: a media type (an exact type, or a "*/*"/"type/*" wildcard
/// catch-all) paired with the RequestDelegate that produces it and that
/// representation's own metadata. Representations are independent of each other --
/// there is no shared object serialized differently per entry, unlike
/// IOutputFormatter's model.
type NegotiateSpec =
    { Representations: (string * RequestDelegate * obj list) list }

    static member Empty: NegotiateSpec

[<Sealed>]
type NegotiateBuilder =
    new: unit -> NegotiateBuilder

    member Yield: 'T -> NegotiateSpec
    /// Builds one `HandlerDefinition` per registered representation -- dispatch among
    /// them happens at the routing layer (`FrankProducesMatcherPolicy`), not here.
    /// Every representation's `HandlerDefinition.Metadata` carries its own
    /// `ProducesMediaTypeMetadata` tag (used by the matcher policy), then its OWN
    /// non-`produces` metadata, then the SAME broadcast-merged `produces` metadata
    /// (see `Negotiation.mergeProducesMetadata`) that every sibling carries. Only
    /// `produces` metadata is broadcast -- anything else a representation declares
    /// stays on that representation's endpoint alone.
    member Run: spec: NegotiateSpec -> HandlerDefinition list

    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: RequestDelegate -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> unit) -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handlerDef: HandlerDefinition -> NegotiateSpec
    /// A self-writing async handler -- what a `task { ... }` block with no `return`
    /// infers as. Dispatched directly, like `RequestDelegate`/`HttpContext -> unit`;
    /// never routed through `viaOutputFormatter`. See frank-fs/frank#492.
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> Task<unit>) -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> Task<'a>) -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> Async<'a>) -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaTypes: string list * handler: (HttpContext -> Task<'a>) -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaTypes: string list * handler: (HttpContext -> Async<'a>) -> NegotiateSpec

[<AutoOpen>]
module NegotiateFunctions =
    /// The `negotiate { }` computation expression: registers one representation per
    /// `accepts` operation. `Run` builds one `HandlerDefinition` per representation;
    /// which one serves a given request is decided at the routing layer, by
    /// `FrankProducesMatcherPolicy`, based on the client's Accept header.
    ///
    /// NAME COLLISION: this shares the identifier `negotiate` with the unrelated
    /// function `Frank.ContentNegotiation.negotiate` (`statusCode -> body -> ctx -> Task`,
    /// which delegates to MVC's IOutputFormatter registry). With both `open Frank.Builder`
    /// and `open Frank.ContentNegotiation` in scope, F#'s normal shadowing rules apply and
    /// the LAST `open` wins. If you need both in the same file, qualify at least one of
    /// them (`Frank.ContentNegotiation.negotiate 200 body ctx`, or `ctx.Negotiate(200, body)`
    /// via the HttpContext extension, which never collides).
    val negotiate: NegotiateBuilder
