namespace Frank.Cli.Core.Commands

open Frank.Cli.Core.State

/// Structured diff between extraction states.
module DiffCommand =

    type DiffCommandResult =
        { Diff: DiffResult
          FormattedDiff: string }

    let execute (oldStatePath: string) (newStatePath: string) : Result<DiffCommandResult, string> =
        match ExtractionState.load oldStatePath with
        | Error e -> Error $"Failed to load old state: {e}"
        | Ok oldState ->

        match ExtractionState.load newStatePath with
        | Error e -> Error $"Failed to load new state: {e}"
        | Ok newState ->

        let diff = DiffEngine.diffStates oldState newState
        let formatted = DiffEngine.formatDiff diff

        Ok
            { Diff = diff
              FormattedDiff = formatted }
