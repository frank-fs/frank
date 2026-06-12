module Frank.Cli.Tests.SemanticExtractTests

open System
open System.IO
open System.Collections.Generic
open System.Collections.ObjectModel
open Expecto
open Frank.Semantic
open Frank.Cli

// ── Helpers ──────────────────────────────────────────────────────────────────

let private minimalFsproj (sourceFiles: string list) =
    let compileItems =
        sourceFiles
        |> List.map (fun f -> sprintf "    <Compile Include=\"%s\" />" (Path.GetFileName f))
        |> String.concat "\n"

    sprintf
        """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <OutputType>Library</OutputType>
  </PropertyGroup>
  <ItemGroup>
%s
  </ItemGroup>
</Project>"""
        compileItems

/// Create a temp directory with the given source files and a minimal .fsproj.
/// Returns the path to the .fsproj file.
let private createTempProject (sources: (string * string) list) : string =
    let dir =
        Path.Combine(Path.GetTempPath(), "frank_cli_extract_test_" + Guid.NewGuid().ToString("N"))

    Directory.CreateDirectory(dir) |> ignore

    let sourceFiles =
        sources
        |> List.map (fun (name, content) ->
            let path = Path.Combine(dir, name)
            File.WriteAllText(path, content)
            path)

    let fsproj = Path.Combine(dir, "TestProject.fsproj")
    File.WriteAllText(fsproj, minimalFsproj sourceFiles)
    fsproj

let private deleteTempProject (fsproj: string) =
    try
        let dir = Path.GetDirectoryName(fsproj)
        Directory.Delete(dir, true)
    with _ ->
        ()

/// Build a minimal in-memory VocabularyRegistry for the given prefix→IRI and using-set.
let private makeRegistry (prefixes: (string * string) list) (usingPrefixes: string list) : VocabularyRegistry =
    { Prefixes = prefixes |> List.map (fun (k, v) -> k, Uri(v)) |> Map.ofList
      Using = Set.ofList usingPrefixes
      EquivalentClasses = ReadOnlyDictionary<Type, Uri>(Dictionary<Type, Uri>())
      SeeAlso = ReadOnlyDictionary<Type, Uri list>(Dictionary<Type, Uri list>())
      FieldSeeAlso = ReadOnlyDictionary<(Type * string), Uri list>(Dictionary<(Type * string), Uri list>())
      ProvClasses = ReadOnlyDictionary<Type, ProvOClass>(Dictionary<Type, ProvOClass>())
      ConstraintPatterns =
        ReadOnlyDictionary<(Type * string), string>(Dictionary<(Type * string), string>()) }

// ── Tests ─────────────────────────────────────────────────────────────────────

[<Tests>]
let at1ParsePrefixAndUsing =
    testList
        "AT1: parseVocabularyFile extracts prefixes and using declarations"
        [ test "prefix and using calls are parsed from Vocabulary.fs" {
              let vocabularySource =
                  """module MyApp.Vocabulary
open Frank.Semantic
let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        using "schema"
    }
"""

              let fsproj = createTempProject [ "Vocabulary.fs", vocabularySource ]

              try
                  let vocabFile = Path.Combine(Path.GetDirectoryName(fsproj), "Vocabulary.fs")

                  match SemanticCommands.parseVocabularyFile vocabFile with
                  | Error msg -> failtest $"Expected Ok but got Error: {msg}"
                  | Ok(prefixes, usingSet) ->
                      Expect.isTrue (prefixes |> Map.containsKey "schema") "schema prefix should be present"
                      Expect.equal (prefixes.["schema"].ToString()) "https://schema.org/" "schema IRI"
                      Expect.isTrue (usingSet |> Set.contains "schema") "schema should be in using set"
              finally
                  deleteTempProject fsproj
          }

          test "multiple prefixes and using declarations are all captured" {
              let vocabularySource =
                  """module MyApp.Vocabulary
open Frank.Semantic
let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        prefix "ex" "http://example.com/"
        using "schema"
        using "ex"
    }
"""

              let fsproj = createTempProject [ "Vocabulary.fs", vocabularySource ]

              try
                  let vocabFile = Path.Combine(Path.GetDirectoryName(fsproj), "Vocabulary.fs")

                  match SemanticCommands.parseVocabularyFile vocabFile with
                  | Error msg -> failtest $"Expected Ok but got Error: {msg}"
                  | Ok(prefixes, usingSet) ->
                      Expect.isTrue (prefixes |> Map.containsKey "schema") "schema prefix"
                      Expect.isTrue (prefixes |> Map.containsKey "ex") "ex prefix"
                      Expect.isTrue (usingSet |> Set.contains "schema") "schema in using"
                      Expect.isTrue (usingSet |> Set.contains "ex") "ex in using"
              finally
                  deleteTempProject fsproj
          } ]

