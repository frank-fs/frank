module Frank.Analyzers.Tests.VocabAnalyzerV2Tests

open System
open System.Diagnostics
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
    let assemblyDir = AppContext.BaseDirectory

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

// Fixed clock for deterministic SLA tests
let private fixedNow = DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero)

// Lock construction helpers
let private makeValidatedEntry (uri: string) (fetchedDaysAgo: float) (owned: bool) : VocabularyEntry =
    { v1Empty with
        Uri = uri
        FetchedAt = fixedNow.AddDays(-fetchedDaysAgo)
        Hash = "sha256:test"
        Validated =
            { IsValidated = true
              Reason = None
              LastChecked = Some fixedNow }
        Owned = owned
        Terms = None }

let private makeStaleEntry (uri: string) (fetchedDaysAgo: float) (owned: bool) : VocabularyEntry =
    { v1Empty with
        Uri = uri
        FetchedAt = fixedNow.AddDays(-fetchedDaysAgo)
        Hash = "sha256:test"
        Validated =
            { IsValidated = true
              Reason = None
              LastChecked = Some fixedNow }
        Owned = owned
        Terms = None }

let private emptyLock: LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = Map.empty
      DeclaredPrefixes = Map.empty
      Mappings = [] }

let private stampedLock (lf: LockFile) : LockFile = withIntegrity lf

let private filterCode (code: string) (msgs: FSharp.Analyzers.SDK.Message list) =
    msgs |> List.filter (fun m -> m.Code = code)

// ── Phase 1: AT-reasoning-home ───────────────────────────────────────────────

[<Tests>]
let reasoningHomeTests =
    testList
        "AT-reasoning-home"
        [ testCase "VocabState type assembly is Frank.Semantic.Core"
          <| fun _ ->
              let asm = typeof<VocabState>.Assembly.GetName().Name
              Expect.equal asm "Frank.Semantic.Core" "VocabState must come from Frank.Semantic.Core"

          testCase "classifyReferencedVocab is from VocabClassifier in Core"
          <| fun _ ->
              // Accessing this function compiles only if Core is referenced, not just Analyzers SDK
              let _: LockFile -> DateTimeOffset -> string list -> VocabState list =
                  classifyReferencedVocab

              Expect.isTrue true "classifyReferencedVocab is accessible from Core"

          testCase "verifyIntegrity is from Core"
          <| fun _ ->
              let _: LockFile -> Result<unit, string> = verifyIntegrity
              Expect.isTrue true "verifyIntegrity is accessible from Core"

          testCase "SlaPolicy.defaultPolicy unowned threshold is 30 days"
          <| fun _ -> Expect.equal SlaPolicy.defaultPolicy.UnownedMaxAgeDays 30 "unowned SLA is 30 days"

          testCase "SlaPolicy.defaultPolicy owned threshold is 90 days"
          <| fun _ -> Expect.equal SlaPolicy.defaultPolicy.OwnedReachabilityDays 90 "owned SLA is 90 days" ]

// ── Phase 2: classifyReferencedVocab IRI-identity (AT7) ──────────────────────

[<Tests>]
let iriIdentityTests =
    testList
        "AT7 IRI-identity: classifyReferencedVocab"
        [ testCase "AT7: same IRI under different prefix key → Confirmed (no warn)"
          <| fun _ ->
              // sdo prefix → https://schema.org/ is stored in Vocabularies under key "schema"
              let entry = makeValidatedEntry "https://schema.org/" 5.0 false

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "sdo", "https://schema.org/" ] }

              let states = classifyReferencedVocab lf fixedNow [ "sdo" ]
              Expect.equal states [ VocabState.Confirmed ] "AT7: sdo → same IRI as schema → Confirmed"

          testCase "AT6: foreign authority not in Vocabularies → Undereferenceable (authority-aware)"
          <| fun _ ->
              let lf =
                  stampedLock
                      { emptyLock with
                          DeclaredPrefixes = Map.ofList [ "ext", "https://foreign.org/vocab" ] }

              let states = classifyReferencedVocab lf fixedNow [ "ext" ]
              Expect.equal states [ VocabState.Undereferenceable ] "AT6: foreign authority → Undereferenceable"

          testCase "prefix not in DeclaredPrefixes → Undereferenceable"
          <| fun _ ->
              let states = classifyReferencedVocab emptyLock fixedNow [ "orphan" ]
              Expect.equal states [ VocabState.Undereferenceable ] "orphan prefix → Undereferenceable" ]

