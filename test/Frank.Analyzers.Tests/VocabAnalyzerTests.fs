module Frank.Analyzers.Tests.VocabAnalyzerTests

open System
open System.IO
open FSharp.Compiler.CodeAnalysis
open FSharp.Compiler.Text
open Expecto
open Frank.Semantic.LockFile
open Frank.Semantic.VocabCheck
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

let private emptyLock =
    { SchemaVersion = 1
      Generated = DateTimeOffset.UtcNow
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
                FetchedAt = DateTimeOffset.UtcNow
                Hash = "testhash" })
        |> Map.ofList

    { emptyLock with
        DeclaredPrefixes = Map.ofList declaredPrefixes
        Vocabularies = vocabularies }

let private tttNsUri = "https://example.org/tictactoe#"
let private schemaNsUri = "https://schema.org/"

let private tttLockUnfetched = makeLock [ "ttt", tttNsUri ] []
let private tttLockFetched = makeLock [ "ttt", tttNsUri ] [ "ttt", tttNsUri ]

let private schemaLockFetched =
    makeLock [ "schema", schemaNsUri ] [ "schema", schemaNsUri ]

// ── checkUndereferenceableVocab pure-fn tests ─────────────────────────────────

[<Tests>]
let pureFnTests =
    testList
        "checkUndereferenceableVocab"
        [

          testCase "AT1: ttt not in Vocabularies + no routes → warns ttt"
          <| fun _ ->
              let result = checkUndereferenceableVocab tttLockUnfetched [] [ "ttt" ]
              Expect.equal result [ "ttt" ] "Expected ttt in result"

          testCase "AT2: route /tictactoe covers ttt namespace → no warning"
          <| fun _ ->
              let result = checkUndereferenceableVocab tttLockUnfetched [ "/tictactoe" ] [ "ttt" ]

              Expect.isEmpty result "Expected no warnings when route covers namespace"

          testCase "AT3: ttt in Vocabularies → no warning"
          <| fun _ ->
              let result = checkUndereferenceableVocab tttLockFetched [] [ "ttt" ]
              Expect.isEmpty result "Expected no warning when vocab is dereferenceable"

          testCase "AT5: schema.org in Vocabularies → no warning"
          <| fun _ ->
              let result = checkUndereferenceableVocab schemaLockFetched [] [ "schema" ]
              Expect.isEmpty result "Expected no warning for fetched schema.org"

          testCase "route /tic does NOT cover /tictactoe namespace"
          <| fun _ ->
              let result = checkUndereferenceableVocab tttLockUnfetched [ "/tic" ] [ "ttt" ]

              Expect.equal result [ "ttt" ] "Route /tic must not cover /tictactoe"

          testCase "route /tictactoe/game covers /tictactoe/game namespace"
          <| fun _ ->
              let subNsUri = "https://example.org/tictactoe/game#"
              let lock = makeLock [ "tttg", subNsUri ] []
              let result = checkUndereferenceableVocab lock [ "/tictactoe/game" ] [ "tttg" ]
              Expect.isEmpty result "Route /tictactoe/game covers /tictactoe/game"

          testCase "prefix without URI in lock → no warning (benefit of doubt)"
          <| fun _ ->
              let lock =
                  { emptyLock with
                      DeclaredPrefixes = Map.empty }

              let result = checkUndereferenceableVocab lock [] [ "orphan" ]
              Expect.isEmpty result "No URI → no warning (benefit of doubt)"

          testCase "empty referencedNs → empty result"
          <| fun _ ->
              let result = checkUndereferenceableVocab tttLockUnfetched [] []
              Expect.isEmpty result "Empty input → empty output" ]

// ── Analyzer fixture tests (FRANK002) ─────────────────────────────────────────

let private frank002 (msgs: FSharp.Analyzers.SDK.Message list) =
    msgs |> List.filter (fun m -> m.Code = "FRANK002")

let private expectFrank002 (fixtureName: string) (lock: LockFile) (description: string) =
    testCase $"{fixtureName} + lock → {description}"
    <| fun _ ->
        let tree = parseFixture (fixture fixtureName)
        let messages = analyzeWithLock (Some lock) tree
        Expect.isGreaterThanOrEqual (frank002 messages).Length 1 $"Expected FRANK002 in {fixtureName}"

let private expectNoFrank002 (fixtureName: string) (lock: LockFile) (description: string) =
    testCase $"{fixtureName} + lock → {description}"
    <| fun _ ->
        let tree = parseFixture (fixture fixtureName)
        let messages = analyzeWithLock (Some lock) tree
        Expect.isEmpty (frank002 messages) $"Expected no FRANK002 in {fixtureName}"

[<Tests>]
let analyzerTests =
    testList
        "UndereferenceableVocabAnalyzer"
        [

          // AT1: ttt not in lock + no route → FRANK002
          expectFrank002 "VocabNoRoute" tttLockUnfetched "AT1: warns when ttt not in lock and no route"

          // AT2: route /tictactoe in source → no FRANK002
          expectNoFrank002 "VocabWithRoute" tttLockUnfetched "AT2: no warning when route covers ttt namespace"

          // AT3: ttt in Vocabularies → no FRANK002
          expectNoFrank002 "VocabNoRoute" tttLockFetched "AT3: no warning when ttt is dereferenceable"

          // AT5: schema.org in Vocabularies → no FRANK002
          expectNoFrank002 "VocabNoRoute" schemaLockFetched "AT5: no warning for fetched schema.org"

          // No lock → no diagnostics
          testCase "No lock (None) → no FRANK002"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabNoRoute")
              let messages = analyzeWithLock None tree
              Expect.isEmpty (frank002 messages) "No lock → no diagnostics"

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