[<Tests>]
let at2SummaryCountsCorrect =
    testList
        "AT2: summarizeMappings counts confirmed/proposed/unresolved correctly"
        [ test "empty mappings produce zero counts" {
              let summary = SemanticCommands.summarizeMappings []
              Expect.equal summary.Confirmed 0 "confirmed"
              Expect.equal summary.Proposed 0 "proposed"
              Expect.equal summary.Unresolved 0 "unresolved"
          }

          test "mixed mappings are counted correctly" {
              let mappings =
                  [ { FsharpType = "A.Order"
                      Iri = "schema:Order"
                      Confidence = 0.92
                      Source = Convention
                      Status = Confirmed
                      Fields = [] }
                    { FsharpType = "A.Product"
                      Iri = "schema:Product"
                      Confidence = 0.7
                      Source = Convention
                      Status = Proposed
                      Fields = [] }
                    { FsharpType = "A.Widget"
                      Iri = ""
                      Confidence = 0.0
                      Source = Convention
                      Status = Unresolved
                      Fields = [] } ]

              let summary = SemanticCommands.summarizeMappings mappings
              Expect.equal summary.Confirmed 1 "confirmed = 1"
              Expect.equal summary.Proposed 1 "proposed = 1"
              Expect.equal summary.Unresolved 1 "unresolved = 1"
          } ]

[<Tests>]
let at3SummaryString =
    testList
        "AT3: formatSummary produces expected output string"
        [ test "format matches expected pattern" {
              let summary: SemanticCommands.ExtractSummary =
                  { Confirmed = 12
                    Proposed = 3
                    Unresolved = 1 }

              let line = SemanticCommands.formatSummary summary
              Expect.equal line "Confirmed: 12, Proposed: 3, Unresolved: 1" "summary line"
          } ]

[<Tests>]
let at4MergePreservesLlm =
    testList
        "AT4: extract merge preserves LLM-confirmed entries"
        [ test "confirmed llm entry survives re-extract against same types" {
              let llmEntry: TypeMapping =
                  { FsharpType = "MyApp.Order"
                    Iri = "https://schema.org/PurchaseOrder"
                    Confidence = 0.99
                    Source = Llm
                    Status = Confirmed
                    Fields = [] }

              let conventionEntry: TypeMapping =
                  { FsharpType = "MyApp.Order"
                    Iri = "https://schema.org/Order"
                    Confidence = 0.91
                    Source = Convention
                    Status = Confirmed
                    Fields = [] }

              let existing =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.UtcNow
                    Vocabularies = Map.empty
                    Mappings = [ llmEntry ] }

              let resolved = [ conventionEntry ]
              let merged = LockFile.merge existing resolved
              let preserved = merged.Mappings |> List.tryFind (fun m -> m.FsharpType = "MyApp.Order")
              Expect.isSome preserved "Order mapping should survive"
              Expect.equal preserved.Value.Source Llm "source should remain Llm"
              Expect.equal preserved.Value.Iri "https://schema.org/PurchaseOrder" "IRI preserved from llm entry"
          } ]

[<Tests>]
let at5BuildRegistryFromParsed =
    testList
        "AT5: buildRegistryFromParsed reconstructs VocabularyRegistry from parsed data"
        [ test "prefixes and using set are reconstructed" {
              let prefixes = Map.ofList [ "schema", Uri("https://schema.org/") ]
              let usingSet = Set.singleton "schema"

              let registry = SemanticCommands.buildRegistryFromParsed prefixes usingSet

              Expect.isTrue (registry.Prefixes |> Map.containsKey "schema") "schema prefix in registry"
              Expect.isTrue (registry.Using |> Set.contains "schema") "schema in using set"
              Expect.equal (registry.Prefixes.["schema"].ToString()) "https://schema.org/" "IRI correct"
          } ]

[<Tests>]
let at6LockFileWrittenWithFrankDir =
    testList
        "AT6: extract writes lock file to .frank/semantic-mappings.lock.json"
        [ test "extract result writes to expected path" {
              let registry = makeRegistry [ "schema", "https://schema.org/" ] [ "schema" ]

              let mappings =
                  [ { FsharpType = "MyApp.Order"
                      Iri = "https://schema.org/Order"
                      Confidence = 0.91
                      Source = Convention
                      Status = Confirmed
                      Fields = [] } ]

              let vocabularies =
                  Map.ofList
                      [ "schema",
                        { Uri = "https://schema.org/"
                          FetchedAt = None
                          Hash = None } ]

              let tmpDir =
                  Path.Combine(
                      Path.GetTempPath(),
                      "frank_extract_lock_test_" + Guid.NewGuid().ToString("N")
                  )

              Directory.CreateDirectory(tmpDir) |> ignore

              try
                  let lockPath = Path.Combine(tmpDir, ".frank", "semantic-mappings.lock.json")

                  SemanticCommands.writeLockFile lockPath vocabularies mappings

                  Expect.isTrue (File.Exists(lockPath)) "lock file should exist"

                  match LockFile.read lockPath with
                  | Error msg -> failtest $"Lock file unreadable: {msg}"
                  | Ok lf ->
                      Expect.equal lf.SchemaVersion 1 "schema version"
                      Expect.equal (List.length lf.Mappings) 1 "one mapping"
                      Expect.equal lf.Mappings.[0].FsharpType "MyApp.Order" "type name"
              finally
                  Directory.Delete(tmpDir, true)
          } ]
