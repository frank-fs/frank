module Frank.Cli.Core.Commands.StatechartExtractCommand

open Frank.Resources.Model
open Frank.Cli.Core.Statechart
open Frank.Cli.Core.Statechart.StatechartError

type ExtractResult =
    { StateMachines: ExtractedStatechart list }

/// Extract statechart metadata from an F# project using the compiler.
let execute (projectPath: string) : Async<Result<ExtractResult, StatechartError>> =
    async {
        match! StatechartSourceExtractor.extract projectPath with
        | Ok machines -> return Ok { StateMachines = machines }
        | Error e -> return Error e
    }
