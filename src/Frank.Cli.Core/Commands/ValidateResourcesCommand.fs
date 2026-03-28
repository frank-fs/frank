module Frank.Cli.Core.Commands.ValidateResourcesCommand

open System.IO
open Frank.Resources.Model
open Frank.Statecharts.Analysis.ProjectionValidator
open Frank.Cli.Core.Unified
open Frank.Cli.Core.Statechart.StatechartError

type ValidateResult =
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
    : ValidateResult =
    let withRoles =
        resources
        |> List.choose (fun r ->
            r.Statechart
            |> Option.bind (fun sc -> if sc.Roles.IsEmpty then None else Some(r.RouteTemplate, sc)))

    let withRolesOrGuards =
        resources
        |> List.choose (fun r ->
            r.Statechart
            |> Option.bind (fun sc ->
                let hasRoles = not sc.Roles.IsEmpty

                let hasGuards =
                    not sc.GuardNames.IsEmpty
                    || sc.Transitions |> List.exists (fun t -> t.Guard.IsSome)

                if hasRoles || hasGuards then
                    Some(r.RouteTemplate, sc)
                else
                    None))

    let results =
        if checkProjection then
            withRolesOrGuards |> List.map (fun (route, sc) -> validateProjection route sc)
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
    : Async<Result<ValidateResult, StatechartError>> =
    async {
        match! UnifiedExtractor.loadOrExtract projectPath force with
        | Error e -> return Error e
        | Ok result -> return Ok(buildResult result.FromCache result.Resources checkProjection checkProgress)
    }
