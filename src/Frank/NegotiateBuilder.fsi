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
    val negotiate: NegotiateBuilder
