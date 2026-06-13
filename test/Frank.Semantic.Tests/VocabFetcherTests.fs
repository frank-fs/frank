module Frank.Semantic.Tests.VocabFetcherTests

open System
open System.IO
open System.Security.Cryptography
open System.Text
open Expecto
open FsCheck
open Frank.Semantic

// ── Fixture bytes ─────────────────────────────────────────────────────────────

let private turtleBytes =
    """
@prefix schema: <https://schema.org/> .
schema:Order a schema:Thing .
"""
    |> Encoding.UTF8.GetBytes

let private jsonLdBytes =
    """
{
  "@context": {"schema": "https://schema.org/"},
  "@graph": [{"@id": "schema:Order", "@type": "schema:Thing"}]
}
"""
    |> Encoding.UTF8.GetBytes

let private rdfXmlBytes =
    """<?xml version="1.0"?>
<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
         xmlns:schema="https://schema.org/">
  <schema:Thing rdf:about="https://schema.org/Order"/>
</rdf:RDF>
"""
    |> Encoding.UTF8.GetBytes

let private stubFetch (body: byte[]) (contentType: string option) : VocabFetcher.Fetch =
    fun _uri ->
        async {
            return
                Ok
                    {| ContentType = contentType
                       Body = body |}
        }

let private failFetch (reason: string) : VocabFetcher.Fetch =
    fun _uri -> async { return Error reason }

// ── Format detection ──────────────────────────────────────────────────────────

[<Tests>]
let formatDetectionTests =
    testList
        "VocabFetcher.detectFormat"
        [ test "application/ld+json → JsonLd" {
              let uri = Uri "https://schema.org/schema.jsonld"
              Expect.equal (VocabFetcher.detectFormat (Some "application/ld+json") uri) VocabFetcher.JsonLd "JsonLd"
          }

          test "application/rdf+xml → RdfXml" {
              let uri = Uri "https://schema.org/schema.rdf"
              Expect.equal (VocabFetcher.detectFormat (Some "application/rdf+xml") uri) VocabFetcher.RdfXml "RdfXml"
          }

          test "text/turtle → Turtle" {
              let uri = Uri "https://schema.org/schema.ttl"
              Expect.equal (VocabFetcher.detectFormat (Some "text/turtle") uri) VocabFetcher.Turtle "Turtle"
          }

          test "no content-type, .jsonld extension → JsonLd" {
              let uri = Uri "https://schema.org/schema.jsonld"
              Expect.equal (VocabFetcher.detectFormat None uri) VocabFetcher.JsonLd "JsonLd by ext"
          }

          test "no content-type, .rdf extension → RdfXml" {
              let uri = Uri "https://schema.org/schema.rdf"
              Expect.equal (VocabFetcher.detectFormat None uri) VocabFetcher.RdfXml "RdfXml by ext"
          }

          test "no content-type, .ttl extension → Turtle" {
              let uri = Uri "https://schema.org/schema.ttl"
              Expect.equal (VocabFetcher.detectFormat None uri) VocabFetcher.Turtle "Turtle by ext"
          }

          test "no content-type, .owl extension → RdfXml" {
              let uri = Uri "https://schema.org/schema.owl"
              Expect.equal (VocabFetcher.detectFormat None uri) VocabFetcher.RdfXml "RdfXml by .owl ext"
          }

          test "content-type takes precedence over extension" {
              let uri = Uri "https://schema.org/schema.ttl"

              Expect.equal
                  (VocabFetcher.detectFormat (Some "application/ld+json") uri)
                  VocabFetcher.JsonLd
                  "content-type wins"
          } ]

// ── sha256Hex ─────────────────────────────────────────────────────────────────

