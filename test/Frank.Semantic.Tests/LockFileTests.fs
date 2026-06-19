module Frank.Semantic.Tests.LockFileTests

open System
open System.IO
open Expecto
open FsCheck
open Frank.Semantic

// ── Generators ────────────────────────────────────────────────────────────────

let private genMappingSource =
    Gen.elements [ MappingSource.Convention; MappingSource.Llm; MappingSource.Manual ]

let private genMappingStatus =
    Gen.elements [ MappingStatus.Confirmed; MappingStatus.Proposed; MappingStatus.Unresolved ]

let private genFieldMapping =
    gen {
        let! name = Arb.generate<NonEmptyString> |> Gen.map (fun (NonEmptyString s) -> s)
        let! iri = Gen.optionOf (Arb.generate<NonEmptyString> |> Gen.map (fun (NonEmptyString s) -> s))
        let! confidence = Gen.choose (0, 100) |> Gen.map (fun n -> float n / 100.0)
        let! source = genMappingSource
        let! status = genMappingStatus

        return
            { Name = name
              Iri = iri
              Confidence = confidence
              Source = source
              Status = status }
    }

let private genMapping =
    gen {
        let! fsType = Arb.generate<NonEmptyString> |> Gen.map (fun (NonEmptyString s) -> s)
        let! iri = Gen.optionOf (Arb.generate<NonEmptyString> |> Gen.map (fun (NonEmptyString s) -> s))
        let! confidence = Gen.choose (0, 100) |> Gen.map (fun n -> float n / 100.0)
        let! source = genMappingSource
        let! status = genMappingStatus
        let! fields = Gen.listOfLength 2 genFieldMapping
        let! alternates = Gen.listOf (Arb.generate<NonEmptyString> |> Gen.map (fun (NonEmptyString s) -> s))

        return
            { FSharpType = fsType
              Iri = iri
              Confidence = confidence
              Source = source
              Status = status
              Alternates = alternates
              Fields = fields }
    }

let private truncSec (dto: DateTimeOffset) =
    DateTimeOffset(dto.Year, dto.Month, dto.Day, dto.Hour, dto.Minute, dto.Second, dto.Offset)

let private genVocabEntry =
    gen {
        let! uri =
            Arb.generate<NonEmptyString>
            |> Gen.map (fun (NonEmptyString s) -> $"https://{s}.example.org/")

        let fetchedAt = truncSec DateTimeOffset.UtcNow

        let! hash =
            Arb.generate<NonEmptyString>
            |> Gen.map (fun (NonEmptyString s) -> $"sha256:{s}")

        return
            ({ Uri = uri
               FetchedAt = fetchedAt
               Hash = hash }
            : LockFile.VocabularyEntry)
    }

let private genLockFile =
    gen {
        let generated = truncSec DateTimeOffset.UtcNow
        let! vocabCount = Gen.choose (0, 3)

        let! vocabKeys =
            Gen.listOfLength vocabCount (Arb.generate<NonEmptyString> |> Gen.map (fun (NonEmptyString s) -> s))

        let! vocabValues = Gen.listOfLength vocabCount genVocabEntry
        let vocabs = List.zip vocabKeys vocabValues |> Map.ofList
        let! mappings = Gen.listOfLength 2 genMapping

        return
            ({ SchemaVersion = 1
               Generated = generated
               Vocabularies = vocabs
               Mappings = mappings }
            : LockFile.LockFile)
    }

// ── DU ↔ string totality ──────────────────────────────────────────────────────

