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
      FromCache: bool }

let private buildResult (fromCache: bool) (resources: UnifiedResource list) : UnifiedValidateResult =
    let withRoles =
        resources
        |> List.choose (fun r ->
            r.Statechart
            |> Option.bind (fun sc -> if sc.Roles.IsEmpty then None else Some(r.RouteTemplate, sc)))

    let results = withRoles |> List.map (fun (route, sc) -> validateProjection route sc)

    let totalIssues = results |> List.sumBy (fun r -> r.Issues.Length)

    let totalErrors =
        results
        |> List.sumBy (fun r -> r.Issues |> List.filter (fun i -> i.Severity = Severity.Error) |> List.length)

    let totalWarnings =
        results
        |> List.sumBy (fun r -> r.Issues |> List.filter (fun i -> i.Severity = Severity.Warning) |> List.length)

    { ProjectionResults = results
      TotalIssues = totalIssues
      TotalErrors = totalErrors
      TotalWarnings = totalWarnings
      ResourcesChecked = withRoles.Length
      FromCache = fromCache }

let execute (projectPath: string) (force: bool) : Async<Result<UnifiedValidateResult, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error(FileNotFound projectPath)
        else
            let projectDir = Path.GetDirectoryName(Path.GetFullPath(projectPath))

            match UnifiedCache.tryLoadFresh projectDir force with
            | Ok state -> return Ok(buildResult true state.Resources)
            | Error _ ->
                match! UnifiedExtractor.extract projectPath with
                | Error e -> return Error e
                | Ok resources -> return Ok(buildResult false resources)
    }
