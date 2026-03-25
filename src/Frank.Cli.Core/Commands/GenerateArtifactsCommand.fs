module Frank.Cli.Core.Commands.GenerateArtifactsCommand

open System.IO
open System.Text.Json
open Frank.Resources.Model
open Frank.Statecharts.Validation
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Unified.ProjectionPipeline
open Frank.Cli.Core.Statechart
open Frank.Cli.Core.Statechart.StatechartError

type GeneratedArtifact =
    { ResourceSlug: string
      RouteTemplate: string
      Format: FormatTag
      Content: string
      FilePath: string option }

type GenerateResult =
    { Artifacts: GeneratedArtifact list
      OutputDirectory: string option
      GenerationErrors: StatechartError list
      FromCache: bool }

let private parseFormat (s: string) : Result<FormatTag list, StatechartError> =
    match s.ToLowerInvariant() with
    | "wsd" -> Ok [ Wsd ]
    | "alps" -> Ok [ Alps ]
    | "alps-xml"
    | "alpsxml" -> Ok [ AlpsXml ]
    | "scxml" -> Ok [ Scxml ]
    | "smcat" -> Ok [ Smcat ]
    | "xstate" -> Ok [ XState ]
    | "all" -> Ok FormatPipeline.allFormats
    | other -> Error(UnknownFormat other)

let private isAffordanceMapFormat (s: string) : bool = s.ToLowerInvariant() = "affordance-map"

let private generateAffordanceMapJson (resources: UnifiedResource list) (baseUri: string) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
    writer.WriteStartObject()
    writer.WriteString("version", Frank.Resources.Model.AffordanceMap.currentVersion)
    writer.WriteStartArray("entries")

    for resource in resources do
        match resource.Statechart with
        | Some sc ->
            for stateName in sc.StateNames do
                let methods =
                    sc.StateMetadata
                    |> Map.tryFind stateName
                    |> Option.map _.AllowedMethods
                    |> Option.defaultValue []

                writer.WriteStartObject()
                writer.WriteString("routeTemplate", resource.RouteTemplate)
                writer.WriteString("stateKey", stateName)
                writer.WriteStartArray("allowedMethods")

                for m in methods do
                    writer.WriteStringValue(m)

                writer.WriteEndArray()
                writer.WriteStartArray("linkRelations")
                writer.WriteEndArray()
                writer.WriteString("profileUrl", $"{baseUri}/{resource.ResourceSlug}")
                writer.WriteEndObject()
        | None ->
            writer.WriteStartObject()
            writer.WriteString("routeTemplate", resource.RouteTemplate)
            writer.WriteString("stateKey", Frank.Resources.Model.AffordanceMap.WildcardStateKey)
            writer.WriteStartArray("allowedMethods")

            for cap in resource.HttpCapabilities do
                writer.WriteStringValue(cap.Method)

            writer.WriteEndArray()
            writer.WriteStartArray("linkRelations")
            writer.WriteEndArray()
            writer.WriteString("profileUrl", $"{baseUri}{resource.ResourceSlug}")
            writer.WriteEndObject()

    writer.WriteEndArray()
    writer.WriteEndObject()
    writer.Flush()
    System.Text.Encoding.UTF8.GetString(stream.ToArray())