[<Tests>]
let mappingSourceStringTests =
    testList
        "MappingSource ↔ string totality"
        [ test "Convention → 'convention'" {
              Expect.equal (LockFile.mappingSourceToString MappingSource.Convention) "convention" "convention"
          }

          test "Llm → 'llm'" { Expect.equal (LockFile.mappingSourceToString MappingSource.Llm) "llm" "llm" }

          test "Manual → 'manual'" {
              Expect.equal (LockFile.mappingSourceToString MappingSource.Manual) "manual" "manual"
          }

          test "'convention' → Convention" {
              Expect.equal (LockFile.mappingSourceFromString "convention") (Ok MappingSource.Convention) "convention"
          }

          test "'llm' → Llm" { Expect.equal (LockFile.mappingSourceFromString "llm") (Ok MappingSource.Llm) "llm" }

          test "'manual' → Manual" {
              Expect.equal (LockFile.mappingSourceFromString "manual") (Ok MappingSource.Manual) "manual"
          }

          test "unknown string → Error" {
              Expect.isError (LockFile.mappingSourceFromString "unknown") "unknown source → Error"
          }

          test "empty string → Error" { Expect.isError (LockFile.mappingSourceFromString "") "empty → Error" }

          testProperty "every DU case round-trips through string" (fun () ->
              let cases = [ MappingSource.Convention; MappingSource.Llm; MappingSource.Manual ]

              cases
              |> List.forall (fun c -> LockFile.mappingSourceFromString (LockFile.mappingSourceToString c) = Ok c)) ]

[<Tests>]
let mappingStatusStringTests =
    testList
        "MappingStatus ↔ string totality"
        [ test "Confirmed → 'confirmed'" {
              Expect.equal (LockFile.mappingStatusToString MappingStatus.Confirmed) "confirmed" "confirmed"
          }

          test "Proposed → 'proposed'" {
              Expect.equal (LockFile.mappingStatusToString MappingStatus.Proposed) "proposed" "proposed"
          }

          test "Unresolved → 'unresolved'" {
              Expect.equal (LockFile.mappingStatusToString MappingStatus.Unresolved) "unresolved" "unresolved"
          }

          test "'confirmed' → Confirmed" {
              Expect.equal (LockFile.mappingStatusFromString "confirmed") (Ok MappingStatus.Confirmed) "confirmed"
          }

          test "'proposed' → Proposed" {
              Expect.equal (LockFile.mappingStatusFromString "proposed") (Ok MappingStatus.Proposed) "proposed"
          }

          test "'unresolved' → Unresolved" {
              Expect.equal (LockFile.mappingStatusFromString "unresolved") (Ok MappingStatus.Unresolved) "unresolved"
          }

          test "unknown string → Error" {
              Expect.isError (LockFile.mappingStatusFromString "unknown") "unknown status → Error"
          }

          testProperty "every DU case round-trips through string" (fun () ->
              let cases =
                  [ MappingStatus.Confirmed; MappingStatus.Proposed; MappingStatus.Unresolved ]

              cases
              |> List.forall (fun c -> LockFile.mappingStatusFromString (LockFile.mappingStatusToString c) = Ok c)) ]

// ── AT2: schema version mismatch fails closed ─────────────────────────────────

[<Tests>]
let schemaVersionTests =
    testList
        "AT2: schema version validation"
        [ test "schemaVersion 1 → Ok" {
              let json =
                  """{
  "schemaVersion": 1,
  "generated": "2026-04-20T12:00:00Z",
  "vocabularies": {},
  "mappings": []
}"""

              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(path, json)
                  let result = LockFile.read path
                  Expect.isOk result "version 1 must succeed"
              finally
                  File.Delete path
          }

          test "schemaVersion 99 → Error containing 'schema version 99 not supported'" {
              let json =
                  """{
  "schemaVersion": 99,
  "generated": "2026-04-20T12:00:00Z",
  "vocabularies": {},
  "mappings": []
}"""

              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(path, json)

                  match LockFile.read path with
                  | Ok _ -> failtest "expected Error for unsupported version"
                  | Error msg -> Expect.stringContains msg "schema version 99 not supported" "error message"
              finally
                  File.Delete path
          }

          test "malformed JSON → Error with context" {
              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(path, "{ this is not json }")
                  let result = LockFile.read path
                  Expect.isError result "malformed JSON must return Error"
              finally
                  File.Delete path
          } ]

