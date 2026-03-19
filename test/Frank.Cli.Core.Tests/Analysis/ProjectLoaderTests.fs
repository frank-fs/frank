module Frank.Cli.Core.Tests.Analysis.ProjectLoaderTests

open System.IO
open Expecto
open Frank.Cli.Core.Analysis

[<Tests>]
let tests =
    testList "ProjectLoader" [
        testCaseAsync "non-existent project returns Error" <| async {
            let! result = ProjectLoader.loadProject "/nonexistent/path/project.fsproj"
            match result with
            | Error msg ->
                Expect.stringContains msg "not found" "Should indicate file not found"
            | Ok _ ->
                failwith "Should return Error for non-existent project"
        }

        testCaseAsync "loads real F# project and returns source files" <| async {
            let fixturesPath =
                Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "Fixtures.fsproj"))

            let! result = ProjectLoader.loadProject fixturesPath
            match result with
            | Error e -> failwith $"Should load successfully: {e}"
            | Ok loaded ->
                Expect.isGreaterThan loaded.ParsedFiles.Length 0 "Should have parsed files"
                let fileNames = loaded.ParsedFiles |> List.map (fun (path, _) -> Path.GetFileName path)
                Expect.contains fileNames "SimpleTypes.fs" "Should contain SimpleTypes.fs"
                Expect.contains fileNames "ConstraintAttributes.fs" "Should contain ConstraintAttributes.fs"
        }

        testCaseAsync "loaded project has type-check results" <| async {
            let fixturesPath =
                Path.GetFullPath(Path.Combine(__SOURCE_DIRECTORY__, "..", "Fixtures", "Fixtures.fsproj"))

            let! result = ProjectLoader.loadProject fixturesPath
            match result with
            | Error e -> failwith $"Should load successfully: {e}"
            | Ok loaded ->
                Expect.isFalse loaded.CheckResults.HasCriticalErrors "Should have no critical errors"
                let entities =
                    loaded.CheckResults.AssemblySignature.Entities |> Seq.length
                Expect.isGreaterThan entities 0 "Should have type entities"
        }
    ]
