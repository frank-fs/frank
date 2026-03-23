module Frank.Cli.Core.Commands.StatechartGenerateCommand

open System.IO
open Frank.Resources.Model
open Frank.Statecharts.Validation
open Frank.Cli.Core.Statechart
open Frank.Cli.Core.Statechart.StatechartError
open Frank.Cli.Core.Unified

type GeneratedArtifact =
    { ResourceSlug: string
      RouteTemplate: string
      Format: FormatTag
      Content: string
      FilePath: string option }

type GenerateResult =
    {
        Artifacts: GeneratedArtifact list
        OutputDirectory: string option
        GenerationErrors: StatechartError list
        /// Affordance map JSON output (when --format affordance-map is used)
        AffordanceMapJson: string option
    }

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

/// Generate affordance map JSON from the unified extractor pipeline.
let private executeAffordanceMap
    (projectPath: string)
    (baseUri: string)
    (outputDir: string option)
    : Async<Result<GenerateResult, StatechartError>> =
    async {
        match! UnifiedExtractor.extract projectPath with
        | Error e -> return Error e
        | Ok resources ->
            let json = AffordanceMapGenerator.generate resources baseUri None

            match outputDir with
            | Some dir ->
                Directory.CreateDirectory(dir) |> ignore
                let filePath = Path.Combine(dir, "affordance-map.json")
                File.WriteAllText(filePath, json)

                return
                    Ok
                        { Artifacts = []
                          OutputDirectory = Some dir
                          GenerationErrors = []
                          AffordanceMapJson = Some json }
            | None ->
                return
                    Ok
                        { Artifacts = []
                          OutputDirectory = None
                          GenerationErrors = []
                          AffordanceMapJson = Some json }
    }

let execute
    (projectPath: string)
    (format: string)
    (outputDir: string option)
    (resourceFilter: string option)
    : Async<Result<GenerateResult, StatechartError>> =
    async {
        // Handle affordance-map as a special format (uses unified extractor)
        match format.ToLowerInvariant() with
        | "affordance-map"
        | "affordancemap" ->
            let baseUri = "http://localhost:5000/alps"
            return! executeAffordanceMap projectPath baseUri outputDir
        | _ ->

            match parseFormat format with
            | Error e -> return Error e
            | Ok formats ->
                match! StatechartSourceExtractor.extract projectPath with
                | Error e -> return Error e
                | Ok machines ->
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
                        let results =
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
                            results
                            |> List.choose (function
                                | Ok a -> Some a
                                | Error _ -> None)

                        let generationErrors =
                            results
                            |> List.choose (function
                                | Error e -> Some e
                                | Ok _ -> None)
                            |> List.rev

                        match outputDir with
                        | None ->
                            return
                                Ok
                                    { Artifacts = artifacts
                                      OutputDirectory = None
                                      GenerationErrors = generationErrors
                                      AffordanceMapJson = None }
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
                                      GenerationErrors = generationErrors
                                      AffordanceMapJson = None }
    }

/// Execute affordance-map generation with an explicit base URI.
let executeWithBaseUri
    (projectPath: string)
    (format: string)
    (baseUri: string)
    (outputDir: string option)
    (resourceFilter: string option)
    : Async<Result<GenerateResult, StatechartError>> =
    async {
        match format.ToLowerInvariant() with
        | "affordance-map"
        | "affordancemap" -> return! executeAffordanceMap projectPath baseUri outputDir
        | _ -> return! execute projectPath format outputDir resourceFilter
    }
