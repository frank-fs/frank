namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// A web link (RFC 8288) contributed to every response.
type WebLink =
    { Target: string
      Rel: string
      Params: (string * string) list }

/// Implemented by libraries that contribute links to responses.
/// Register instances in DI; the provider list is resolved once at startup,
/// while GetLinks is called per request.
type IResponseLinkProvider =
    abstract GetLinks: ctx: HttpContext -> WebLink seq

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module WebLink =

    /// A link with no parameters.
    val create: target: string -> rel: string -> WebLink

    /// Formats a link as an RFC 8288 field value.
    val format: link: WebLink -> string

    /// Middleware appending every provider's links to the Link response header.
    /// Returns None when there are no providers, so callers can skip installing it.
    val middleware: providers: IResponseLinkProvider[] -> (HttpContext -> (unit -> Task) -> Task) option