// ── Phase 3: AstExtractors ───────────────────────────────────────────────────

[<Tests>]
let astExtractorTests =
    testList
        "AstExtractors"
        [ testCase "extractRoutes: VocabWithRoute has /tictactoe"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabWithRoute")
              let routes = extractRoutes tree
              Expect.contains routes "/tictactoe" "Should extract /tictactoe route"

          testCase "extractRoutes: VocabNoRoute has no routes"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabNoRoute")
              let routes = extractRoutes tree
              Expect.isEmpty routes "VocabNoRoute should have no routes"

          testCase "extractRoutes: VocabForeignWithLocalRoute has /vocab"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabForeignWithLocalRoute")
              let routes = extractRoutes tree
              Expect.contains routes "/vocab" "Should extract /vocab route"

          testCase "AT8 matcher: trailing slash /games/ covers /games nsPath"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabTrailingSlashRoute")
              let routes = extractRoutes tree
              Expect.contains routes "/games/" "Should extract /games/ route"
              // Route /games/ should cover namespace path /games (trailing slash stripped)
              Expect.isTrue (routeCoversNsPath routes "/games") "Route /games/ covers /games nsPath"

          testCase "AT8 matcher: /Games (case) covers /games nsPath"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabCaseRoute")
              let routes = extractRoutes tree
              Expect.contains routes "/Games" "Should extract /Games route"
              // Route /Games (case) should cover /games (case-insensitive)
              Expect.isTrue (routeCoversNsPath routes "/games") "Route /Games covers /games nsPath (case-insensitive)"

          testCase "AT-contract: /foo covers /foo/bar, NOT /foobar"
          <| fun _ ->
              Expect.isTrue (routeCoversNsPath [ "/foo" ] "/foo/bar") "Route /foo covers /foo/bar"
              Expect.isFalse (routeCoversNsPath [ "/foo" ] "/foobar") "Route /foo does NOT cover /foobar"

          testCase "AT-bounded: deeply nested modules do not stack-overflow"
          <| fun _ ->
              // Construct a syntactically deeply nested F# source and parse it
              let lines =
                  List.init 220 (fun i -> String.replicate (i * 2) " " + $"module L{i} =")
                  @ [ String.replicate (220 * 2) " " + "let x = 1" ]

              let src = String.concat "\n" lines
              let checker = FSharpChecker.Create()
              let sourceText = SourceText.ofString src

              let options =
                  { FSharpParsingOptions.Default with
                      SourceFiles = [| "deep.fs" |] }

              let result =
                  checker.ParseFile("deep.fs", sourceText, options) |> Async.RunSynchronously
              // May have parse errors due to indentation — the point is no StackOverflowException
              let routes = extractRoutes result.ParseTree
              Expect.isEmpty routes "deeply nested: no routes expected, no crash"

          testCase "extractReferencedTerms: VocabTermUsage has schema:Game and schema:Person"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabTermUsage")
              let terms = extractReferencedTerms tree
              Expect.contains terms ("schema", "Game") "Should extract schema:Game CURIE"
              Expect.contains terms ("schema", "Person") "Should extract schema:Person CURIE" ]

// ── Phase 4: analyzeWithLock (thin adapter) ──────────────────────────────────