// ── AT1/AT3: round-trip and determinism ───────────────────────────────────────

[<Tests>]
let roundTripTests =
    testList
        "AT1/AT3: round-trip and determinism"
        [ test "read(write(lf)) reconstructs fields (modulo Generated)" {
              let lf: LockFile.LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.Parse("2026-04-20T12:00:00Z")
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = DateTimeOffset.Parse("2026-04-20T11:00:00Z")
                              Hash = "sha256:abc123" } ]
                    Mappings =
                      [ { FSharpType = "MyApp.Order"
                          Iri = Some "schema:Order"
                          Confidence = 0.92
                          Source = MappingSource.Convention
                          Status = MappingStatus.Confirmed
                          Alternates = [ "schema:OrderAction" ]
                          Fields =
                            [ { Name = "Total"
                                Iri = Some "schema:totalPaymentDue"
                                Confidence = 0.78
                                Source = MappingSource.Llm
                                Status = MappingStatus.Confirmed } ] } ] }

              let path = Path.GetTempFileName()

              try
                  LockFile.write path lf

                  match LockFile.read path with
                  | Error e -> failtest $"read failed: {e}"
                  | Ok result ->
                      Expect.equal result.SchemaVersion 1 "schemaVersion"
                      Expect.equal result.Vocabularies lf.Vocabularies "vocabularies"
                      Expect.equal result.Mappings.Length 1 "mapping count"
                      let m = result.Mappings.[0]
                      Expect.equal m.FSharpType "MyApp.Order" "fsharpType"
                      Expect.equal m.Iri (Some "schema:Order") "iri"
                      Expect.equal m.Source MappingSource.Convention "source"
                      Expect.equal m.Status MappingStatus.Confirmed "status"
                      Expect.equal m.Alternates [ "schema:OrderAction" ] "alternates preserved through round-trip"
                      let f = m.Fields.[0]
                      Expect.equal f.Name "Total" "field name"
                      Expect.equal f.Iri (Some "schema:totalPaymentDue") "field iri"
                      Expect.equal f.Source MappingSource.Llm "field source"
              finally
                  File.Delete path
          }

          test "null iri → Iri = None round-trips" {
              let lf: LockFile.LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.Parse("2026-04-20T12:00:00Z")
                    Vocabularies = Map.empty
                    Mappings =
                      [ { FSharpType = "MyApp.Unknown"
                          Iri = None
                          Confidence = 0.0
                          Source = MappingSource.Convention
                          Status = MappingStatus.Unresolved
                          Alternates = []
                          Fields = [] } ] }

              let path = Path.GetTempFileName()

              try
                  LockFile.write path lf

                  match LockFile.read path with
                  | Error e -> failtest $"read failed: {e}"
                  | Ok result ->
                      let m = result.Mappings.[0]
                      Expect.isNone m.Iri "iri must be None after round-trip"
              finally
                  File.Delete path
          }

          test "AT3: write of same LockFile twice → byte-identical output" {
              let lf: LockFile.LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.Parse("2026-04-20T12:00:00Z")
                    Vocabularies =
                      Map.ofList
                          [ "schema",
                            { Uri = "https://schema.org/"
                              FetchedAt = DateTimeOffset.Parse("2026-04-20T11:00:00Z")
                              Hash = "sha256:abc123" }
                            "prov",
                            { Uri = "http://www.w3.org/ns/prov#"
                              FetchedAt = DateTimeOffset.Parse("2026-04-20T11:00:00Z")
                              Hash = "sha256:def456" } ]
                    Mappings =
                      [ { FSharpType = "MyApp.Order"
                          Iri = Some "schema:Order"
                          Confidence = 0.92
                          Source = MappingSource.Convention
                          Status = MappingStatus.Confirmed
                          Alternates = []
                          Fields = [] } ] }

              let path1 = Path.GetTempFileName()
              let path2 = Path.GetTempFileName()

              try
                  LockFile.write path1 lf
                  LockFile.write path2 lf
                  let bytes1 = File.ReadAllBytes path1
                  let bytes2 = File.ReadAllBytes path2
                  Expect.equal bytes1 bytes2 "write is deterministic: byte-identical"
              finally
                  File.Delete path1
                  File.Delete path2
          }

          testProperty
              "FsCheck: read(write(lf)) reconstructs all fields"
              (Prop.forAll (Arb.fromGen genLockFile) (fun lf ->
                  let path = Path.GetTempFileName()

                  try
                      LockFile.write path lf

                      match LockFile.read path with
                      | Error _ -> false
                      | Ok result ->
                          result.SchemaVersion = lf.SchemaVersion
                          && result.Vocabularies = lf.Vocabularies
                          && result.Mappings.Length = lf.Mappings.Length
                          && List.zip result.Mappings lf.Mappings
                             |> List.forall (fun (r, orig) ->
                                 r.FSharpType = orig.FSharpType
                                 && r.Iri = orig.Iri
                                 && abs (r.Confidence - orig.Confidence) < 1e-9
                                 && r.Source = orig.Source
                                 && r.Status = orig.Status
                                 && r.Alternates = orig.Alternates
                                 && r.Fields.Length = orig.Fields.Length
                                 && List.zip r.Fields orig.Fields
                                    |> List.forall (fun (rf, of_) ->
                                        rf.Name = of_.Name
                                        && rf.Iri = of_.Iri
                                        && abs (rf.Confidence - of_.Confidence) < 1e-9
                                        && rf.Source = of_.Source
                                        && rf.Status = of_.Status))
                  finally
                      File.Delete path)) ]

