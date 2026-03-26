namespace Frank.Discovery

open Microsoft.AspNetCore.Routing
open Frank.Builder

/// Pure projection from EndpointDataSource + optional metadata to JsonHomeInput.
/// No side effects, no DI — receives all data as parameters.
module JsonHomeProjection =

    /// Route prefixes that indicate framework-internal endpoints (not user resources).
    /// Matched against the route template after stripping the leading '/'.
    let private internalPrefixes =
        [| ".well-known/"
           "alps/"
           "ontology/"
           "shapes/"
           "schemas/"
           "scalar/"
           "openapi/" |]

    let private isInternalEndpoint (rawText: string) =
        let normalized = rawText.TrimStart('/')

        internalPrefixes
        |> Array.exists (fun prefix -> normalized.StartsWith(prefix, System.StringComparison.OrdinalIgnoreCase))

    /// Extract route variable names from a RouteEndpoint using RoutePattern.Parameters.
    let private extractRouteVariables
        (endpoint: RouteEndpoint)
        (slug: string)
        (alpsDescriptors: Map<string, Map<string, string>>)
        (assemblyName: string)
        =
        endpoint.RoutePattern.Parameters
        |> Seq.map (fun param ->
            let varName = param.Name

            let uri =
                match Map.tryFind slug alpsDescriptors with
                | Some descriptors -> Map.tryFind varName descriptors
                | None -> None
                |> Option.defaultValue (sprintf "urn:frank:%s/param/%s" assemblyName varName)

            varName, uri)
        |> Map.ofSeq

    /// Derive a collision-free fragment identifier from a route template.
    /// /games → "games", /games/{gameId} → "games-gameId"
    let private routeToFragment (routeTemplate: string) =
        routeTemplate.TrimStart('/').Replace("/", "-").Replace("{", "").Replace("}", "")

    /// Derive the link relation type for a resource.
    /// Uses route-template-based fragments to avoid slug collisions (#200).
    /// Requires AlpsBaseUri to be absolute per RFC 8288 (#201).
    let private deriveRelationType
        (slug: string)
        (routeTemplate: string)
        (alpsBaseUri: string option)
        (alpsDescriptors: Map<string, Map<string, string>>)
        (assemblyName: string)
        =
        match alpsBaseUri, Map.tryFind slug alpsDescriptors with
        | Some baseUri, Some _ when System.Uri.IsWellFormedUriString(baseUri, System.UriKind.Absolute) ->
            sprintf "%s#%s" baseUri (routeToFragment routeTemplate)
        | _ -> sprintf "urn:frank:%s%s" assemblyName routeTemplate

    /// Project an EndpointDataSource into a JsonHomeInput.
    let project
        (dataSource: EndpointDataSource)
        (metadata: JsonHomeMetadata option)
        (assemblyName: string)
        : JsonHomeInput =

        let title =
            metadata |> Option.bind (fun m -> m.Title) |> Option.defaultValue assemblyName

        let docsUrl = metadata |> Option.bind (fun m -> m.DocsUrl)

        let alpsBaseUri = metadata |> Option.bind (fun m -> m.AlpsBaseUri)

        let alpsDescriptors =
            metadata
            |> Option.bind (fun m -> m.AlpsDescriptors)
            |> Option.defaultValue Map.empty

        let describedByUrl =
            dataSource.Endpoints
            |> Seq.exists (fun ep ->
                match ep with
                | :? RouteEndpoint as re ->
                    let raw = re.RoutePattern.RawText
                    not (isNull raw) && raw.Contains(".well-known/frank-profiles")
                | _ -> false)
            |> fun found -> if found then Some "/.well-known/frank-profiles" else None

        let resources =
            dataSource.Endpoints
            |> Seq.choose (fun ep ->
                match ep with
                | :? RouteEndpoint as re ->
                    let rawText = re.RoutePattern.RawText

                    if isNull rawText || isInternalEndpoint rawText then
                        None
                    else
                        Some(rawText, re)
                | _ -> None)
            |> Seq.groupBy fst
            |> Seq.map (fun (routeTemplate, endpoints) ->
                let allEndpoints = endpoints |> Seq.map snd |> Seq.toList
                let slug = routeTemplate.TrimStart('/').Split('/') |> Array.head

                let routeVars =
                    allEndpoints
                    |> List.tryHead
                    |> Option.map (fun ep -> extractRouteVariables ep slug alpsDescriptors assemblyName)
                    |> Option.defaultValue Map.empty

                let getHttpMethods (ep: RouteEndpoint) =
                    let meta = ep.Metadata.GetMetadata<HttpMethodMetadata>()
                    if isNull meta then [] else meta.HttpMethods |> Seq.toList

                let getMediaTypes (ep: RouteEndpoint) =
                    ep.Metadata
                    |> Seq.choose (fun m ->
                        match m with
                        | :? DiscoveryMediaType as d -> Some d.MediaType
                        | _ -> None)
                    |> Seq.toList

                let allMethods =
                    allEndpoints |> List.collect getHttpMethods |> List.distinct |> List.sort

                let collectMediaTypes methodPredicate =
                    allEndpoints
                    |> List.filter (fun ep -> getHttpMethods ep |> List.exists methodPredicate)
                    |> List.collect getMediaTypes
                    |> List.distinct

                let getFormats = collectMediaTypes (fun m -> m = "GET" || m = "HEAD")

                let collectAcceptTypes methodName =
                    let types = collectMediaTypes (fun m -> m = methodName)
                    if types.IsEmpty then None else Some types

                let relationType =
                    deriveRelationType slug routeTemplate alpsBaseUri alpsDescriptors assemblyName

                { RelationType = relationType
                  RouteTemplate = routeTemplate
                  RouteVariables = routeVars
                  Hints =
                    { Allow = allMethods
                      Formats = getFormats
                      AcceptPost = collectAcceptTypes "POST"
                      AcceptPut = collectAcceptTypes "PUT"
                      AcceptPatch = collectAcceptTypes "PATCH"
                      DocsUrl = docsUrl } })
            |> Seq.toList

        { Title = title
          DescribedByUrl = describedByUrl
          Resources = resources }
