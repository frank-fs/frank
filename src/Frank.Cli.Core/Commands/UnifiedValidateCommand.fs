module Frank.Cli.Core.Commands.UnifiedValidateCommand

open System.IO
open Frank.Resources.Model
open Frank.Statecharts.Analysis.ProjectionValidator
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Statechart.StatechartError

type UnifiedValidateResult =
    { ProjectionResults: ProjectionCheckResult list
      TotalIssues: int
      TotalErrors: int
      TotalWarnings: int
      ResourcesChecked: int
      ProgressReports: ProgressAnalysis.ProgressReport list
      HasProgressErrors: bool
      FromCache: bool }

let private buildResult
    (fromCache: bool)
    (resources: UnifiedResource list)
    (checkProjection: bool)
    (checkProgress: bool)
    : UnifiedValidateResult =
    let withRoles =
        resources
        |> List.choose (fun r ->
            r.Statechart
            |> Option.bind (fun sc -> if sc.Roles.IsEmpty then None else Some(r.RouteTemplate, sc)))

    let results =
        if checkProjection then
            withRoles |> List.map (fun (route, sc) -> validateProjection route sc)
        else
            []

    let totalIssues = results |> List.sumBy (fun r -> r.Issues.Length)

    let totalErrors =
        results
        |> List.sumBy (fun r -> r.Issues |> List.filter (fun i -> i.Severity = Severity.Error) |> List.length)

    let totalWarnings =
        results
        |> List.sumBy (fun r -> r.Issues |> List.filter (fun i -> i.Severity = Severity.Warning) |> List.length)

    let progressReports =
        if checkProgress then
            resources
            |> List.choose (fun r ->
                r.Statechart
                |> Option.bind (fun sc -> if sc.Roles.IsEmpty then None else Some sc)
                |> Option.map ProgressAnalysis.analyzeProgress)
        else
            []

    let hasProgressErrors = progressReports |> List.exists (fun r -> r.HasErrors)

    { ProjectionResults = results
      TotalIssues = totalIssues
      TotalErrors = totalErrors
      TotalWarnings = totalWarnings
      ResourcesChecked = withRoles.Length
      ProgressReports = progressReports
      HasProgressErrors = hasProgressErrors
      FromCache = fromCache }

let execute
    (projectPath: string)
    (force: bool)
    (checkProjection: bool)
    (checkProgress: bool)
    : Async<Result<UnifiedValidateResult, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error(FileNotFound projectPath)
        else
            let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))

            match UnifiedCache.tryLoadFresh projectDir force with
            | Ok state -> return Ok(buildResult true state.Resources checkProjection checkProgress)
            | Error _ ->
                match! UnifiedExtractor.extract projectPath with
                | Error e -> return Error e
                | Ok resources -> return Ok(buildResult false resources checkProjection checkProgress)
    }