// ── AT5: diff-friendly serialization ─────────────────────────────────────────

[<Tests>]
let diffFriendlyTests =
    test "AT5: adding one mapping touches only the new entry's lines" {
        let vocab: LockFile.VocabularyEntry =
            { Uri = "https://schema.org/"
              FetchedAt = DateTimeOffset.Parse("2026-04-20T11:00:00Z")
              Hash = "sha256:abc123" }

        let mapping1: Mapping =
            { FSharpType = "MyApp.Order"
              Iri = Some "schema:Order"
              Confidence = 0.92
              Source = MappingSource.Convention
              Status = MappingStatus.Confirmed
              Alternates = []
              Fields = [] }

        let mapping2: Mapping =
            { FSharpType = "MyApp.Product"
              Iri = Some "schema:Product"
              Confidence = 0.88
              Source = MappingSource.Convention
              Status = MappingStatus.Confirmed
              Alternates = []
              Fields = [] }

        let lf1: LockFile.LockFile =
            { SchemaVersion = 1
              Generated = DateTimeOffset.Parse("2026-04-20T12:00:00Z")
              Vocabularies = Map.ofList [ "schema", vocab ]
              Mappings = [ mapping1 ] }

        let lf2 =
            { lf1 with
                Mappings = [ mapping1; mapping2 ] }

        let path1 = Path.GetTempFileName()
        let path2 = Path.GetTempFileName()

        try
            LockFile.write path1 lf1
            LockFile.write path2 lf2

            let lines1 = File.ReadAllLines path1 |> Set.ofArray
            let lines2 = File.ReadAllLines path2 |> Array.toList

            let newLines = lines2 |> List.filter (fun l -> not (Set.contains l lines1))

            Expect.isNonEmpty newLines "new mapping must add lines"

            let existingPreserved =
                File.ReadAllLines path1
                |> Array.forall (fun line -> lines2 |> List.contains line)

            Expect.isTrue existingPreserved "all lines from file1 appear in file2 (existing content preserved)"
        finally
            File.Delete path1
            File.Delete path2
    }

// ── merge ─────────────────────────────────────────────────────────────────────

