module Frank.Cli.Core.Commands.UnifiedGenerateCommand

open System.IO
open System.Text.Json
open Frank.Statecharts.Unified
open Frank.Statecharts.Validation
open Frank.Cli.Core.Unified
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

let private isAffordanceMapFormat (s: string) : bool =
    s.ToLowerInvariant() = "affordance-map"

let private generateAffordanceMapJson (resources: UnifiedResource list) (baseUri: string) : string =
    use stream = new MemoryStream()
    use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))
    writer.WriteStartObject()
    writer.WriteString("version", Frank.Affordances.AffordanceMap.currentVersion)
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
                writer.WriteString("profileUrl", $"{baseUri}{resource.ResourceSlug}")
                writer.WriteEndObject()
        | None ->
            writer.WriteStartObject()
            writer.WriteString("routeTemplate", resource.RouteTemplate)
            writer.WriteString("stateKey", Frank.Affordances.AffordanceMap.WildcardStateKey)
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

let private getResources
    (projectPath: string)
    (force: bool)
    : Async<Result<UnifiedResource list * bool, StatechartError>> =
    async {
        let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))

        match UnifiedCache.tryLoadFresh projectDir force with
        | Ok state -> return Ok(state.Resources, true)
        | Error _ ->
            match! UnifiedExtractor.extract projectPath with
            | Ok resources -> return Ok(resources, false)
            | Error e -> return Error e
    }

let execute
    (projectPath: string)
    (format: string)
    (outputDir: string option)
    (resourceFilter: string option)
    (force: bool)
    : Async<Result<GenerateResult, StatechartError>> =
    async {
        if isAffordanceMapFormat format then
            match! getResources projectPath force with
            | Error e -> return Error e
            | Ok(resources, fromCache) ->
                let mapContent = generateAffordanceMapJson resources ""

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
                          FromCache = fromCache }
        else
            match parseFormat format with
            | Error e -> return Error e
            | Ok formats ->
                match! getResources projectPath force with
                | Error e -> return Error e
                | Ok(resources, fromCache) ->
                    let machines =
                        resources |> List.choose (fun r -> r.Statechart)

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
                        let mutable generationErrors = []

                        let artifacts =
                            [ for m in filtered do
                                  let slug = FormatPipeline.resourceSlug m.RouteTemplate

                                  for fmt in formats do
                                      match FormatPipeline.generateFormatFromExtracted fmt slug m with
                                      | Ok content ->
                                          { ResourceSlug = slug
                                            RouteTemplate = m.RouteTemplate
                                            Format = fmt
                                            Content = content
                                            FilePath = None }
                                      | Error err -> generationErrors <- err :: generationErrors ]

                        match outputDir with
                        | None ->
                            return
                                Ok
                                    { Artifacts = artifacts
                                      OutputDirectory = None
                                      GenerationErrors = List.rev generationErrors
                                      FromCache = fromCache }
                        | Some dir ->
                            Directory.CreateDirectory(dir) |> ignore

                            let written =
                                artifacts
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
                                      GenerationErrors = List.rev generationErrors
                                      FromCache = fromCache }
    }
