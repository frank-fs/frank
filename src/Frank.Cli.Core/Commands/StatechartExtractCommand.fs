module Frank.Cli.Core.Commands.StatechartExtractCommand

open Frank.Resources.Model
open Frank.Cli.Core.Statechart.StatechartError
open Frank.Cli.Core.Unified

type ExtractResult =
    { StateMachines: ExtractedStatechart list }

/// Extract statechart metadata from an F# project using the compiler.
let execute (projectPath: string) : Async<Result<ExtractResult, StatechartError>> =
    async {
        match! UnifiedExtractor.loadOrExtract projectPath false with
        | Ok result ->
            let machines = result.Resources |> List.choose _.Statechart
            return Ok { StateMachines = machines }
        | Error e -> return Error e
    }
