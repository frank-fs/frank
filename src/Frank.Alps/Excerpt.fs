namespace Frank.Alps

open System
open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.DependencyInjection

type CurrentStateResolver = string -> Uri list

module Excerpt =
    let rec satisfiesState (current: Uri) (candidate: Descriptor) : bool =
        (candidate.Def = Some current) || (candidate.Descriptors |> List.exists (satisfiesState current))

module Alps =
    let private routePatternOf (ctx: HttpContext) : string =
        match ctx.GetEndpoint() with
        | :? RouteEndpoint as re -> re.RoutePattern.RawText
        | _ -> failwith "Frank.Alps: Alps.excerpt requires a routed endpoint"

    /// The root document's rootUri, read back from the AlpsOptions `useAlps` registered as a DI
    /// singleton (WebHostBuilderExtensions.install in AlpsDocument.fs) -- so a served excerpt's
    /// cross-document references point at wherever the app actually configured the full document to
    /// live, not a hardcoded assumption. Falls back to AlpsOptions.Default when no `useAlps` was ever
    /// composed: `Alps.excerpt` can be wired into a `negotiate { }` block on its own, with no app-wide
    /// document registered at all, and still needs a working rootUri for descriptors it references
    /// that aren't in this excerpt -- the default path is exactly where a full document would land if
    /// one were added later, so a same-app cross-reference still resolves correctly once it is.
    let private rootUriFor (ctx: HttpContext) : Uri =
        let resolved = ctx.RequestServices.GetService<AlpsOptions>()
        let options = if obj.ReferenceEquals(resolved, null) then AlpsOptions.Default else resolved
        Uri(options.Path, UriKind.Relative)

    let excerpt (resolver: CurrentStateResolver option) : RequestDelegate =
        RequestDelegate(fun ctx ->
            (task {
                let pairs = EndpointSurface.descriptorsForRoute ctx.RequestServices (routePatternOf ctx)
                let! authAllowed = AuthorizationFilter.filter ctx pairs
                let allowedIds = authAllowed |> List.map (fun d -> d.Id) |> Set.ofList

                let stateFiltered =
                    match resolver with
                    | None -> authAllowed
                    | Some resolve ->
                        match resolve ctx.Request.Path.Value with
                        | [] -> authAllowed
                        | activeStates ->
                            authAllowed
                            |> List.filter (fun d ->
                                List.isEmpty d.From
                                || d.From
                                   |> List.exists (fun candidate -> activeStates |> List.exists (fun s -> Excerpt.satisfiesState s candidate)))

                // `descriptorsForRoute` yields the descriptors bound directly to this route's endpoints,
                // so the roots here are already authorization-checked -- but nothing in the type system
                // stops a bound transition from carrying `contains` children of its own, and
                // Serialization.toJson recurses into `Descriptors` unconditionally. Without this the
                // nested children would be served unfiltered, exactly the hole the app-wide document had.
                // Pruning keeps every root (each root's id is in `allowedIds` by construction) and every
                // `Semantic` child (vocabulary, e.g. the request fields of an `unsafe` transition), and
                // drops a nested *transition* that this route's own authorization evaluation never
                // covered -- fail closed, matching AuthorizationFilter's own posture.
                let served = DescriptorTree.prune allowedIds stateFiltered

                if AuthorizationFilter.varies pairs then
                    // Both HTTP exposures need this whenever filtering is principal-dependent, not just
                    // the app-wide document (design doc, *HTTP surface*): without it a shared cache could
                    // hand one principal's filtered excerpt to a different principal at the same URL.
                    // Mirrors AlpsDocument.documentHandler, which mirrors Frank.JsonHome's own -- except
                    // that Vary is APPENDED rather than assigned. AlpsDocument and Frank.JsonHome each
                    // own a dedicated endpoint and can assign; the excerpt is documented to be wired
                    // inside a `negotiate { }` block, and FrankProducesMatcherPolicy has already
                    // appended "Accept" at the routing layer, before this endpoint's handler runs
                    // (RFC 9110 §12.5.5). Assigning would drop it,
                    // making the response cacheable across clients with different Accept headers.
                    ctx.Response.Headers.CacheControl <- "private, no-cache"
                    ctx.Response.Headers.Append("Vary", "Authorization")

                ctx.Response.ContentType <- AlpsDocument.MediaType
                return! ctx.Response.WriteAsync(Serialization.toJson (rootUriFor ctx) served)
             })
            :> Task)
