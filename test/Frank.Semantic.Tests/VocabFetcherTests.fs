module Frank.Semantic.Tests.VocabFetcherTests

open System
open System.IO
open System.Security.Cryptography
open Expecto
open Frank.Semantic

// ── Helpers ─────────────────────────────────────────────────────────────────

let computeHash (bytes: byte[]) =
    let hash = SHA256.HashData(bytes)
    "sha256:" + Convert.ToHexString(hash).ToLowerInvariant()

let withTempDir (f: string -> unit) =
    let dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(dir) |> ignore

    try
        f dir
    finally
        if Directory.Exists dir then
            Directory.Delete(dir, true)

// A small but valid JSON-LD document
let sampleJsonLd =
    """{"@context":{"schema":"https://schema.org/"},"@type":"schema:Person","schema:name":"Alice"}"""

// A small but valid Turtle document
let sampleTurtle =
    "@prefix schema: <https://schema.org/> .\n<https://example.org/alice> a schema:Person ; schema:name \"Alice\" .\n"

// A small but valid RDF/XML document
let sampleRdfXml =
    """<?xml version="1.0"?>
<rdf:RDF xmlns:rdf="http://www.w3.org/1999/02/22-rdf-syntax-ns#"
         xmlns:schema="https://schema.org/">
  <rdf:Description rdf:about="https://example.org/alice">
    <rdf:type rdf:resource="https://schema.org/Person"/>
    <schema:name>Alice</schema:name>
  </rdf:Description>
</rdf:RDF>"""

// ── AT2: Cache hit avoids network ───────────────────────────────────────────

[<Tests>]
let at2 =
    testList
        "AT2: Subsequent fetch uses cache (no network)"
        [ test "pre-written cache file is used without re-fetching" {
              withTempDir (fun cacheDir ->
                  let content = System.Text.Encoding.UTF8.GetBytes(sampleJsonLd)
                  let hash = computeHash content
                  let hashHex = hash.Substring(7) // strip "sha256:"
                  let cachedFile = Path.Combine(cacheDir, $"schema.{hashHex}.jsonld")
                  File.WriteAllBytes(cachedFile, content)

                  // fetchOrLoad should use the cached file, not the network
                  let result =
                      VocabFetcher.fetchOrLoad cacheDir "schema" "https://nonexistent.example.invalid/schema"

                  Expect.equal result.Hash hash "hash should match cached file"
                  Expect.equal result.Prefix "schema" "prefix should match"
                  Expect.isFalse result.Graph.IsEmpty "graph should have triples")
          } ]

// ── AT4: Drift detection without auto-mutation ───────────────────────────────

[<Tests>]
let at4 =
    testList
        "AT4: Drift detection without auto-mutation"
        [ test "detectDrift returns drifted prefix when hash differs" {
              withTempDir (fun cacheDir ->
                  let content = System.Text.Encoding.UTF8.GetBytes(sampleJsonLd)
                  let realHash = computeHash content
                  let realHashHex = realHash.Substring(7)
                  let cachedFile = Path.Combine(cacheDir, $"schema.{realHashHex}.jsonld")
                  File.WriteAllBytes(cachedFile, content)

                  let staleHash =
                      "sha256:0000000000000000000000000000000000000000000000000000000000000000"

                  let lockFile: LockFile =
                      { SchemaVersion = 1
                        Generated = DateTimeOffset.UtcNow
                        Vocabularies =
                          Map.ofList
                              [ "schema",
                                { Uri = "https://schema.org/"
                                  FetchedAt = Some "2026-01-01T00:00:00Z"
                                  Hash = Some staleHash } ]
                        Mappings = [] }

                  let drifted = VocabFetcher.detectDrift cacheDir lockFile

                  Expect.hasLength drifted 1 "should have one drifted vocabulary"
                  let prefix, oldHash, newHash = drifted.[0]
                  Expect.equal prefix "schema" "drifted prefix should be 'schema'"
                  Expect.equal oldHash staleHash "old hash should be the stale hash"
                  Expect.equal newHash realHash "new hash should be the real hash")
          }

          test "detectDrift does not mutate the lock file" {
              withTempDir (fun cacheDir ->
                  let content = System.Text.Encoding.UTF8.GetBytes(sampleJsonLd)
                  let realHash = computeHash content
                  let realHashHex = realHash.Substring(7)
                  let cachedFile = Path.Combine(cacheDir, $"schema.{realHashHex}.jsonld")
                  File.WriteAllBytes(cachedFile, content)

                  let staleHash =
                      "sha256:0000000000000000000000000000000000000000000000000000000000000000"

                  let lockFile: LockFile =
                      { SchemaVersion = 1
                        Generated = DateTimeOffset.UtcNow
                        Vocabularies =
                          Map.ofList
                              [ "schema",
                                { Uri = "https://schema.org/"
                                  FetchedAt = Some "2026-01-01T00:00:00Z"
                                  Hash = Some staleHash } ]
                        Mappings = [] }

                  let _ = VocabFetcher.detectDrift cacheDir lockFile

                  let schemaEntry = lockFile.Vocabularies |> Map.find "schema"
                  Expect.equal schemaEntry.Hash (Some staleHash) "lock file must not be mutated by detectDrift")
          }

          test "detectDrift returns empty list when hashes match" {
              withTempDir (fun cacheDir ->
                  let content = System.Text.Encoding.UTF8.GetBytes(sampleJsonLd)
                  let realHash = computeHash content
                  let realHashHex = realHash.Substring(7)
                  let cachedFile = Path.Combine(cacheDir, $"schema.{realHashHex}.jsonld")
                  File.WriteAllBytes(cachedFile, content)

                  let lockFile: LockFile =
                      { SchemaVersion = 1
                        Generated = DateTimeOffset.UtcNow
                        Vocabularies =
                          Map.ofList
                              [ "schema",
                                { Uri = "https://schema.org/"
                                  FetchedAt = Some "2026-01-01T00:00:00Z"
                                  Hash = Some realHash } ]
                        Mappings = [] }

                  let drifted = VocabFetcher.detectDrift cacheDir lockFile
                  Expect.isEmpty drifted "no drift when hashes match")
          } ]

