namespace Frank.JsonHome

open Microsoft.AspNetCore.Mvc.ApiExplorer

type ResourceDescription =
    { Rel: string
      Href: string
      IsTemplated: bool
      HrefVars: (string * string) list
      Methods: string list
      Formats: string list
      Accepts: (string * string list) list
      AcceptRanges: string list
      AcceptPrefer: string list
      PreconditionRequired: Precondition list
      AuthSchemes: (string * string list) list
      Docs: string option
      Status: ResourceStatus option
      Metadata: obj list
      MethodMetadata: (string * obj list) list }

module ApiSurface =

    let private metadataOf (description: ApiDescription) =
        match description.ActionDescriptor with
        | null -> []
        | action ->
            match action.EndpointMetadata with
            | null -> []
            | items -> List.ofSeq items

    let private pick<'T when 'T: not struct> (metadata: obj list) =
        metadata
        |> List.tryPick (fun m ->
            match m with
            | :? 'T as t -> Some t
            | _ -> None)

    let private pickAll<'T when 'T: not struct> (metadata: obj list) =
        metadata
        |> List.choose (fun m ->
            match m with
            | :? 'T as t -> Some t
            | _ -> None)

    let private responseFormats (description: ApiDescription) =
        description.SupportedResponseTypes
        |> Seq.filter (fun r -> r.StatusCode >= 200 && r.StatusCode < 300)
        |> Seq.collect (fun r -> r.ApiResponseFormats |> Seq.map (fun f -> f.MediaType))
        |> Seq.distinct
        |> List.ofSeq

    let private requestFormats (description: ApiDescription) =
        description.SupportedRequestFormats
        |> Seq.map (fun f -> f.MediaType)
        |> Seq.distinct
        |> List.ofSeq

    let ofApiDescriptions (descriptions: ApiDescription seq) : ResourceDescription list =
        descriptions
        |> Seq.filter (fun d -> not (isNull d.RelativePath))
        |> Seq.groupBy (fun d -> d.RelativePath)
        |> Seq.choose (fun (relativePath, group) ->
            let group = List.ofSeq group
            let methodMetadata = group |> List.map (fun d -> d.HttpMethod, metadataOf d)
            let metadata = methodMetadata |> List.collect snd

            match pick<RelMetadata> metadata with
            | None -> None
            | Some rel ->
                let routeTemplate = "/" + relativePath.TrimStart '/'

                let accepts =
                    group
                    |> List.choose (fun d ->
                        match requestFormats d with
                        | [] -> None
                        | formats -> Some(d.HttpMethod, formats))

                let formats =
                    group
                    |> List.tryFind (fun d -> d.HttpMethod = "GET")
                    |> Option.map responseFormats
                    |> Option.defaultValue []

                Some
                    { Rel = rel.Rel
                      Href = UriTemplate.ofRouteTemplate routeTemplate
                      IsTemplated = UriTemplate.isTemplated routeTemplate
                      HrefVars = pickAll<HrefVarMetadata> metadata |> List.map (fun v -> v.Name, v.Uri)
                      Methods = group |> List.map (fun d -> d.HttpMethod) |> List.distinct
                      Formats = formats
                      Accepts = accepts
                      AcceptRanges =
                        pick<AcceptRangesMetadata> metadata
                        |> Option.map (fun r -> r.Units)
                        |> Option.defaultValue []
                      AcceptPrefer =
                        pick<AcceptPreferMetadata> metadata
                        |> Option.map (fun p -> p.Preferences)
                        |> Option.defaultValue []
                      PreconditionRequired =
                        pick<PreconditionRequiredMetadata> metadata
                        |> Option.map (fun p -> p.Preconditions)
                        |> Option.defaultValue []
                      AuthSchemes = pickAll<AuthSchemeMetadata> metadata |> List.map (fun s -> s.Scheme, s.Realms)
                      Docs = pick<DocsMetadata> metadata |> Option.map (fun d -> d.Uri)
                      Status = pick<StatusMetadata> metadata |> Option.map (fun s -> s.Status)
                      Metadata = metadata
                      MethodMetadata = methodMetadata })
        |> List.ofSeq
