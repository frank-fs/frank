module Frank.Analyzers.Tests.VocabAnalyzerTests

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabClassifier
open Frank.Analyzers.AstExtractors
open Frank.Analyzers.UndereferenceableVocabAnalyzer

// ── Helpers ──────────────────────────────────────────────────────────────────

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
            let parent = Directory.GetParent dir
            if isNull parent then None else findRoot parent.FullName

    match findRoot assemblyDir with
    | Some dir -> dir
    | None -> failwith "Could not find fixtures directory"

let private fixture name = Path.Combine(fixturesDir, $"{name}.fs")

let private fixedNow = DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero)

let private emptyLock : LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = Map.empty
      DeclaredPrefixes = Map.empty
      Mappings = [] }

let private makeLock (declaredPrefixes: (string * string) list) (fetchedVocabs: (string * string) list) =
    let vocabularies =
        fetchedVocabs
        |> List.map (fun (prefix, uri) ->
            prefix,
            { v1Empty with
                Uri = uri
                FetchedAt = fixedNow
                Hash = "testhash"
                Validated =
                    { IsValidated = true
                      Reason = None
                      LastChecked = Some fixedNow } })
        |> Map.ofList

    withIntegrity
        { emptyLock with
            DeclaredPrefixes = Map.ofList declaredPrefixes
            Vocabularies = vocabularies }

let private tttNsUri = "https://example.org/tictactoe#"
let private schemaNsUri = "https://schema.org/"

let private tttLockUnfetched = makeLock [ "ttt", tttNsUri ] []
let private tttLockFetched = makeLock [ "ttt", tttNsUri ] [ "ttt", tttNsUri ]
let private schemaLockFetched = makeLock [ "schema", schemaNsUri ] [ "schema", schemaNsUri ]

// ── classifyReferencedVocab pure-fn tests (replaces checkUndereferenceableVocab) ──

[<Tests>]
let pureFnTests =
    testList
        "classifyReferencedVocab (replacing checkUndereferenceableVocab)"
        [ testCase "AT1 equiv: ttt not in Vocabularies -> Undereferenceable"
          <| fun _ ->
              let states = classifyReferencedVocab tttLockUnfetched fixedNow [ "ttt" ]
              Expect.equal states [ VocabState.Undereferenceable ] "Expected Undereferenceable for unfetched ttt"

          testCase "AT3 equiv: ttt in Vocabularies (validated) -> Confirmed"
          <| fun _ ->
              let states = classifyReferencedVocab tttLockFetched fixedNow [ "ttt" ]
              Expect.equal states [ VocabState.Confirmed ] "Expected Confirmed when ttt is fetched and validated"

          testCase "AT5 equiv: schema.org in Vocabularies -> Confirmed"
          <| fun _ ->
              let states = classifyReferencedVocab schemaLockFetched fixedNow [ "schema" ]
              Expect.equal states [ VocabState.Confirmed ] "Expected Confirmed for fetched schema.org"

          testCase "empty referencedNs -> empty result"
          <| fun _ ->
              let states = classifyReferencedVocab tttLockUnfetched fixedNow []
              Expect.isEmpty states "Empty input -> empty output"

          testCase "multiple prefixes produce states in order"
          <| fun _ ->
              let lock =
                  makeLock [ "ttt", tttNsUri; "schema", schemaNsUri ] [ "schema", schemaNsUri ]

              let states = classifyReferencedVocab lock fixedNow [ "ttt"; "schema" ]

              Expect.equal
                  states
                  [ VocabState.Undereferenceable; VocabState.Confirmed ]
                  "ttt Undereferenceable, schema Confirmed" ]

// ── Analyzer fixture tests (FRANK002) ─────────────────────────────────────────

let private frank002 (msgs: FSharp.Analyzers.SDK.Message list) =
    msgs |> List.filter (fun m -> m.Code = "FRANK002")

let private expectFrank002 (fixtureName: string) (lock: LockFile) (description: string) =
    testCase $"{fixtureName} + lock -> {description}"
    <| fun _ ->
        let tree = parseFixture (fixture fixtureName)
        let messages = analyzeWithLock (Some(Ok lock)) fixedNow tree
        Expect.isGreaterThanOrEqual (frank002 messages).Length 1 $"Expected FRANK002 in {fixtureName}"

let private expectNoFrank002 (fixtureName: string) (lock: LockFile) (description: string) =
    testCase $"{fixtureName} + lock -> {description}"
    <| fun _ ->
        let tree = parseFixture (fixture fixtureName)
        let messages = analyzeWithLock (Some(Ok lock)) fixedNow tree
        Expect.isEmpty (frank002 messages) $"Expected no FRANK002 in {fixtureName}"

[<Tests>]
let analyzerTests =
    testList
        "UndereferenceableVocabAnalyzer"
        [
          // AT1: ttt not in lock, file references ttt:X -> FRANK002
          expectFrank002 "VocabWithRouteAndTttRef" tttLockUnfetched "AT1: warns when ttt not in lock and file references ttt prefix"

          // AT2: route does NOT suppress FRANK002; file must reference prefix for scoping
          testCase "AT2 new: route /tictactoe does NOT suppress FRANK002 (hint only)"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabWithRouteAndTttRef")
              let msgs = analyzeWithLock (Some(Ok tttLockUnfetched)) fixedNow tree
              Expect.isGreaterThanOrEqual (frank002 msgs).Length 1 "Route does not suppress FRANK002 -- route is hint only"

          // AT3: ttt in Vocabularies (validated) -> no FRANK002; file references ttt: so suppression is exercised
          expectNoFrank002 "VocabWithRouteAndTttRef" tttLockFetched "AT3: no warning when ttt is dereferenceable"

          // AT5: schema.org in Vocabularies -> no FRANK002; VocabTermUsage references schema: so suppression is exercised
          expectNoFrank002 "VocabTermUsage" schemaLockFetched "AT5: no warning for fetched schema.org"

          // No lock -> no diagnostics
          testCase "No lock (None) -> no FRANK002"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabNoRoute")
              let messages = analyzeWithLock None fixedNow tree
              Expect.isEmpty (frank002 messages) "No lock -> no diagnostics"

          // extractRoutes extracts /tictactoe from VocabWithRoute fixture
          testCase "extractRoutes: VocabWithRoute has /tictactoe"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabWithRoute")
              let routes = extractRoutes tree
              Expect.contains routes "/tictactoe" "Should extract /tictactoe route"

          // extractRoutes: VocabNoRoute has no routes
          testCase "extractRoutes: VocabNoRoute has no routes"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabNoRoute")
              let routes = extractRoutes tree
              Expect.isEmpty routes "VocabNoRoute should have no routes" ]