let execute
    (projectPath: string)
    (format: string)
    (baseUri: string)
    (outputDir: string option)
    (resourceFilter: string option)
    (force: bool)
    : Async<Result<GenerateResult, StatechartError>> =
    async {
        if isAffordanceMapFormat format then
            match! UnifiedExtractor.loadOrExtract projectPath force with
            | Error e -> return Error e
            | Ok result ->
                let mapContent = generateAffordanceMapJson result.Resources baseUri

                return
                    Ok
                        { Artifacts =
                            [ { ResourceSlug = "affordance-map"
                                RouteTemplate = "*"
                                Format = Alps
                                Content = mapContent
                                FilePath = None } ]
                          OutputDirectory = outputDir
                          GenerationErrors = []
                          FromCache = result.FromCache }
        else
            match parseFormat format with
            | Error e -> return Error e
            | Ok formats ->
                match! UnifiedExtractor.loadOrExtract projectPath force with
                | Error e -> return Error e
                | Ok result ->
                    let machines = result.Resources |> List.choose (fun r -> r.Statechart)

                    let filtered =
                        match resourceFilter with
                        | None -> machines
                        | Some name ->
                            machines
                            |> List.filter (fun m ->
                                let slug = FormatPipeline.resourceSlug m.RouteTemplate
                                slug = name || m.RouteTemplate.Contains(name))

                    match resourceFilter, filtered with
                    | Some name, [] ->
                        let available =
                            machines |> List.map (fun m -> FormatPipeline.resourceSlug m.RouteTemplate)

                        return Error(ResourceNotFound(name, available))
                    | _ ->
                        let formatResults =
                            [ for m in filtered do
                                  let slug = FormatPipeline.resourceSlug m.RouteTemplate

                                  for fmt in formats do
                                      match FormatPipeline.generateFormatFromExtracted fmt slug m with
                                      | Ok content ->
                                          Ok
                                              { ResourceSlug = slug
                                                RouteTemplate = m.RouteTemplate
                                                Format = fmt
                                                Content = content
                                                FilePath = None }
                                      | Error err -> Error err ]

                        let artifacts =
                            formatResults
                            |> List.choose (function
                                | Ok a -> Some a
                                | Error _ -> None)

                        let formatErrors =
                            formatResults
                            |> List.choose (function
                                | Error e -> Some e
                                | Ok _ -> None)

                        let projectionArtifacts, projectionErrors, projectedSlugs =
                            if formats |> List.contains Alps then
                                // Find UnifiedResources that match the filtered statecharts
                                let filteredRoutes = filtered |> List.map (fun m -> m.RouteTemplate) |> Set.ofList

                                let matchingResources =
                                    result.Resources
                                    |> List.filter (fun r -> Set.contains r.RouteTemplate filteredRoutes)

                                let batchResult, projectionResults =
                                    ProjectionPipeline.projectAllResources matchingResources baseUri

                                // Print warnings from projection
                                for result in projectionResults do
                                    for err in result.Errors do
                                        eprintfn "%s" err

                                    for orphan in result.Orphans do
                                        eprintfn
                                            "Warning: transition '%s' (%s -> %s) is not covered by any role projection"
                                            orphan.Event
                                            orphan.Source
                                            orphan.Target

                                // Build artifacts from projection results
                                let projResults =
                                    [ for result in projectionResults do
                                          for KeyValue(rSlug, content) in result.RoleProfiles do
                                              Ok
                                                  { ResourceSlug = rSlug
                                                    RouteTemplate = "*"
                                                    Format = Alps
                                                    Content = content
                                                    FilePath = None }

                                          for err in result.Errors do
                                              Error(GenerationFailed(Alps, "projection", err)) ]

                                // Add global profile overrides as artifacts
                                let globalArts =
                                    batchResult.GlobalProfileOverrides
                                    |> Map.toList
                                    |> List.choose (fun (slug, content) ->
                                        let route =
                                            matchingResources
                                            |> List.tryFind (fun r -> r.ResourceSlug = slug)
                                            |> Option.map _.RouteTemplate
                                            |> Option.defaultValue "*"

                                        Some
                                            { ResourceSlug = slug
                                              RouteTemplate = route
                                              Format = Alps
                                              Content = content
                                              FilePath = None })

                                let arts =
                                    projResults
                                    |> List.choose (function
                                        | Ok a -> Some a
                                        | Error _ -> None)

                                let errs =
                                    projResults
                                    |> List.choose (function
                                        | Error e -> Some e
                                        | Ok _ -> None)

                                (arts @ globalArts, errs, batchResult.ProjectedSlugs)
                            else
                                ([], [], Set.empty)

                        let generationErrors = List.rev formatErrors @ List.rev projectionErrors

                        let mergedArtifacts =
                            let baseArtifacts =
                                artifacts
                                |> List.filter (fun a ->
                                    not (a.Format = Alps && Set.contains a.ResourceSlug projectedSlugs))

                            baseArtifacts @ projectionArtifacts

                        match outputDir with
                        | None ->
                            return
                                Ok
                                    { Artifacts = mergedArtifacts
                                      OutputDirectory = None
                                      GenerationErrors = generationErrors
                                      FromCache = result.FromCache }
                        | Some dir ->
                            Directory.CreateDirectory(dir) |> ignore

                            let written =
                                mergedArtifacts
                                |> List.map (fun a ->
                                    let fileName =
                                        sprintf "%s%s" a.ResourceSlug (FormatDetector.formatExtension a.Format)

                                    let filePath = Path.Combine(dir, fileName)
                                    File.WriteAllText(filePath, a.Content)
                                    { a with FilePath = Some filePath })

                            return
                                Ok
                                    { Artifacts = written
                                      OutputDirectory = Some dir
                                      GenerationErrors = generationErrors
                                      FromCache = result.FromCache }
    }
