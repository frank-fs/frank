namespace Frank.JsonHome

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

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
