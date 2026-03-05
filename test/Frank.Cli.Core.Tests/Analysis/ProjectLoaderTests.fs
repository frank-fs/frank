module Frank.Cli.Core.Tests.Analysis.ProjectLoaderTests

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
    ]
