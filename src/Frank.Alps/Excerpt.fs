namespace Frank.Alps

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing

type CurrentStateResolver = string -> Uri option

module Excerpt =
    let rec satisfiesState (current: Uri) (candidate: Descriptor) : bool =
        (candidate.Def = Some current) || (candidate.Descriptors |> List.exists (satisfiesState current))

module Alps =
    let private routePatternOf (ctx: HttpContext) : string =
        match ctx.GetEndpoint() with
        | :? RouteEndpoint as re -> re.RoutePattern.RawText
        | _ -> failwith "Frank.Alps: Alps.excerpt requires a routed endpoint"

    let excerpt (resolver: CurrentStateResolver option) : RequestDelegate =
        RequestDelegate(fun ctx ->
            (task {
                let pairs = EndpointSurface.descriptorsForRoute ctx.RequestServices (routePatternOf ctx)
                let! authAllowed = AuthorizationFilter.filter ctx pairs

                let stateFiltered =
                    match resolver with
                    | None -> authAllowed
                    | Some resolve ->
                        match resolve ctx.Request.Path.Value with
                        | None -> authAllowed
                        | Some current ->
                            authAllowed
                            |> List.filter (fun d ->
                                List.isEmpty d.From || d.From |> List.exists (Excerpt.satisfiesState current))

                ctx.Response.ContentType <- "application/alps+json"
                return! ctx.Response.WriteAsync(Serialization.toJson stateFiltered)
             })
            :> Task)
