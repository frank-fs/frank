module Frank.Cli.Core.Commands.StatechartGenerateCommand

open System.IO
open Frank.Statecharts.Validation
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
      GenerationErrors: StatechartError list }

let private parseFormat (s: string) : Result<FormatTag list, StatechartError> =
    match s.ToLowerInvariant() with
    | "wsd" -> Ok [ Wsd ]
    | "alps" -> Ok [ Alps ]
    | "alps-xml" | "alpsxml" -> Ok [ AlpsXml ]
    | "scxml" -> Ok [ Scxml ]
    | "smcat" -> Ok [ Smcat ]
    | "xstate" -> Ok [ XState ]
    | "all" -> Ok FormatPipeline.allFormats
    | other -> Error (UnknownFormat other)

let execute
    (projectPath: string)
    (format: string)
    (outputDir: string option)
    (resourceFilter: string option)
    : Async<Result<GenerateResult, StatechartError>> =
    async {
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
                        machines
                        |> List.map (fun m -> FormatPipeline.resourceSlug m.RouteTemplate)

                    return Error (ResourceNotFound(name, available))
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
                                  | Error err ->
                                      generationErrors <- err :: generationErrors ]

                    match outputDir with
                    | None ->
                        return Ok { Artifacts = artifacts
                                    OutputDirectory = None
                                    GenerationErrors = List.rev generationErrors }
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

                        return Ok { Artifacts = written
                                    OutputDirectory = Some dir
                                    GenerationErrors = List.rev generationErrors }
    }
