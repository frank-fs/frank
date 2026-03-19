module Frank.Cli.Core.Commands.UnifiedExtractCommand

open System
open System.IO
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Statechart.StatechartError

type UnifiedExtractResult =
    { ResourceCount: int
      StatefulResourceCount: int
      PlainResourceCount: int
      TypeCount: int
      Warnings: string list
      CacheFilePath: string
      FromCache: bool
      State: UnifiedExtractionState }

let private collectWarnings (resources: UnifiedResource list) : string list =
    resources
    |> List.collect (fun r ->
        let orphanWarnings =
            r.DerivedFields.OrphanStates
            |> List.map (fun s ->
                $"Resource {r.RouteTemplate}: state '{s}' is declared but never handled in inState/forState")

        let unhandledWarnings =
            r.DerivedFields.UnhandledCases
            |> List.map (fun c ->
                $"Resource {r.RouteTemplate}: DU case '{c}' not found in statechart state list")

        orphanWarnings @ unhandledWarnings)

let execute
    (projectPath: string)
    (baseUri: string)
    (vocabularies: string list)
    (force: bool)
    : Async<Result<UnifiedExtractResult, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error(FileNotFound projectPath)
        else
            let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))

            let cacheResult = UnifiedCache.tryLoadFresh projectDir force

            match cacheResult with
            | Ok cachedState ->
                let warnings = collectWarnings cachedState.Resources

                return
                    Ok
                        { ResourceCount = cachedState.Resources.Length
                          StatefulResourceCount =
                            cachedState.Resources
                            |> List.filter (fun r -> r.Statechart.IsSome)
                            |> List.length
                          PlainResourceCount =
                            cachedState.Resources
                            |> List.filter (fun r -> r.Statechart.IsNone)
                            |> List.length
                          TypeCount =
                            cachedState.Resources
                            |> List.collect (fun r -> r.TypeInfo)
                            |> List.distinctBy _.FullName
                            |> List.length
                          Warnings = warnings
                          CacheFilePath = UnifiedCache.cachePath projectDir
                          FromCache = true
                          State = cachedState }

            | Error _ ->
                match! UnifiedExtractor.extract projectPath with
                | Error e -> return Error e
                | Ok resources ->
                    let state =
                        match UnifiedCache.saveExtractionState projectDir resources baseUri vocabularies with
                        | Ok s -> s
                        | Error _ ->
                            { Resources = resources
                              SourceHash = ""
                              BaseUri = baseUri
                              Vocabularies = vocabularies
                              ExtractedAt = DateTimeOffset.UtcNow
                              ToolVersion = UnifiedCache.currentToolVersion }

                    let warnings = collectWarnings resources

                    return
                        Ok
                            { ResourceCount = resources.Length
                              StatefulResourceCount =
                                resources
                                |> List.filter (fun r -> r.Statechart.IsSome)
                                |> List.length
                              PlainResourceCount =
                                resources
                                |> List.filter (fun r -> r.Statechart.IsNone)
                                |> List.length
                              TypeCount =
                                resources
                                |> List.collect (fun r -> r.TypeInfo)
                                |> List.distinctBy _.FullName
                                |> List.length
                              Warnings = warnings
                              CacheFilePath = UnifiedCache.cachePath projectDir
                              FromCache = false
                              State = state }
    }
