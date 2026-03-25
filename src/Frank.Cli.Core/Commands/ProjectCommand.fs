module Frank.Cli.Core.Commands.ProjectCommand

open System.IO
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Unified.ProjectionPipeline
open Frank.Cli.Core.Statechart.StatechartError

type ArtifactKind =
    | RoleProfile of roleName: string
    | GlobalOverride

type ProjectedArtifact =
    { ProfileSlug: string
      ResourceSlug: string
      Content: string
      FilePath: string option
      Kind: ArtifactKind }

type RoleProjectionResult =
    { Artifacts: ProjectedArtifact list
      OrphanWarnings: string list
      Errors: string list
      OutputDirectory: string option
      FromCache: bool }

let execute
    (projectPath: string)
    (baseUri: string)
    (outputDir: string option)
    (resourceFilter: string option)
    (force: bool)
    : Async<Result<RoleProjectionResult, StatechartError>> =
    async {
        let! resourcesResult = UnifiedExtractor.loadOrExtract projectPath force

        match resourcesResult with
        | Error e -> return Error e
        | Ok result ->
            let filtered =
                match resourceFilter with
                | None -> result.Resources
                | Some name ->
                    result.Resources
                    |> List.filter (fun r -> r.ResourceSlug = name || r.RouteTemplate.Contains(name))

            match resourceFilter, filtered with
            | Some name, [] ->
                let available = result.Resources |> List.map _.ResourceSlug
                return Error(ResourceNotFound(name, available))
            | _ ->
                let batchResult, projectionResults = projectAllResources filtered baseUri

                let orphanWarnings =
                    [ for result in projectionResults do
                          for orphan in result.Orphans do
                              $"Warning: transition '{orphan.Event}' ({orphan.Source} -> {orphan.Target}) is not covered by any role projection" ]

                let errors =
                    [ for result in projectionResults do
                          yield! result.Errors ]

                let roleArtifacts =
                    [ for result in projectionResults do
                          for KeyValue(profileSlug, content) in result.RoleProfiles do
                              let roleName =
                                  result.RoleNameByProfileSlug
                                  |> Map.tryFind profileSlug
                                  |> Option.defaultValue profileSlug

                              { ProfileSlug = profileSlug
                                ResourceSlug = result.ResourceSlug
                                Content = content
                                FilePath = None
                                Kind = RoleProfile roleName } ]

                let globalArtifacts =
                    batchResult.GlobalProfileOverrides
                    |> Map.toList
                    |> List.map (fun (resourceSlug, content) ->
                        { ProfileSlug = resourceSlug
                          ResourceSlug = resourceSlug
                          Content = content
                          FilePath = None
                          Kind = GlobalOverride })

                let allArtifacts = roleArtifacts @ globalArtifacts

                match outputDir with
                | None ->
                    return
                        Ok
                            { Artifacts = allArtifacts
                              OrphanWarnings = orphanWarnings
                              Errors = errors
                              OutputDirectory = None
                              FromCache = result.FromCache }
                | Some dir ->
                    Directory.CreateDirectory(dir) |> ignore

                    let written =
                        allArtifacts
                        |> List.map (fun a ->
                            let filePath = Path.Combine(dir, $"{a.ProfileSlug}.alps.json")
                            File.WriteAllText(filePath, a.Content)
                            { a with FilePath = Some filePath })

                    return
                        Ok
                            { Artifacts = written
                              OrphanWarnings = orphanWarnings
                              Errors = errors
                              OutputDirectory = Some dir
                              FromCache = result.FromCache }
    }