// ── AT5: Network failure surfaces clean error ────────────────────────────────

[<Tests>]
let at5 =
    testList
        "AT5: Network failure surfaces clean error"
        [ test "fetchOrLoad raises exception for unreachable URI (no cache)" {
              withTempDir (fun cacheDir ->
                  let unreachableUri = "https://nonexistent.example.invalid/vocab"

                  Expect.throws
                      (fun () -> VocabFetcher.fetchOrLoad cacheDir "broken" unreachableUri |> ignore)
                      "should throw for unreachable URI")
          }

          test "no partial cache file is written on network failure" {
              withTempDir (fun cacheDir ->
                  let unreachableUri = "https://nonexistent.example.invalid/vocab"

                  try
                      VocabFetcher.fetchOrLoad cacheDir "broken" unreachableUri |> ignore
                  with _ ->
                      ()

                  let files = Directory.GetFiles(cacheDir, "broken.*")
                  Expect.isEmpty files "no partial cache file should be written on failure")
          } ]

// ── AT3: Format auto-detection (offline: local content via file bytes) ────────

[<Tests>]
let at3 =
    testList
        "AT3: Format auto-detection (from cache)"
        [ test "JSON-LD cached file produces non-empty graph" {
              withTempDir (fun cacheDir ->
                  let content = System.Text.Encoding.UTF8.GetBytes(sampleJsonLd)
                  let hash = computeHash content
                  let hashHex = hash.Substring(7)
                  let cachedFile = Path.Combine(cacheDir, $"vocab.{hashHex}.jsonld")
                  File.WriteAllBytes(cachedFile, content)

                  let result =
                      VocabFetcher.fetchOrLoad cacheDir "vocab" "https://example.org/irrelevant"

                  Expect.isFalse result.Graph.IsEmpty "JSON-LD graph should have triples")
          }

          test "Turtle cached file produces non-empty graph" {
              withTempDir (fun cacheDir ->
                  let content = System.Text.Encoding.UTF8.GetBytes(sampleTurtle)
                  let hash = computeHash content
                  let hashHex = hash.Substring(7)
                  let cachedFile = Path.Combine(cacheDir, $"vocab.{hashHex}.ttl")
                  File.WriteAllBytes(cachedFile, content)

                  let result =
                      VocabFetcher.fetchOrLoad cacheDir "vocab" "https://example.org/irrelevant"

                  Expect.isFalse result.Graph.IsEmpty "Turtle graph should have triples")
          }

          test "RDF/XML cached file produces non-empty graph" {
              withTempDir (fun cacheDir ->
                  let content = System.Text.Encoding.UTF8.GetBytes(sampleRdfXml)
                  let hash = computeHash content
                  let hashHex = hash.Substring(7)
                  let cachedFile = Path.Combine(cacheDir, $"vocab.{hashHex}.rdf")
                  File.WriteAllBytes(cachedFile, content)

                  let result =
                      VocabFetcher.fetchOrLoad cacheDir "vocab" "https://example.org/irrelevant"

                  Expect.isFalse result.Graph.IsEmpty "RDF/XML graph should have triples")
          } ]
