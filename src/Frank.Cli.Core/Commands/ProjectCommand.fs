module Frank.Cli.Core.Commands.ProjectCommand

open System.IO
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Unified.ProjectionPipeline
open Frank.Cli.Core.Statechart.StatechartError

type ProjectedArtifact =
    { Slug: string
      Content: string
      FilePath: string option
      IsGlobalOverride: bool }

type ProjectResult =
    { Artifacts: ProjectedArtifact list
      OrphanWarnings: string list
      Errors: string list
      OutputDirectory: string option
      FromCache: bool }

let execute
    (projectPath: string)
    (outputDir: string option)
    (resourceFilter: string option)
    (force: bool)
    : Async<Result<ProjectResult, StatechartError>> =
    async {
        let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))

        let! resourcesResult =
            async {
                match UnifiedCache.tryLoadFresh projectDir force with
                | Ok state -> return Ok(state.Resources, true)
                | Error _ ->
                    match! UnifiedExtractor.extract projectPath with
                    | Ok resources -> return Ok(resources, false)
                    | Error e -> return Error e
            }

        match resourcesResult with
        | Error e -> return Error e
        | Ok(resources, fromCache) ->
            let filtered =
                match resourceFilter with
                | None -> resources
                | Some name ->
                    resources
                    |> List.filter (fun r ->
                        r.ResourceSlug = name || r.RouteTemplate.Contains(name))

            match resourceFilter, filtered with
            | Some name, [] ->
                let available = resources |> List.map _.ResourceSlug
                return Error(ResourceNotFound(name, available))
            | _ ->
                let batchResult, projectionResults = projectAllResources filtered ""

                let orphanWarnings =
                    [ for result in projectionResults do
                          for orphan in result.Orphans do
                              $"Warning: transition '{orphan.Event}' ({orphan.Source} -> {orphan.Target}) is not covered by any role projection" ]

                let errors =
                    [ for result in projectionResults do
                          yield! result.Errors ]

                let roleArtifacts =
                    [ for result in projectionResults do
                          for KeyValue(slug, content) in result.RoleProfiles do
                              { Slug = slug
                                Content = content
                                FilePath = None
                                IsGlobalOverride = false } ]

                let globalArtifacts =
                    batchResult.GlobalProfileOverrides
                    |> Map.toList
                    |> List.map (fun (slug, content) ->
                        { Slug = slug
                          Content = content
                          FilePath = None
                          IsGlobalOverride = true })

                let allArtifacts = roleArtifacts @ globalArtifacts

                match outputDir with
                | None ->
                    return
                        Ok
                            { Artifacts = allArtifacts
                              OrphanWarnings = orphanWarnings
                              Errors = errors
                              OutputDirectory = None
                              FromCache = fromCache }
                | Some dir ->
                    Directory.CreateDirectory(dir) |> ignore

                    let written =
                        allArtifacts
                        |> List.map (fun a ->
                            let filePath = Path.Combine(dir, $"{a.Slug}.alps.json")
                            File.WriteAllText(filePath, a.Content)
                            { a with FilePath = Some filePath })

                    return
                        Ok
                            { Artifacts = written
                              OrphanWarnings = orphanWarnings
                              Errors = errors
                              OutputDirectory = Some dir
                              FromCache = fromCache }
    }
