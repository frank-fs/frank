namespace Frank.JsonHome

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Frank.Builder

type JsonHomeOptions =
    { /// Path the document is served from.
      Path: string
      /// Link relation type used when advertising the document.
      Rel: string
      /// Optional api.title member.
      Title: string option
      /// Optional api.links members.
      Links: (string * string) list }

    /// Path "/.well-known/home.json", rel "home", no api member.
    static member Default: JsonHomeOptions

module JsonHome =

    [<Literal>]
    val MediaType: string = "application/json-home"

    /// Renders resources as a draft-06 JSON Home document.
    val serialize: options: JsonHomeOptions -> resources: ResourceDescription list -> string

    /// Writes the document as an HTTP response.
    val write: options: JsonHomeOptions -> resources: ResourceDescription list -> ctx: HttpContext -> Task

    /// The resource that serves the document itself. Add its Endpoints to
    /// WebHostSpec.Endpoints rather than a separate app.UseEndpoints(...) call:
    /// that dispatches through the same, single, structurally-last routing
    /// stage every other Frank resource uses, so it runs after any
    /// authentication/authorization middleware the app has composed --
    /// regardless of where useJsonHome appears in the webHost {} block. A raw
    /// path-matching middleware, or an independent UseEndpoints(...) call
    /// placed ahead of that middleware, cannot make that guarantee: an
    /// endpoint matched by an earlier UseEndpoints(...) call dispatches
    /// straight through, without ever reaching code sandwiched between it and
    /// a later one, no matter which resource "owns" that later call.
    val documentResource: options: JsonHomeOptions -> Resource
