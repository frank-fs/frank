module Frank.Cli.Core.Tests.ProvenanceEmitterTests

open System
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Cli.Core
open Frank.Cli.Core.Tests.FcsTypecheck

let private okOrFail (r: Result<'a, string>) : 'a =
    match r with
    | Ok v -> v
    | Error e -> failwith $"Expected Ok but got Error: {e}"

let private schemaPrefix = Uri("https://schema.org/")

let private registry: VocabularyRegistry =
    { VocabularyRegistry.empty with
        Prefixes = Map.ofList [ "schema", schemaPrefix ]
        Using = Set.ofList [ "schema" ]
        ProvClasses = Map.ofList [ "MyApp.OrderPlaced", Activity ] }

let private lock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies =
        Map.ofList
            [ "schema",
              { v1Empty with
                  Uri = "https://schema.org/"
                  FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                  Hash = "sha256:test" } ]
      DeclaredPrefixes = Map.empty
      Mappings =
        [ { FSharpType = "MyApp.OrderPlaced"
            Iri = Some "schema:OrderAction"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape = MappingShape.Record [] } ] }

[<Tests>]
let emitTests =
    testList
        "ProvenanceEmitter — emit"
        [ test "emits provClasses entry with case name + class IRI" {
              let src =
                  ProvenanceEmitter.emit "MyApp.GeneratedProvenance" registry lock |> okOrFail

              Expect.stringContains src "module GeneratedProvenance" "module header"
              Expect.stringContains src "Activity" "provClass case rendered"
              Expect.stringContains src "https://schema.org/OrderAction" "class IRI rendered"
              Expect.stringContains src "knownNamespaces" "namespaces emitted"
              Expect.stringContains src "MyApp.OrderPlaced" "FSharpType key rendered"
          }

          test "empty provClasses when no ProvClass set" {
              let emptyRegistry: VocabularyRegistry =
                  { VocabularyRegistry.empty with
                      Prefixes = Map.ofList [ "schema", schemaPrefix ]
                      Using = Set.ofList [ "schema" ] }

              let src =
                  ProvenanceEmitter.emit "MyApp.GeneratedProvenance" emptyRegistry lock
                  |> okOrFail

              Expect.stringContains src "provClasses" "provClasses binding emitted"
          }

          test "bad module name returns Error not exception" {
              let result = ProvenanceEmitter.emit "NoNamespace" registry lock
              Expect.isError result "dotless module name must return Error"
          } ]

[<Tests>]
let compileGateTests =
    testList
        "ProvenanceEmitter — tier-3 compile-gate"
        [ test "emitted GeneratedProvenance compiles (tier 3)" {
              let src =
                  ProvenanceEmitter.emit "MyApp.GeneratedProvenance" registry lock |> okOrFail

              let assemblies = [ typeof<Frank.Semantic.ProvOClass>.Assembly ]

              let diagnostics = typecheckAgainstRealAssemblies src assemblies
              Expect.isEmpty diagnostics $"emitted Provenance module compiles cleanly; errors: {diagnostics}"
          } ]

// A lock that has wikidata as a declared-only EXTERNAL reference prefix (not in vocabularies,
// not used in any mapping IRI) and ttt as an app-owned declared-only prefix (used in ttt:square).
let private lockWithExternalDeclared: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
      Integrity = None
      Vocabularies =
        Map.ofList
            [ "schema",
              { v1Empty with
                  Uri = "https://schema.org/"
                  FetchedAt = DateTimeOffset.Parse("2025-01-01T00:00:00Z")
                  Hash = "sha256:test" } ]
      DeclaredPrefixes =
        Map.ofList
            [ "schema", "https://schema.org/"
              "wikidata", "http://www.wikidata.org/entity/"
              "ttt", "https://example.org/tictactoe#" ]
      Mappings =
        [ { FSharpType = "MyApp.OrderPlaced"
            Iri = Some "schema:OrderAction"
            Confidence = 1.0
            Source = Convention
            Status = Confirmed
            Alternates = []
            Rt = None
            Shape =
              MappingShape.Record
                  [ { Name = "Position"
                      Iri = Some "ttt:square"
                      Confidence = 1.0
                      Source = Manual
                      Status = Confirmed } ] } ] }

[<Tests>]
let toStoredNsClassificationTests =
    testList
        "ProvenanceEmitter — toStoredNs classification"
        [ test "external declared-only vocab keeps absolute URI; app-owned stays host-relative" {
              // RED before fix: current Map.containsKey Vocabularies logic host-strips wikidata to /entity/
              // GREEN after fix: wikidata (declared but not used in mappings) keeps absolute URI
              let src =
                  ProvenanceEmitter.emit "MyApp.GeneratedProvenance" registry lockWithExternalDeclared
                  |> okOrFail

              // wikidata must be absolute — not host-stripped
              Expect.stringContains
                  src
                  "http://www.wikidata.org/entity/"
                  "wikidata namespace must be absolute (external declared-only)"

              // ttt must be host-stripped — it IS used in a mapping IRI (ttt:square)
              Expect.stringContains src "/tictactoe#" "ttt namespace must be host-relative (app-owned)"

              // schema must be absolute — it is in lock.Vocabularies
              Expect.stringContains src "https://schema.org/" "schema namespace must be absolute (vocabulary entry)"
          } ]
