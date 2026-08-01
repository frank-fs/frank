namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// One representation: a media type (an exact type, or a "*/*"/"type/*" wildcard
/// catch-all) paired with the RequestDelegate that produces it. Representations are
/// independent of each other -- there is no shared object serialized differently per
/// entry, unlike IOutputFormatter's model.
type NegotiateSpec =
    { Representations: (string * RequestDelegate) list
      Metadata: obj list }

    static member Empty: NegotiateSpec

[<Sealed>]
type NegotiateBuilder =
    new: unit -> NegotiateBuilder

    member Yield: 'T -> NegotiateSpec
    member Run: spec: NegotiateSpec -> HandlerDefinition

    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: RequestDelegate -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handler: (HttpContext -> unit) -> NegotiateSpec
    [<CustomOperation("accepts")>]
    member Accepts: spec: NegotiateSpec * mediaType: string * handlerDef: HandlerDefinition -> NegotiateSpec
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
    /// `accepts` operation and dispatches to whichever the client's Accept header
    /// selects.
    ///
    /// NAME COLLISION: this shares the identifier `negotiate` with the unrelated
    /// function `Frank.ContentNegotiation.negotiate` (`statusCode -> body -> ctx -> Task`,
    /// which delegates to MVC's IOutputFormatter registry). With both `open Frank.Builder`
    /// and `open Frank.ContentNegotiation` in scope, F#'s normal shadowing rules apply and
    /// the LAST `open` wins. If you need both in the same file, qualify at least one of
    /// them (`Frank.ContentNegotiation.negotiate 200 body ctx`, or `ctx.Negotiate(200, body)`
    /// via the HttpContext extension, which never collides).
    val negotiate: NegotiateBuilder
