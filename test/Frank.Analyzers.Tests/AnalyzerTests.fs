module Frank.Analyzers.Tests.AnalyzerTests

open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Expecto
open Frank.Analyzers.DuplicateHandlerAnalyzer

/// Parse a fixture file and return the ParsedInput.
let private parseFixture (fixturePath: string) =
    let checker = FSharpChecker.Create()
    let sourceText = SourceText.ofString (File.ReadAllText fixturePath)

    let options =
        { FSharpParsingOptions.Default with
            SourceFiles = [| fixturePath |] }

    let parseResult =
        checker.ParseFile(fixturePath, sourceText, options) |> Async.RunSynchronously

    if parseResult.ParseHadErrors then
        failwith $"Parse errors in fixture: {fixturePath}"

    parseResult.ParseTree

let private fixturesDir =
    let assemblyDir = System.AppContext.BaseDirectory

    let rec findRoot (dir: string) =
        let candidate = Path.Combine(dir, "test", "Frank.Analyzers.Tests", "fixtures")

        if Directory.Exists candidate then
            Some candidate
        else
            let parent = Directory.GetParent(dir)
            if isNull parent then None else findRoot parent.FullName

    match findRoot assemblyDir with
    | Some dir -> dir
    | None -> failwith "Could not find fixtures directory"

let private fixture name = Path.Combine(fixturesDir, $"{name}.fs")

let private expectWarning (fixtureName: string) (description: string) =
    testCase $"{fixtureName} — {description}"
    <| fun _ ->
        let tree = parseFixture (fixture fixtureName)
        let messages = analyzeFile tree
        let frank001 = messages |> List.filter (fun m -> m.Code = "FRANK001")
        Expect.isGreaterThanOrEqual frank001.Length 1 $"Expected FRANK001 warning in {fixtureName}"

let private expectNoWarning (fixtureName: string) (description: string) =
    testCase $"{fixtureName} — {description}"
    <| fun _ ->
        let tree = parseFixture (fixture fixtureName)
        let messages = analyzeFile tree
        let frank001 = messages |> List.filter (fun m -> m.Code = "FRANK001")
        Expect.isEmpty frank001 $"Expected no FRANK001 warning in {fixtureName}"

[<Tests>]
let tests =
    testList
        "DuplicateHandlerAnalyzer"
        [
          // Core duplicate detection
          expectWarning "DuplicateGet" "Duplicate GET detection"
          expectNoWarning "ValidSingleHandlers" "Valid single handlers (no warning)"
          expectNoWarning "MultipleResources" "Multiple resources (no warning)"

          // All HTTP methods
          expectWarning "DuplicatePost" "Duplicate POST detection"
          expectWarning "DuplicatePut" "Duplicate PUT detection"
          expectWarning "DuplicateDelete" "Duplicate DELETE detection"
          expectWarning "DuplicatePatch" "Duplicate PATCH detection"
          expectWarning "DuplicateHead" "Duplicate HEAD detection"
          expectWarning "DuplicateOptions" "Duplicate OPTIONS detection"
          expectWarning "DuplicateConnect" "Duplicate CONNECT detection"
          expectWarning "DuplicateTrace" "Duplicate TRACE detection"
          expectNoWarning "AllMethodsOnce" "All methods once (no warning)"

          // Datastar compatibility
          expectWarning "DatastarConflict" "Datastar + GET conflict"
          expectWarning "DatastarWithPost" "Datastar POST + POST conflict"
          expectNoWarning "DatastarNoConflict" "Datastar + POST (no conflict)" ]
