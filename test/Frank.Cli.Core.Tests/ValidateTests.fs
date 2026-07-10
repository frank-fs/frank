module Frank.Cli.Core.Tests.ValidateTests

open System
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core.Validate
open Frank.Cli.Core.Tests.RefreshFixtures

let private fixedNow = DateTimeOffset(2026, 7, 9, 12, 0, 0, TimeSpan.Zero)

let private mkOwnedValidateEntry (uri: string) : VocabularyEntry =
    { v1Empty with
        Uri = uri
        FetchedAt = fixedNow.AddDays(-5.0)
        Hash = schemaBodyHash
        Owned = true }

let private mkLockV2WithOwned (prefix: string) (entry: VocabularyEntry) : LockFile =
    { SchemaVersion = 2
      Generated = fixedNow
      Integrity = None
      Vocabularies = Map.ofList [ prefix, entry ]
      DeclaredPrefixes = Map.empty
      Mappings = [] }

let private runValidate (fetch: ConnegFetch) (lf: LockFile) : ValidateReport * LockFile =
    validate fetch fixedNow lf |> Async.RunSynchronously

// ── A-C7: owned validate text/html → LyingIri (exit 2) ───────────────────────

[<Tests>]
let ac7ValidateTests =
    testList
        "A-C7 — owned validate: text/html response → LyingIri, exit 2"
        [ testCase "owned vocab returns text/html → LyingIri, Validated=false, exit 2"
          <| fun () ->
              let entry = mkOwnedValidateEntry "http://localhost:9971/vocab"
              let lock = mkLockV2WithOwned "vocab" entry

              let fetch =
                  stubConnegFetch (NonRdfContent {| MediaType = "text/html"; HttpStatus = 200 |})

              let (report, updatedLock) = runValidate fetch lock

              let hasLying =
                  report.Outcomes
                  |> List.exists (fun (_, o) ->
                      match o with
                      | LyingIri _ -> true
                      | _ -> false)

              Expect.isTrue hasLying "owned text/html → LyingIri (A-C7)"
              Expect.equal (validateExitCode report) 2 "exit 2 on owned text/html (A-C7)"
              let updatedEntry = updatedLock.Vocabularies.["vocab"]
              Expect.isFalse updatedEntry.Validated.IsValidated "Validated=false after LyingIri"

          testCase "owned vocab returns Turtle → Validated=true, exit 0"
          <| fun () ->
              let entry = mkOwnedValidateEntry "http://localhost:9972/vocab"
              let lock = mkLockV2WithOwned "vocab" entry
              let fetch = stubTurtleConnegFetch schemaBody
              let (report, updatedLock) = runValidate fetch lock

              let allValidated =
                  report.Outcomes
                  |> List.forall (fun (_, o) ->
                      match o with
                      | Validated -> true
                      | _ -> false)

              Expect.isTrue allValidated "owned Turtle → Validated"
              Expect.equal (validateExitCode report) 0 "exit 0 on successful validate" ]
