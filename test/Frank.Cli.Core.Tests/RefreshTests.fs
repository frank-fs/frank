module Frank.Cli.Core.Tests.RefreshTests

open System
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabFetcher
open Frank.Cli.Core.Refresh

// ── Fixtures ──────────────────────────────────────────────────────────────────

let private schemaBody: byte[] =
    Text.Encoding.UTF8.GetBytes "{ \"@context\": \"https://schema.org/\" }"

let private schemaBodyHash: string = sha256Hex schemaBody

let private stubFetch (body: byte[]) : Fetch =
    fun (_: Uri) -> async { return Ok {| ContentType = None; Body = body |} }

let private errorFetch (reason: string) : Fetch =
    fun (_: Uri) -> async { return Error reason }

let private mkVocabEntry (hash: string) : VocabularyEntry =
    { Uri = "https://schema.org/"
      FetchedAt = DateTimeOffset.UnixEpoch
      Hash = hash }

let private mkLock (vocabs: Map<string, VocabularyEntry>) : LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.UnixEpoch
      Vocabularies = vocabs
      Mappings = [] }

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let refreshTests =
    testList
        "Refresh"
        [ testCase "AT4: hash drift detected — drifted entry returned with recorded and current hashes"
          <| fun () ->
              let lock = mkLock (Map.ofList [ "schema", mkVocabEntry "DEADBEEF" ])

              let fetch = stubFetch schemaBody

              let result = refresh fetch lock |> Async.RunSynchronously

              match result with
              | Error e -> failtest $"unexpected error: {e}"
              | Ok report ->
                  Expect.equal report.Checked 1 "one vocab checked"
                  Expect.equal report.Drifted.Length 1 "one drift entry"
                  let d = report.Drifted.[0]
                  Expect.equal d.Prefix "schema" "prefix"
                  Expect.equal d.Recorded "DEADBEEF" "recorded hash"
                  Expect.equal d.Current schemaBodyHash "current hash"

          testCase "no drift — drifted list empty, checked = 1"
          <| fun () ->
              let lock = mkLock (Map.ofList [ "schema", mkVocabEntry schemaBodyHash ])

              let fetch = stubFetch schemaBody

              let result = refresh fetch lock |> Async.RunSynchronously

              match result with
              | Error e -> failtest $"unexpected error: {e}"
              | Ok report ->
                  Expect.equal report.Checked 1 "one vocab checked"
                  Expect.equal report.Drifted [] "no drift"

          testCase "fetch error — refresh returns Error containing prefix and reason"
          <| fun () ->
              let lock = mkLock (Map.ofList [ "schema", mkVocabEntry "DEADBEEF" ])

              let fetch = errorFetch "boom"

              let result = refresh fetch lock |> Async.RunSynchronously

              match result with
              | Ok _ -> failtest "expected Error"
              | Error msg ->
                  Expect.stringContains msg "schema" "error contains prefix"
                  Expect.stringContains msg "boom" "error contains reason"

          testCase "empty vocabularies — Ok with Checked=0 and Drifted=[]"
          <| fun () ->
              let lock = mkLock Map.empty
              let fetch = errorFetch "should not be called"

              let result = refresh fetch lock |> Async.RunSynchronously

              match result with
              | Error e -> failtest $"unexpected error: {e}"
              | Ok report ->
                  Expect.equal report.Checked 0 "zero checked"
                  Expect.equal report.Drifted [] "no drift" ]
