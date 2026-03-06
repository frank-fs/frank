namespace Frank.Cli.Core.Commands

open System.IO
open Frank.Cli.Core.State

/// Structured diff between extraction states.
module DiffCommand =

    type DiffCommandResult =
        { Diff: DiffResult
          FormattedDiff: string }

    let private findLatestBackup (projectDir: string) : string option =
        let backupDir = Path.Combine(projectDir, "obj", "frank-cli", "backups")
        if Directory.Exists backupDir then
            Directory.GetFiles(backupDir, "extraction-state-*.json")
            |> Array.sortDescending
            |> Array.tryHead
        else
            None

    let private emptyResult () =
        let emptyDiff =
            { Added = []; Removed = []; Modified = [] }
        Ok
            { Diff = emptyDiff
              FormattedDiff = "No previous state found. Nothing to compare." }

    let execute (projectPath: string) (previousPath: string option) : Result<DiffCommandResult, string> =
        let projectDir = Path.GetDirectoryName projectPath
        let currentStatePath = ExtractionState.defaultStatePath projectDir

        match ExtractionState.load currentStatePath with
        | Error e -> Error $"Failed to load current state: {e}"
        | Ok currentState ->

        let resolvedPrevious =
            match previousPath with
            | Some p -> Some p
            | None -> findLatestBackup projectDir

        match resolvedPrevious with
        | None -> emptyResult ()
        | Some prevPath ->

        match ExtractionState.load prevPath with
        | Error e -> Error $"Failed to load previous state: {e}"
        | Ok previousState ->

        let diff = DiffEngine.diffStates previousState currentState
        let formatted = DiffEngine.formatDiff diff

        Ok
            { Diff = diff
              FormattedDiff = formatted }