[<Tests>]
let mergeTests =
    testList
        "LockFile.merge"
        [ test "merge replaces matching fsharpType with resolved entry" {
              let existing: Mapping =
                  { FSharpType = "MyApp.Order"
                    Iri = None
                    Confidence = 0.0
                    Source = MappingSource.Convention
                    Status = MappingStatus.Unresolved
                    Alternates = []
                    Fields = [] }

              let resolved: Mapping =
                  { FSharpType = "MyApp.Order"
                    Iri = Some "schema:Order"
                    Confidence = 0.95
                    Source = MappingSource.Manual
                    Status = MappingStatus.Confirmed
                    Alternates = []
                    Fields = [] }

              let lf: LockFile.LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.UtcNow
                    Vocabularies = Map.empty
                    Mappings = [ existing ] }

              let result = LockFile.merge lf [ resolved ]
              Expect.equal result.Mappings.Length 1 "still one mapping"
              let m = result.Mappings.[0]
              Expect.equal m.Iri (Some "schema:Order") "iri updated"
              Expect.equal m.Status MappingStatus.Confirmed "status updated"
              Expect.equal m.Source MappingSource.Manual "source updated"
          }

          test "merge keeps unmatched existing entries" {
              let existing: Mapping =
                  { FSharpType = "MyApp.Order"
                    Iri = Some "schema:Order"
                    Confidence = 0.92
                    Source = MappingSource.Convention
                    Status = MappingStatus.Confirmed
                    Alternates = []
                    Fields = [] }

              let resolved: Mapping =
                  { FSharpType = "MyApp.Product"
                    Iri = Some "schema:Product"
                    Confidence = 0.88
                    Source = MappingSource.Llm
                    Status = MappingStatus.Confirmed
                    Alternates = []
                    Fields = [] }

              let lf: LockFile.LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.UtcNow
                    Vocabularies = Map.empty
                    Mappings = [ existing ] }

              let result = LockFile.merge lf [ resolved ]
              Expect.equal result.Mappings.Length 2 "both entries present"
              let types = result.Mappings |> List.map (fun m -> m.FSharpType) |> Set.ofList
              Expect.equal types (Set.ofList [ "MyApp.Order"; "MyApp.Product" ]) "both types present"
          }

          test "merge replaces matching field by name within a type" {
              let existing: Mapping =
                  { FSharpType = "MyApp.Order"
                    Iri = Some "schema:Order"
                    Confidence = 0.92
                    Source = MappingSource.Convention
                    Status = MappingStatus.Confirmed
                    Alternates = []
                    Fields =
                      [ { Name = "Total"
                          Iri = None
                          Confidence = 0.0
                          Source = MappingSource.Convention
                          Status = MappingStatus.Unresolved } ] }

              let resolvedField: FieldMapping =
                  { Name = "Total"
                    Iri = Some "schema:totalPaymentDue"
                    Confidence = 0.95
                    Source = MappingSource.Manual
                    Status = MappingStatus.Confirmed }

              let resolved: Mapping =
                  { existing with
                      Fields = [ resolvedField ] }

              let lf: LockFile.LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.UtcNow
                    Vocabularies = Map.empty
                    Mappings = [ existing ] }

              let result = LockFile.merge lf [ resolved ]
              let field = result.Mappings.[0].Fields.[0]
              Expect.equal field.Iri (Some "schema:totalPaymentDue") "field iri updated"
              Expect.equal field.Status MappingStatus.Confirmed "field status updated"
          }

          test "merge is pure: original LockFile unchanged" {
              let existing: Mapping =
                  { FSharpType = "MyApp.Order"
                    Iri = None
                    Confidence = 0.0
                    Source = MappingSource.Convention
                    Status = MappingStatus.Unresolved
                    Alternates = []
                    Fields = [] }

              let lf: LockFile.LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.UtcNow
                    Vocabularies = Map.empty
                    Mappings = [ existing ] }

              let resolved =
                  { existing with
                      Iri = Some "schema:Order"
                      Status = MappingStatus.Confirmed }

              let _ = LockFile.merge lf [ resolved ]
              Expect.isNone lf.Mappings.[0].Iri "original lf.Mappings unchanged"
          } ]