[<Tests>]
let sha256Tests =
    testList
        "VocabFetcher.sha256Hex"
        [ test "deterministic: same bytes → same hash" {
              let h1 = VocabFetcher.sha256Hex turtleBytes
              let h2 = VocabFetcher.sha256Hex turtleBytes
              Expect.equal h1 h2 "hashes must match"
          }

          test "different bytes → different hash" {
              let h1 = VocabFetcher.sha256Hex turtleBytes
              let h2 = VocabFetcher.sha256Hex jsonLdBytes
              Expect.notEqual h1 h2 "hashes must differ"
          }

          test "hash is lowercase hex, 64 chars" {
              let h = VocabFetcher.sha256Hex turtleBytes
              Expect.equal h.Length 64 "length"

              Expect.isTrue
                  (h |> Seq.forall (fun c -> (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f')))
                  "lowercase hex"
          }

          testProperty "FsCheck: sha256Hex is deterministic for any bytes" (fun (NonEmptyArray bytes) ->
              VocabFetcher.sha256Hex bytes = VocabFetcher.sha256Hex bytes)

          testProperty
              "FsCheck: distinct byte arrays produce distinct hashes (modulo collision)"
              (fun (NonEmptyArray b1) (NonEmptyArray b2) ->
                  let different = b1 <> b2

                  not different
                  || VocabFetcher.sha256Hex b1 <> VocabFetcher.sha256Hex b2
                  || b1 = b2) ]

// ── cacheFileName ─────────────────────────────────────────────────────────────

[<Tests>]
let cacheFileNameTests =
    testList
        "VocabFetcher.cacheFileName"
        [ test "JsonLd → .jsonld extension" {
              let name = VocabFetcher.cacheFileName "schema" "abc123" VocabFetcher.JsonLd
              Expect.equal name "schema.abc123.jsonld" "jsonld ext"
          }

          test "RdfXml → .rdf extension" {
              let name = VocabFetcher.cacheFileName "schema" "abc123" VocabFetcher.RdfXml
              Expect.equal name "schema.abc123.rdf" "rdf ext"
          }

          test "Turtle → .ttl extension" {
              let name = VocabFetcher.cacheFileName "schema" "abc123" VocabFetcher.Turtle
              Expect.equal name "schema.abc123.ttl" "ttl ext"
          } ]

// ── parseGraph ────────────────────────────────────────────────────────────────

[<Tests>]
let parseGraphTests =
    testList
        "VocabFetcher.parseGraph — AT3 mechanism"
        [ test "parses Turtle bytes to non-empty IGraph" {
              let result = VocabFetcher.parseGraph VocabFetcher.Turtle turtleBytes

              match result with
              | Error e -> failtest $"Expected Ok, got Error: {e}"
              | Ok graph -> Expect.isGreaterThan graph.Triples.Count 0 "graph must have triples"
          }

          test "parses JSON-LD bytes to non-empty IGraph" {
              let result = VocabFetcher.parseGraph VocabFetcher.JsonLd jsonLdBytes

              match result with
              | Error e -> failtest $"Expected Ok, got Error: {e}"
              | Ok graph -> Expect.isGreaterThan graph.Triples.Count 0 "graph must have triples"
          }

          test "parses RDF/XML bytes to non-empty IGraph" {
              let result = VocabFetcher.parseGraph VocabFetcher.RdfXml rdfXmlBytes

              match result with
              | Error e -> failtest $"Expected Ok, got Error: {e}"
              | Ok graph -> Expect.isGreaterThan graph.Triples.Count 0 "graph must have triples"
          }

          test "returns Error for malformed bytes" {
              let bad = Encoding.UTF8.GetBytes "this is not valid RDF"
              let result = VocabFetcher.parseGraph VocabFetcher.Turtle bad
              Expect.isError result "malformed input must return Error"
          } ]

// ── fetchAndCache ─────────────────────────────────────────────────────────────

[<Tests>]
let fetchAndCacheTests =
    testList
        "VocabFetcher.fetchAndCache — AT1/AT2 mechanisms"
        [ testAsync "AT1 mechanism: first fetch writes <name>.<hash>.ttl to cache dir" {
              let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(dir) |> ignore

              try
                  let fetch = stubFetch turtleBytes (Some "text/turtle")
                  let uri = Uri "https://example.com/vocab.ttl"
                  let! result = VocabFetcher.fetchAndCache fetch dir "myvocab" uri

                  match result with
                  | Error e -> failtest $"Expected Ok, got Error: {e}"
                  | Ok info ->
                      let expectedFile = Path.Combine(dir, $"myvocab.{info.Hash}.ttl")
                      Expect.isTrue (File.Exists expectedFile) "cache file must exist"

                      let fileBytes = File.ReadAllBytes expectedFile
                      let fileHash = VocabFetcher.sha256Hex fileBytes
                      Expect.equal fileHash info.Hash "file SHA-256 must match returned hash"
              finally
                  Directory.Delete(dir, true)
          }

          testAsync "AT2 mechanism: second call with cache present does not invoke fetch" {
              let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(dir) |> ignore

              try
                  let mutable fetchCount = 0

                  let countingFetch: VocabFetcher.Fetch =
                      fun _uri ->
                          async {
                              fetchCount <- fetchCount + 1

                              return
                                  Ok
                                      {| ContentType = Some "text/turtle"
                                         Body = turtleBytes |}
                          }

                  let uri = Uri "https://example.com/vocab.ttl"
                  let! _ = VocabFetcher.fetchAndCache countingFetch dir "myvocab" uri
                  let! _ = VocabFetcher.fetchAndCache countingFetch dir "myvocab" uri

                  Expect.equal fetchCount 1 "fetch must be called exactly once"
              finally
                  Directory.Delete(dir, true)
          }

          testAsync "AT5 mechanism: fetch failure returns Error, cache dir untouched" {
              let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString())
              Directory.CreateDirectory(dir) |> ignore

              try
                  let fetch = failFetch "connection refused"
                  let uri = Uri "https://nonexistent.example/"
                  let! result = VocabFetcher.fetchAndCache fetch dir "broken" uri

                  match result with
                  | Ok _ -> failtest "Expected Error"
                  | Error msg ->
                      Expect.stringContains msg "connection refused" "reason propagated"
                      let files = Directory.GetFiles(dir)
                      Expect.isEmpty files "no cache files written on failure"
              finally
                  Directory.Delete(dir, true)
          } ]

// ── detectDrift ───────────────────────────────────────────────────────────────

[<Tests>]
let detectDriftTests =
    testList
        "VocabFetcher.detectDrift — AT4 mechanism"
        [ test "same hash → NoDrift" {
              let result = VocabFetcher.detectDrift "abc123" "abc123"
              Expect.equal result VocabFetcher.NoDrift "identical hashes"
          }

          test "different hash → Drift(old, new)" {
              let result = VocabFetcher.detectDrift "old_hash" "new_hash"

              match result with
              | VocabFetcher.Drift(old, current) ->
                  Expect.equal old "old_hash" "old hash preserved"
                  Expect.equal current "new_hash" "current hash preserved"
              | VocabFetcher.NoDrift -> failtest "Expected Drift"
          }

          test "detectDrift is pure: no side effects (no mapping mutation)" {
              let r1 = VocabFetcher.detectDrift "a" "b"
              let r2 = VocabFetcher.detectDrift "a" "b"
              Expect.equal r1 r2 "same inputs always produce same DriftResult"
          }

          testProperty "FsCheck: equal hashes never produce Drift" (fun (NonWhiteSpaceString h) ->
              match VocabFetcher.detectDrift h h with
              | VocabFetcher.NoDrift -> true
              | VocabFetcher.Drift _ -> false)

          testProperty
              "FsCheck: distinct hashes always produce Drift"
              (fun (NonWhiteSpaceString h1) (NonWhiteSpaceString h2) ->
                  if h1 = h2 then
                      true
                  else
                      match VocabFetcher.detectDrift h1 h2 with
                      | VocabFetcher.Drift _ -> true
                      | VocabFetcher.NoDrift -> false) ]