[<Tests>]
let analyzerV2Tests =
    testList
        "analyzeWithLock v2 (thin adapter)"
        [ testCase "AT-integrity (absent): None lock → no diagnostics"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabNoRoute")
              let msgs = analyzeWithLock None fixedNow tree
              Expect.isEmpty msgs "Absent lock → no diagnostics at all"

          testCase "AT-integrity (tampered): Error lock → FRANK003 regenerate diagnostic"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabNoRoute")

              let msgs =
                  analyzeWithLock (Some(Error "lock appears hand-edited; regenerate")) fixedNow tree

              let frank003 = filterCode "FRANK003" msgs
              Expect.isGreaterThanOrEqual frank003.Length 1 "Expected FRANK003 for tampered lock"

              Expect.isTrue
                  (frank003 |> List.forall (fun m -> m.Message.Contains "regenerate"))
                  "FRANK003 message mentions regenerate"

              Expect.isEmpty (filterCode "FRANK002" msgs) "Tampered lock must NOT emit FRANK002"

          testCase "AT-integrity (tampered): tampered lock is distinct from FRANK002"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabNoRoute")
              let msgs = analyzeWithLock (Some(Error "tampered")) fixedNow tree
              let codes = msgs |> List.map (fun m -> m.Code) |> Set.ofList
              Expect.isFalse (codes.Contains "FRANK002") "Tampered lock must NOT produce FRANK002"
              Expect.isTrue (codes.Contains "FRANK003") "Tampered lock must produce FRANK003"

          testCase "AT6 authority: foreign-authority vocab not validated + local route → FRANK002 still fires"
          <| fun _ ->
              // foreign IRI whose AbsPath matches /vocab, but authority is foreign.
              // File references ext:Thing so the prefix is in-scope for analysis.
              let lf =
                  stampedLock
                      { emptyLock with
                          DeclaredPrefixes = Map.ofList [ "ext", "https://foreign.org/vocab" ] }

              let tree = parseFixture (fixture "VocabForeignWithLocalRouteAndExtRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let frank002 = filterCode "FRANK002" msgs

              Expect.isGreaterThanOrEqual
                  frank002.Length
                  1
                  "AT6: foreign authority → FRANK002 must fire even with matching route path"

          testCase "AT-authority-fixture: vocab host differs, shares path prefix → FRANK002 fires"
          <| fun _ ->
              // different host, path /tictactoe.
              // File references ttt:Thing so prefix is in-scope.
              let lf =
                  stampedLock
                      { emptyLock with
                          DeclaredPrefixes = Map.ofList [ "ttt", "https://external.org/tictactoe#" ] }

              let tree = parseFixture (fixture "VocabWithRouteAndTttRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let frank002 = filterCode "FRANK002" msgs
              Expect.isGreaterThanOrEqual frank002.Length 1 "AT-authority-fixture: different host → FRANK002 fires"

          testCase "AT7 IRI-identity: same IRI under different prefix + IRI validated → no FRANK002"
          <| fun _ ->
              // VocabSdoRef references sdo:Game and sdo:Person so sdo is in-scope.
              // sdo → https://schema.org/ in DeclaredPrefixes; schema entry (Confirmed) in
              // Vocabularies with same URI → IRI-identity lookup returns Confirmed → no FRANK002.
              let entry = makeValidatedEntry "https://schema.org/" 5.0 false

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "sdo", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabSdoRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let frank002 = filterCode "FRANK002" msgs
              Expect.isEmpty frank002 "AT7: sdo references schema.org IRI via IRI-identity → Confirmed → no FRANK002"

          testCase "AT-route-hint: route covers namespace path but NOT confirmed → FRANK002 still fires"
          <| fun _ ->
              // ttt prefix declared, route /tictactoe exists, but NOT in Vocabularies.
              // File references ttt:Thing so prefix is in-scope for analysis.
              let lf =
                  stampedLock
                      { emptyLock with
                          DeclaredPrefixes = Map.ofList [ "ttt", "https://example.org/tictactoe#" ] }

              let tree = parseFixture (fixture "VocabWithRouteAndTttRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let frank002 = filterCode "FRANK002" msgs

              Expect.isGreaterThanOrEqual
                  frank002.Length
                  1
                  "AT-route-hint: route is hint only; FRANK002 must still fire"

          testCase "AT-route-hint: route match emits separate INFO note"
          <| fun _ ->
              let lf =
                  stampedLock
                      { emptyLock with
                          DeclaredPrefixes = Map.ofList [ "ttt", "https://example.org/tictactoe#" ] }

              let tree = parseFixture (fixture "VocabWithRouteAndTttRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree

              let infoNotes =
                  msgs |> List.filter (fun m -> m.Severity = FSharp.Analyzers.SDK.Severity.Info)

              Expect.isGreaterThanOrEqual infoNotes.Length 1 "Route match → separate INFO note emitted"

          testCase "AT-stale: FetchedAt > SLA → distinct INFO (not Warning)"
          <| fun _ ->
              // unowned, fetched 31 days ago → stale.
              // VocabTermUsage references schema:Game and schema:Person, so schema is in-scope.
              let entry = makeStaleEntry "https://schema.org/" 31.0 false

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabTermUsage")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let stale = filterCode "FRANK004" msgs
              Expect.isGreaterThanOrEqual stale.Length 1 "AT-stale: stale vocab → FRANK004"

              Expect.isTrue
                  (stale |> List.forall (fun m -> m.Severity = FSharp.Analyzers.SDK.Severity.Info))
                  "Stale diagnostic is Info, never Warning/Error"

              Expect.isTrue
                  (stale |> List.forall (fun m -> m.Message.Contains "scheduled refresh"))
                  "Stale message says 'scheduled refresh'"

          testCase "AT-stale: identical lock + identical now → identical output (deterministic)"
          <| fun _ ->
              let entry = makeStaleEntry "https://schema.org/" 35.0 false

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabTermUsage")
              let msgs1 = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let msgs2 = analyzeWithLock (Some(Ok lf)) fixedNow tree

              Expect.equal
                  (msgs1 |> List.map (fun m -> m.Code))
                  (msgs2 |> List.map (fun m -> m.Code))
                  "Same inputs → same diagnostic codes"

          testCase "AT-term: referenced term absent from confirmed Terms → FRANK006 warn"
          <| fun _ ->
              // Terms stored as bare local names (as produced by RdfConneg.termsInNamespace).
              let entry =
                  { makeValidatedEntry "https://schema.org/" 5.0 false with
                      Terms = Some(Set.ofList [ "Person"; "Event" ]) }

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabTermUsage")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              // schema:Game is referenced but "Game" not in {"Person","Event"}
              let termWarns = filterCode "FRANK006" msgs
              Expect.isGreaterThanOrEqual termWarns.Length 1 "AT-term: schema:Game absent from Terms → FRANK006"

          testCase "AT-term-present: term IS in confirmed Terms (bare names) → no FRANK006"
          <| fun _ ->
              // Terms are bare local names as RdfConneg.termsInNamespace produces.
              // OLD code: terms.Contains "schema:Person" against {"Person","Event"} → false (false FRANK006).
              // FIXED code: terms.Contains "Person" → true → no FRANK006.
              let entry =
                  { makeValidatedEntry "https://schema.org/" 5.0 false with
                      Terms = Some(Set.ofList [ "Person"; "Event" ]) }

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabTermUsage")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let termWarns = filterCode "FRANK006" msgs

              Expect.isFalse
                  (termWarns |> List.exists (fun m -> m.Message.Contains "schema:Person"))
                  "Present term schema:Person must NOT fire FRANK006"

          testCase "AT-term: Terms = None → suppress term check"
          <| fun _ ->
              // Terms = None means unknown — suppress
              let entry = makeValidatedEntry "https://schema.org/" 5.0 false
              // entry.Terms = None already (set by makeValidatedEntry)
              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabTermUsage")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let termWarns = filterCode "FRANK006" msgs
              Expect.isEmpty termWarns "AT-term: Terms=None → suppress term check, no FRANK006"

          testCase "AT-term: Terms = empty → suppress term check"
          <| fun _ ->
              let entry =
                  { makeValidatedEntry "https://schema.org/" 5.0 false with
                      Terms = Some Set.empty }

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabTermUsage")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let termWarns = filterCode "FRANK006" msgs
              Expect.isEmpty termWarns "AT-term: Terms=empty → suppress term check, no FRANK006"

          testCase "Confirmed vocab (validated in lock) → no FRANK002"
          <| fun _ ->
              // VocabTermUsage references schema:Game and schema:Person so schema is in-scope.
              // schema entry is validated (Confirmed) → suppression is genuinely exercised.
              let entry = makeValidatedEntry "https://schema.org/" 5.0 false

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabTermUsage")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let frank002 = filterCode "FRANK002" msgs
              Expect.isEmpty frank002 "Confirmed vocab → no FRANK002 even when prefix is referenced"

          testCase "AT7-term-alias: term under aliased prefix resolves via IRI → no FRANK006"
          <| fun _ ->
              // sdo: maps to same IRI as schema: entry; Terms stored under "schema" key.
              // IRI-first lookup: sdo→DeclaredPrefixes→https://schema.org/→byUri→schema entry.
              // checkTermMembership: localName "Game" ∈ {"Game","Person"} → no FRANK006.
              let entry =
                  { makeValidatedEntry "https://schema.org/" 5.0 false with
                      Terms = Some(Set.ofList [ "Game"; "Person" ]) }

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "sdo", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabSdoRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree

              Expect.isEmpty
                  (filterCode "FRANK006" msgs)
                  "AT7-term-alias: known terms under aliased prefix must not fire FRANK006"

          testCase "FRANK003 severity is Error (trust failure, not routine warning)"
          <| fun _ ->
              let tree = parseFixture (fixture "VocabNoRoute")
              let msgs = analyzeWithLock (Some(Error "lock appears hand-edited")) fixedNow tree
              let frank003 = filterCode "FRANK003" msgs
              Expect.isGreaterThanOrEqual frank003.Length 1 "FRANK003 must fire for tampered lock"

              Expect.isTrue
                  (frank003
                   |> List.forall (fun m -> m.Severity = FSharp.Analyzers.SDK.Severity.Error))
                  "FRANK003 must be Severity.Error (trust failure, louder than routine FRANK002)"

          testCase "CLI path suppresses FRANK004 for stale vocab"
          <| fun _ ->
              let entry = makeStaleEntry "https://schema.org/" 31.0 false

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "schema", entry ]
                          DeclaredPrefixes = Map.ofList [ "schema", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabTermUsage")
              let msgs = analyzeWithLockCli (Some(Ok lf)) fixedNow tree
              Expect.isEmpty (filterCode "FRANK004" msgs) "CLI path must NOT emit FRANK004 for stale vocab"

          testCase "FRANK010: Owned-unvalidated vocab with no covering route → Info nudge (not silent)"
          <| fun _ ->
              // LocallyServedUnconfirmed + no route must emit a FRANK007 nudge, not [].
              let entry =
                  { v1Empty with
                      Uri = "https://schema.org/"
                      FetchedAt = fixedNow.AddDays(-5.0)
                      Hash = "sha256:test"
                      Validated =
                          { IsValidated = false
                            Reason = None
                            LastChecked = None }
                      Owned = true
                      Terms = None }

              // VocabSdoRef references sdo:Game/sdo:Person — no resource route in file.
              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.ofList [ "sdo", entry ]
                          DeclaredPrefixes = Map.ofList [ "sdo", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabSdoRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let frank007 = filterCode "FRANK007" msgs

              Expect.isGreaterThanOrEqual
                  frank007.Length
                  1
                  "FRANK010: owned-unvalidated with no route → FRANK007 nudge emitted"

              Expect.isTrue
                  (frank007
                   |> List.forall (fun m -> m.Severity = FSharp.Analyzers.SDK.Severity.Info))
                  "FRANK010 nudge must be Info severity"

              Expect.isEmpty
                  (filterCode "FRANK005" msgs)
                  "ownership nudge must use its own FRANK007 code, not the route-hint FRANK005 code"

          testCase
              "#419 AC3: declared-only prefix backed by the app's own Confirmed mapping identity (no Vocabularies entry, no base URI) -> FRANK007 nudge, not FRANK002 warning"
          <| fun _ ->
              // sdo declared, NEVER fetched (no Vocabularies entry), but the lock's own
              // Mappings identify one of the app's own types via a sdo:-prefixed CURIE — the
              // produced artifact itself is the ownership evidence, no base URI/flag/config
              // involved (#419 rework). Prior to #419 this fell into the None branch and was
              // mis-classified Undereferenceable regardless of this evidence.
              let ownedMapping: Mapping =
                  { FSharpType = "App.OwnedThing"
                    Iri = Some "sdo:Game"
                    Confidence = 1.0
                    Source = Convention
                    Status = MappingStatus.Confirmed
                    Alternates = []
                    Rt = None
                    Shape = MappingShape.Record [] }

              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.empty
                          DeclaredPrefixes = Map.ofList [ "sdo", "https://schema.org/" ]
                          Mappings = [ ownedMapping ] }

              // VocabSdoRef references sdo:Game/sdo:Person — no resource route in file.
              let tree = parseFixture (fixture "VocabSdoRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree

              Expect.isEmpty
                  (filterCode "FRANK002" msgs)
                  "#419 AC3: owned-but-unfetched must NOT produce the harsher FRANK002/makeUndereferenceable warning"

              let frank007 = filterCode "FRANK007" msgs

              Expect.isGreaterThanOrEqual
                  frank007.Length
                  1
                  "#419 AC3: owned-but-unfetched must produce the softer FRANK007/makeOwnershipNudge instead"

              Expect.isTrue
                  (frank007
                   |> List.forall (fun m -> m.Severity = FSharp.Analyzers.SDK.Severity.Info))
                  "#419 AC3: ownership nudge is Info, not Warning"

              Expect.isTrue
                  (frank007
                   |> List.exists (fun m -> m.Message.Contains "recorded as owned but not yet confirmed"))
                  "#419 AC3: message text is the ownership nudge, not the Undereferenceable warning"

              Expect.isEmpty
                  (filterCode "FRANK005" msgs)
                  "#419 AC3: ownership nudge must use its own FRANK007 code, distinct from FRANK005 route-hint"

          testCase
              "#419: declared-only prefix with NO Mappings evidence of ownership -> still FRANK002 (analyzer needs zero flags either way)"
          <| fun _ ->
              // sdo declared, never fetched, and never used to identify any of the app's own
              // resources -> no evidence of ownership -> must stay the harsher warning. This is
              // the control case proving the analyzer draws ownership from the artifact, not
              // from guessing.
              let lf =
                  stampedLock
                      { emptyLock with
                          Vocabularies = Map.empty
                          DeclaredPrefixes = Map.ofList [ "sdo", "https://schema.org/" ] }

              let tree = parseFixture (fixture "VocabSdoRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree

              Expect.isGreaterThanOrEqual
                  (filterCode "FRANK002" msgs).Length
                  1
                  "#419: no ownership evidence in the lock's own Mappings -> Undereferenceable/FRANK002, same as before #419"

          testCase "Diagnostic range is not Range.range0 (real file range)"
          <| fun _ ->
              // VocabWithRouteAndTttRef references ttt:Thing and ttt:Move so ttt is in-scope.
              // ttt not in Vocabularies → Undereferenceable → FRANK002 fires → range assertion runs.
              let lf =
                  stampedLock
                      { emptyLock with
                          DeclaredPrefixes = Map.ofList [ "ttt", "https://example.org/tictactoe#" ] }

              let tree = parseFixture (fixture "VocabWithRouteAndTttRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree
              let frank002 = filterCode "FRANK002" msgs

              Expect.isGreaterThanOrEqual
                  frank002.Length
                  1
                  "At least one FRANK002 must fire for the loop body to execute"

              for m in frank002 do
                  Expect.notEqual m.Range FSharp.Compiler.Text.Range.range0 "FRANK002 range must not be range0"
                  Expect.isFalse (String.IsNullOrEmpty m.Range.FileName) "FRANK002 range must have real file name" ]

// ── Phase 5: GAP 3 scope-pinning + AT-CI real enforcement ────────────────────

// Subprocess helper: run a dotnet command in cwd, return (exitCode, combined output).
// Bounded by a 120-second WaitForExit timeout.
let private runDotnet (cwd: string) (args: string) : int * string =
    let psi = ProcessStartInfo("dotnet", args)
    psi.WorkingDirectory <- cwd
    psi.UseShellExecute <- false
    psi.RedirectStandardOutput <- true
    psi.RedirectStandardError <- true
    psi.Environment.["DOTNET_SYSTEM_GLOBALIZATION_INVARIANT"] <- "1"
    use p = Process.Start psi
    let out = p.StandardOutput.ReadToEnd()
    let err = p.StandardError.ReadToEnd()
    p.WaitForExit(120_000) |> ignore
    p.ExitCode, out + err

let private findRepoRoot () : string =
    let rec find (dir: string) =
        if Directory.Exists(Path.Combine(dir, "src", "Frank.Analyzers")) then
            Some dir
        else
            let parent = Directory.GetParent dir
            if isNull parent then None else find parent.FullName

    find AppContext.BaseDirectory
    |> Option.defaultWith (fun () -> failwith "repo root not found")

// Always build Release net8.0 from current source — never trust a pre-existing bin.
// A stale Release dll (from a different commit) silently produces "No messages found"
// and masks real analyzer regressions as false-greens.
let private analyzerBinNet8 (repoRoot: string) : string =
    let proj =
        Path.Combine(repoRoot, "src", "Frank.Analyzers", "Frank.Analyzers.fsproj")

    let exitCode, output =
        runDotnet repoRoot $"build \"{proj}\" -f net8.0 -c Release -v q"

    if exitCode <> 0 then
        failwith $"Frank.Analyzers Release net8.0 build failed (exit {exitCode}):\n{output}"

    Path.Combine(repoRoot, "src", "Frank.Analyzers", "bin", "Release", "net8.0")

[<Tests>]
let scopePinningTests =
    testList
        "GAP3 scope-pinning: FRANK002 scoped to referenced prefixes"
        [ testCase "scope: file NOT referencing undereferenceable prefix → no FRANK002"
          <| fun _ ->
              // ext is undereferenceable in the lock, but VocabNoRoute never references ext:X
              let lf =
                  stampedLock
                      { emptyLock with
                          DeclaredPrefixes = Map.ofList [ "ext", "https://foreign.org/vocab" ] }

              let tree = parseFixture (fixture "VocabNoRoute")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree

              Expect.isEmpty
                  (filterCode "FRANK002" msgs)
                  "File with no ext: references must not produce FRANK002 for ext"

          testCase "scope: file DOES reference undereferenceable prefix → FRANK002"
          <| fun _ ->
              // ext is undereferenceable; VocabForeignWithLocalRouteAndExtRef references ext:Thing
              let lf =
                  stampedLock
                      { emptyLock with
                          DeclaredPrefixes = Map.ofList [ "ext", "https://foreign.org/vocab" ] }

              let tree = parseFixture (fixture "VocabForeignWithLocalRouteAndExtRef")
              let msgs = analyzeWithLock (Some(Ok lf)) fixedNow tree

              Expect.isGreaterThanOrEqual
                  (filterCode "FRANK002" msgs).Length
                  1
                  "File referencing ext:Thing must produce FRANK002" ]

[<Tests>]
let ciEnforcementTests =
    testList
        "AT-CI real enforcement: fsharp-analyzers loads analyzer and emits FRANK002"
        [ testCase "AT-CI: CLI loads >=1 analyzer and FRANK002 fires for undereferenceable fixture"
          <| fun _ ->
              let repoRoot = findRepoRoot ()
              let analyzerBin = analyzerBinNet8 repoRoot

              let fixture =
                  Path.Combine(
                      repoRoot,
                      "test",
                      "Frank.Analyzers.Fixture.Undereferenceable",
                      "Undereferenceable.fsproj"
                  )

              let _, output =
                  runDotnet
                      repoRoot
                      $"fsharp-analyzers --project \"{fixture}\" --analyzers-path \"{analyzerBin}\" --verbosity diagnostic"

              Expect.isFalse
                  (output.Contains "0 analyzers found"
                   || output.Contains "Could not load FSharp.Core")
                  $"Analyzer must load successfully. Output was:\n{output}"

              Expect.stringContains output "FRANK002" $"FRANK002 must be emitted. Output was:\n{output}"

          testCase "AT-CI: FrankCheckVocab MSBuild target is absent from .targets"
          <| fun _ ->
              let repoRoot = findRepoRoot ()

              let targetsPath =
                  Path.Combine(repoRoot, "src", "Frank.Cli.MSBuild", "build", "Frank.Cli.MSBuild.targets")

              if not (File.Exists targetsPath) then
                  failwith $"targets file not found at {targetsPath}"

              let content = File.ReadAllText targetsPath
              Expect.isFalse (content.Contains "FrankCheckVocab") "FrankCheckVocab target must be removed"

              Expect.isFalse
                  (content.Contains "CheckUndereferenceableVocabTask")
                  "CheckUndereferenceableVocabTask must be removed" ]
