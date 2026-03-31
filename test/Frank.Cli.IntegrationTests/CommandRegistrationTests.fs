module Frank.Cli.IntegrationTests.CommandRegistrationTests

open System
open System.IO
open Expecto

/// Captures both stdout and stderr while running Program.main with the given args.
/// Returns (exitCode, stdout, stderr).
/// Must be called from a sequenced test context to avoid Console redirection races.
let private runCliCapturingOutput (args: string array) =
    let oldOut = Console.Out
    let oldErr = Console.Error
    let outWriter = new StringWriter()
    let errWriter = new StringWriter()

    try
        Console.SetOut(outWriter)
        Console.SetError(errWriter)
        let exitCode = Program.main args
        Console.SetOut(oldOut)
        Console.SetError(oldErr)
        let outStr = outWriter.ToString()
        let errStr = errWriter.ToString()
        outWriter.Dispose()
        errWriter.Dispose()
        (exitCode, outStr, errStr)
    with ex ->
        Console.SetOut(oldOut)
        Console.SetError(oldErr)
        outWriter.Dispose()
        errWriter.Dispose()
        reraise ()

[<Tests>]
let tests =
    testSequenced
    <| testList
        "Command Registration"
        [ testCase "compile is registered as a root command"
          <| fun _ ->
              // 'frank compile --help' should return 0 and show compile's own help text
              let (exitCode, stdout, stderr) = runCliCapturingOutput [| "compile"; "--help" |]
              let allOutput = stdout + stderr
              Expect.equal exitCode 0 $"'frank compile --help' should succeed (exit code 0), output: [{allOutput}]"
              // The help output should contain the compile command's description
              Expect.stringContains
                  allOutput
                  "Generate OWL/XML and SHACL"
                  "Help should show compile command description"
              // Should show --project option
              Expect.stringContains allOutput "--project" "Help should show --project option"
              // Should show --base-uri option
              Expect.stringContains allOutput "--base-uri" "Help should show --base-uri option"

          testCase "compile appears in root help listing"
          <| fun _ ->
              // 'frank --help' should list compile as a top-level command
              let (exitCode, stdout, stderr) = runCliCapturingOutput [| "--help" |]
              let allOutput = stdout + stderr
              Expect.equal exitCode 0 "'frank --help' should succeed"
              Expect.stringContains allOutput "compile" "Root help should list compile command"

          testCase "compile is not a subcommand of semantic"
          <| fun _ ->
              // 'frank semantic --help' should NOT list compile
              let (exitCode, stdout, stderr) = runCliCapturingOutput [| "semantic"; "--help" |]
              let allOutput = stdout + stderr
              Expect.equal exitCode 0 "'frank semantic --help' should succeed"
              // semantic --help should list extract, clarify, validate, diff, openapi-validate
              Expect.stringContains allOutput "extract" "semantic --help should list extract"
              // compile should NOT appear in semantic's subcommand list
              Expect.isFalse
                  (allOutput.Contains("compile"))
                  "semantic --help should NOT list compile (it was moved to root)"

          testCase "compile --help shows --project-options-file"
          <| fun _ ->
              // 'frank compile --help' should list the --project-options-file option
              let (exitCode, stdout, stderr) = runCliCapturingOutput [| "compile"; "--help" |]
              let allOutput = stdout + stderr
              Expect.equal exitCode 0 $"'frank compile --help' should succeed, output: [{allOutput}]"

              Expect.stringContains
                  allOutput
                  "--project-options-file"
                  "compile --help should show --project-options-file option" ]
