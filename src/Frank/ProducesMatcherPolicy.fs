namespace Frank.Builder

open System.Threading.Tasks
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.AspNetCore.Routing.Matching
open Microsoft.Net.Http.Headers

[<Sealed>]
type FrankProducesMatcherPolicy() =
    inherit MatcherPolicy()

    // Matches the framework's own NegotiationMatcherPolicy<T> (Accept-Encoding):
    // run very late, after any other policy has already invalidated candidates on
    // other grounds (auth, etc.), so this only negotiates among what's left.
    static let http406Endpoint =
        lazy
            Endpoint(
                (fun ctx ->
                    ctx.Response.StatusCode <- StatusCodes.Status406NotAcceptable
                    Task.CompletedTask),
                EndpointMetadataCollection.Empty,
                "406 HTTP Not Acceptable (Frank negotiate { })"
            )

    override _.Order = 10_000

    interface IEndpointSelectorPolicy with
        member _.AppliesToEndpoints(endpoints) =
            endpoints
            |> Seq.exists (fun e -> not (obj.ReferenceEquals(e.Metadata.GetMetadata<ProducesMediaTypeMetadata>(), null)))

        member _.ApplyAsync(httpContext, candidates) =
            let raw: System.Collections.Generic.IList<string> = httpContext.Request.Headers.Accept |> Array.ofSeq :> _

            let parsed =
                match MediaTypeHeaderValue.TryParseList(raw) with
                | true, values -> values |> List.ofSeq
                | false, _ -> []

            let parsed =
                if List.isEmpty parsed then
                    [ MediaTypeHeaderValue.Parse("*/*") ]
                else
                    parsed

            let mutable sawTaggedCandidate = false
            let mutable bestIndex = -1
            let mutable bestQuality = 0.0
            let mutable bestOrdinal = System.Int32.MaxValue

            for i in 0 .. candidates.Count - 1 do
                if candidates.IsValidCandidate(i) then
                    let metadata = candidates.[i].Endpoint.Metadata.GetMetadata<ProducesMediaTypeMetadata>()

                    if not (obj.ReferenceEquals(metadata, null)) then
                        sawTaggedCandidate <- true

                        match MediaTypeNegotiation.effectiveQuality parsed metadata.MediaType with
                        | Some quality when quality > 0.0 ->
                            if
                                bestIndex < 0
                                || quality > bestQuality
                                || (quality = bestQuality && metadata.Ordinal < bestOrdinal)
                            then
                                bestIndex <- i
                                bestQuality <- quality
                                bestOrdinal <- metadata.Ordinal
                        | _ -> ()

            if sawTaggedCandidate then
                httpContext.Response.Headers.Append("Vary", "Accept")

                if bestIndex < 0 then
                    httpContext.SetEndpoint(http406Endpoint.Value)
                    httpContext.Request.RouteValues <- null
                else
                    for i in 0 .. candidates.Count - 1 do
                        if i <> bestIndex && candidates.IsValidCandidate(i) then
                            let metadata = candidates.[i].Endpoint.Metadata.GetMetadata<ProducesMediaTypeMetadata>()

                            if not (obj.ReferenceEquals(metadata, null)) then
                                candidates.SetValidity(i, false)

                    let winner = candidates.[bestIndex].Endpoint.Metadata.GetMetadata<ProducesMediaTypeMetadata>()

                    if not (MediaTypeNegotiation.isWildcard winner.MediaType) then
                        httpContext.Response.ContentType <- winner.MediaType

            Task.CompletedTask
