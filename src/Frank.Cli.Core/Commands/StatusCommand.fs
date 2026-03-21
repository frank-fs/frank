namespace Frank.Cli.Core.Commands

open System.IO
open Frank.Cli.Core.Help
open Frank.Cli.Core.State

/// Project status inspection logic that determines extraction state,
/// artifact presence, and recommended next action.
module StatusCommand =

    let private artifactFileNames =
        [ "ontology.owl.xml"; "shapes.shacl.ttl"; "manifest.json" ]

    let private checkArtifacts (stateDir: string) : ArtifactStatus =
        let missing =
            artifactFileNames
            |> List.filter (fun name ->
                not (File.Exists(Path.Combine(stateDir, name))))
        if missing.IsEmpty then Present
        else Missing missing

    let private determineAction (extraction: ExtractionStatus) (artifacts: ArtifactStatus) : RecommendedAction =
        match extraction, artifacts with
        | NotExtracted, _ -> RunExtract
        | Unreadable reason, _ -> RecoverExtract reason
        | Stale, _ -> ReExtract
        | Current, Missing _ -> RunCompile
        | Current, Present -> UpToDate

    let execute (projectPath: string) : Result<ProjectStatus, string> =
        if not (File.Exists projectPath) then
            Error $"Project file not found: {projectPath}"
        else
            let projectDir = Path.GetDirectoryName(projectPath)
            let stateDir = Path.Combine(projectDir, "obj", "frank")
            let statePath = ExtractionState.defaultStatePath projectDir

            let extraction =
                if not (File.Exists statePath) then
                    NotExtracted
                else
                    match ExtractionState.load statePath with
                    | Error reason -> Unreadable reason
                    | Ok state ->
                        match StalenessChecker.checkStaleness state with
                        | StalenessResult.Stale -> ExtractionStatus.Stale
                        | StalenessResult.Fresh -> Current
                        | StalenessResult.Indeterminate -> Current

            let artifacts = checkArtifacts stateDir
            let action = determineAction extraction artifacts

            Ok
                { ProjectPath = projectPath
                  Extraction = extraction
                  Artifacts = artifacts
                  RecommendedAction = action
                  StateDirectory = stateDir }
