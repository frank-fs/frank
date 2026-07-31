namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Primitives

type WebLink =
    { Target: string
      Rel: string
      Params: (string * string) list }

type internal ResourceLinkProvider = ResourceLinkProvider of (HttpContext -> WebLink seq)

module WebLink =

    let private escapeParam (value: string) =
        value.Replace("\\", "\\\\").Replace("\"", "\\\"")

    let format (link: WebLink) : string =
        let paramStr =
            link.Params
            |> List.map (fun (name, value) -> "; " + name + "=\"" + escapeParam value + "\"")
            |> String.concat ""

        "<" + link.Target + ">; rel=\"" + escapeParam link.Rel + "\"" + paramStr

    let private appendToResponse (ctx: HttpContext) (links: WebLink list) =
        if not (List.isEmpty links) then
            ctx.Response.OnStarting(fun () ->
                let values = links |> List.map format |> Array.ofList
                ctx.Response.Headers.Append("Link", StringValues values)
                Task.CompletedTask)
            |> ignore

    let useAppWideLinks
        (providers: (HttpContext -> WebLink seq) list)
        (app: IApplicationBuilder)
        : IApplicationBuilder =
        if List.isEmpty providers then
            app
        else
            app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
                let links = [ for provider in providers do yield! provider ctx ]
                appendToResponse ctx links
                next.Invoke ctx)

    let useResourceScopedLinks (app: IApplicationBuilder) : IApplicationBuilder =
        app.Use(fun (ctx: HttpContext) (next: RequestDelegate) ->
            match ctx.GetEndpoint() with
            | null -> ()
            | endpoint ->
                let providers = endpoint.Metadata.GetOrderedMetadata<ResourceLinkProvider>()
                if providers.Count > 0 then
                    let links = [ for ResourceLinkProvider provider in providers do yield! provider ctx ]
                    appendToResponse ctx links

            next.Invoke ctx)
