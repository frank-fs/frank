namespace Frank.Builder

open System.Text
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives

type WebLink =
    { Target: string
      Rel: string
      Params: (string * string) list }

type IResponseLinkProvider =
    abstract GetLinks: ctx: HttpContext -> WebLink seq

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module WebLink =

    let create target rel =
        { Target = target; Rel = rel; Params = [] }

    let private escape (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let format (link: WebLink) =
        let sb = StringBuilder()

        sb
            .Append('<')
            .Append(link.Target)
            .Append(">; rel=\"")
            .Append(escape link.Rel)
            .Append('"')
        |> ignore

        for name, value in link.Params do
            sb.Append("; ").Append(name).Append("=\"").Append(escape value).Append('"')
            |> ignore

        sb.ToString()

    let middleware (providers: IResponseLinkProvider[]) =
        if Array.isEmpty providers then
            None
        else
            Some(fun (ctx: HttpContext) (next: unit -> Task) ->
                let formatted =
                    providers
                    |> Array.collect (fun p -> p.GetLinks ctx |> Seq.map format |> Array.ofSeq)

                if not (Array.isEmpty formatted) then
                    // Append rather than assign: other contributors may have added links.
                    let existing = ctx.Response.Headers.Link
                    ctx.Response.Headers.Link <- StringValues.Concat(existing, StringValues formatted)

                next ())