// ── hardening: malformed-shape guards ────────────────────────────────────────

[<Tests>]
let malformedShapeTests =
    testList
        "LockFile.read: malformed root / field shapes"
        [ test "root is JSON array → Error mentioning 'root must be a JSON object'" {
              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(path, "[]")

                  match LockFile.read path with
                  | Ok _ -> failtest "expected Error for array root"
                  | Error msg -> Expect.stringContains msg "root must be a JSON object" "error message"
              finally
                  File.Delete path
          }

          test "schemaVersion absent → Error mentioning 'schemaVersion is required'" {
              let json =
                  """{
  "generated": "2026-04-20T12:00:00Z",
  "vocabularies": {},
  "mappings": []
}"""

              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(path, json)

                  match LockFile.read path with
                  | Ok _ -> failtest "expected Error for missing schemaVersion"
                  | Error msg -> Expect.stringContains msg "schemaVersion is required" "error message"
              finally
                  File.Delete path
          }

          test "'mappings' is object not array → Error, not exception" {
              let json =
                  """{
  "schemaVersion": 1,
  "generated": "2026-04-20T12:00:00Z",
  "vocabularies": {},
  "mappings": {}
}"""

              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(path, json)
                  let result = LockFile.read path
                  Expect.isError result "object mappings must return Error"
              finally
                  File.Delete path
          }

          test "mapping with 'fields' as string → Error, not exception" {
              let json =
                  """{
  "schemaVersion": 1,
  "generated": "2026-04-20T12:00:00Z",
  "vocabularies": {},
  "mappings": [
    {
      "fsharpType": "MyApp.Foo",
      "confidence": 0.5,
      "source": "manual",
      "status": "confirmed",
      "alternates": [],
      "fields": "x"
    }
  ]
}"""

              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(path, json)
                  let result = LockFile.read path
                  Expect.isError result "string fields must return Error"
              finally
                  File.Delete path
          }

          test "mapping with non-string alternates element → Error mentioning alternates" {
              let json =
                  """{
  "schemaVersion": 1,
  "generated": "2026-04-20T12:00:00Z",
  "vocabularies": {},
  "mappings": [
    {
      "fsharpType": "MyApp.Foo",
      "confidence": 0.5,
      "source": "manual",
      "status": "confirmed",
      "alternates": ["ok", 7],
      "fields": []
    }
  ]
}"""

              let path = Path.GetTempFileName()

              try
                  File.WriteAllText(path, json)

                  match LockFile.read path with
                  | Ok _ -> failtest "expected Error for non-string alternate"
                  | Error msg -> Expect.stringContains msg "alternates" "error mentions alternates"
              finally
                  File.Delete path
          } ]

// ── countByStatus ─────────────────────────────────────────────────────────────

[<Tests>]
let countByStatusTests =
    testList
        "countByStatus"
        [ test "2 Confirmed + 1 Proposed + 3 Unresolved" {
              let make status : Mapping =
                  { FSharpType = "T"
                    Iri = None
                    Confidence = 0.0
                    Source = MappingSource.Convention
                    Status = status
                    Alternates = []
                    Fields = [] }

              let mappings =
                  [ make MappingStatus.Confirmed
                    make MappingStatus.Confirmed
                    make MappingStatus.Proposed
                    make MappingStatus.Unresolved
                    make MappingStatus.Unresolved
                    make MappingStatus.Unresolved ]

              let counts = LockFile.countByStatus mappings
              Expect.equal counts.Confirmed 2 "Confirmed"
              Expect.equal counts.Proposed 1 "Proposed"
              Expect.equal counts.Unresolved 3 "Unresolved"
          }

          test "Excluded count is tallied separately from other statuses" {
              let make status : Mapping =
                  { FSharpType = "T"
                    Iri = None
                    Confidence = 0.0
                    Source = MappingSource.Convention
                    Status = status
                    Alternates = []
                    Fields = [] }

              let mappings =
                  [ make MappingStatus.Confirmed
                    make MappingStatus.Confirmed
                    make MappingStatus.Proposed
                    make MappingStatus.Unresolved
                    make MappingStatus.Excluded
                    make MappingStatus.Excluded ]

              let counts = LockFile.countByStatus mappings
              Expect.equal counts.Confirmed 2 "Confirmed"
              Expect.equal counts.Proposed 1 "Proposed"
              Expect.equal counts.Unresolved 1 "Unresolved"
              Expect.equal counts.Excluded 2 "Excluded"
          } ]

// ── MappingStatus.Excluded ────────────────────────────────────────────────────

[<Tests>]
let excludedStatusTests =
    testList
        "MappingStatus.Excluded"
        [ test "mappingStatusFromString 'excluded' → Ok Excluded" {
              Expect.equal
                  (LockFile.mappingStatusFromString "excluded")
                  (Ok MappingStatus.Excluded)
                  "excluded"
          }

          test "mappingStatusToString Excluded → 'excluded'" {
              Expect.equal (LockFile.mappingStatusToString MappingStatus.Excluded) "excluded" "excluded"
          }

          test "Excluded round-trips through write/read with 'excluded' in JSON" {
              let lf: LockFile.LockFile =
                  { SchemaVersion = 1
                    Generated = DateTimeOffset.Parse("2026-04-20T12:00:00Z")
                    Vocabularies = Map.empty
                    Mappings =
                      [ { FSharpType = "MyApp.Legacy"
                          Iri = None
                          Confidence = 0.0
                          Source = MappingSource.Manual
                          Status = MappingStatus.Excluded
                          Alternates = []
                          Fields =
                            [ { Name = "OldField"
                                Iri = None
                                Confidence = 0.0
                                Source = MappingSource.Manual
                                Status = MappingStatus.Excluded } ] } ] }

              let path = Path.GetTempFileName()

              try
                  LockFile.write path lf
                  let json = File.ReadAllText path
                  Expect.stringContains json "\"status\": \"excluded\"" "JSON contains excluded status"

                  match LockFile.read path with
                  | Error e -> failtest $"read failed: {e}"
                  | Ok result ->
                      let m = result.Mappings.[0]
                      Expect.equal m.Status MappingStatus.Excluded "mapping status is Excluded"
                      let f = m.Fields.[0]
                      Expect.equal f.Status MappingStatus.Excluded "field status is Excluded"
              finally
                  File.Delete path
          } ]

// ── vocab key sort ────────────────────────────────────────────────────────────

[<Tests>]
let vocabKeySortTests =
    test "write serializes vocabulary keys in sorted order" {
        let lf: LockFile.LockFile =
            { SchemaVersion = 1
              Generated = DateTimeOffset.Parse("2026-04-20T12:00:00Z")
              Vocabularies =
                Map.ofList
                    [ "schema",
                      { Uri = "https://schema.org/"
                        FetchedAt = DateTimeOffset.Parse("2026-04-20T11:00:00Z")
                        Hash = "sha256:aaa" }
                      "prov",
                      { Uri = "http://www.w3.org/ns/prov#"
                        FetchedAt = DateTimeOffset.Parse("2026-04-20T11:00:00Z")
                        Hash = "sha256:bbb" }
                      "dcat",
                      { Uri = "http://www.w3.org/ns/dcat#"
                        FetchedAt = DateTimeOffset.Parse("2026-04-20T11:00:00Z")
                        Hash = "sha256:ccc" } ]
              Mappings = [] }

        let path = Path.GetTempFileName()

        try
            LockFile.write path lf
            let json = File.ReadAllText path
            let dcatIdx = json.IndexOf("\"dcat\"")
            let provIdx = json.IndexOf("\"prov\"")
            let schemaIdx = json.IndexOf("\"schema\"")
            Expect.isTrue (dcatIdx < provIdx && provIdx < schemaIdx) "vocabulary keys sorted alphabetically"
        finally
            File.Delete path
    }
