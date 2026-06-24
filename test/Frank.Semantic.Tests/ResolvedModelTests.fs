module Frank.Semantic.Tests.ResolvedModelTests

open System
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile

// ── Helpers ──────────────────────────────────────────────────────────────────

let private schemaPrefix = Uri "https://schema.org/"
let private exPrefix = Uri "http://example.com/vocab#"

let private prefixes = Map.ofList [ "schema", schemaPrefix; "ex", exPrefix ]

let private mkVocabEntry (uri: string) : VocabularyEntry =
    { Uri = uri
      FetchedAt = DateTimeOffset.UtcNow
      Hash = "test" }

let private emptyLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.UtcNow
      Vocabularies = Map.empty
      Mappings = [] }

/// Lock pre-loaded with the schema: prefix — used by tests that resolve schema:* IRIs.
let private lockWithSchema: LockFile =
    { emptyLock with
        Vocabularies = Map.ofList [ "schema", mkVocabEntry "https://schema.org/" ] }

/// Lock pre-loaded with schema: and ex: prefixes.
let private lockWithSchemaAndEx: LockFile =
    { emptyLock with
        Vocabularies =
            Map.ofList
                [ "schema", mkVocabEntry "https://schema.org/"
                  "ex", mkVocabEntry "http://example.com/vocab#" ] }

let private mkMapping fsharpType iri fields : Mapping =
    { FSharpType = fsharpType
      Iri = iri
      Confidence = 1.0
      Source = Convention
      Status = Confirmed
      Alternates = []
      Shape = MappingShape.Record fields }

let private mkField name iri : FieldMapping =
    { Name = name
      Iri = iri
      Confidence = 1.0
      Source = Convention
      Status = Confirmed }

let private baseRegistry: VocabularyRegistry =
    { VocabularyRegistry.empty with
        Prefixes = prefixes }

// ── AT-RM1: join a record mapping ────────────────────────────────────────────

[<Tests>]
let at_rm1 =
    test "AT-RM1: build joins mapping with registry associations" {
        let gameType = "TicTacToe.Model.Game"

        let registry =
            { baseRegistry with
                EquivalentClasses = Map.ofList [ gameType, Uri "https://schema.org/Game" ]
                SeeAlso = Map.ofList [ gameType, [ Uri "https://www.wikidata.org/wiki/Q11907" ] ]
                ProvClasses = Map.ofList [ gameType, ProvOClass.Entity ]
                FieldSeeAlso = Map.ofList [ (gameType, "Board"), [ Uri "https://schema.org/board" ] ]
                ConstraintPatterns = Map.ofList [ (gameType, "Board"), @"^\d{9}$" ] }

        let boardField = mkField "Board" (Some "schema:board")
        let mapping = mkMapping gameType (Some "schema:Game") [ boardField ]

        let lock =
            { lockWithSchema with
                Mappings = [ mapping ] }

        match ResolvedModel.build registry lock with
        | Error e -> failwith $"Expected Ok but got Error: {e}"
        | Ok model ->
            let res = model.Resources |> List.head
            Expect.equal res.FSharpType gameType "FSharpType"
            Expect.equal res.LocalName "Game" "LocalName"
            Expect.equal res.GenericArity 0 "GenericArity"
            Expect.equal res.ClassIri (Some(Uri "https://schema.org/Game")) "ClassIri expanded"
            Expect.equal res.EquivalentClass (Some(Uri "https://schema.org/Game")) "EquivalentClass from registry"
            Expect.equal res.SeeAlso [ Uri "https://www.wikidata.org/wiki/Q11907" ] "SeeAlso from registry"
            Expect.equal res.ProvClass (Some ProvOClass.Entity) "ProvClass from registry"
            let f = res.Fields |> List.head
            Expect.equal f.Name "Board" "field name"
            Expect.equal f.Iri (Some(Uri "https://schema.org/board")) "field IRI expanded"
            Expect.equal f.SeeAlso [ Uri "https://schema.org/board" ] "field SeeAlso from registry"
            Expect.equal f.ConstraintPattern (Some @"^\d{9}$") "field ConstraintPattern"
    }

// ── AT-RM2: LocalName / GenericArity parsing ─────────────────────────────────

[<Tests>]
let at_rm2 =
    testList
        "AT-RM2: LocalName and GenericArity parsing"
        [ test "generic type extracts name and arity" {
              let mapping = mkMapping "Ns.Mod.Holder`1" None []

              let lock =
                  { emptyLock with
                      Mappings = [ mapping ] }

              match ResolvedModel.build baseRegistry lock with
              | Error e -> failwith e
              | Ok model ->
                  let res = model.Resources |> List.head
                  Expect.equal res.LocalName "Holder" "LocalName"
                  Expect.equal res.GenericArity 1 "GenericArity"
          }

          test "non-generic type extracts name arity 0" {
              let mapping = mkMapping "Ns.Mod.Game" None []

              let lock =
                  { emptyLock with
                      Mappings = [ mapping ] }

              match ResolvedModel.build baseRegistry lock with
              | Error e -> failwith e
              | Ok model ->
                  let res = model.Resources |> List.head
                  Expect.equal res.LocalName "Game" "LocalName"
                  Expect.equal res.GenericArity 0 "GenericArity"
          }

          test "deeply nested type extracts last segment" {
              let mapping = mkMapping "A.B.C.Thing" None []

              let lock =
                  { emptyLock with
                      Mappings = [ mapping ] }

              match ResolvedModel.build baseRegistry lock with
              | Error e -> failwith e
              | Ok model ->
                  let res = model.Resources |> List.head
                  Expect.equal res.LocalName "Thing" "LocalName"
                  Expect.equal res.GenericArity 0 "GenericArity"
          } ]

// ── AT-RM3: unknown prefix returns Error ─────────────────────────────────────

[<Tests>]
let at_rm3 =
    test "AT-RM3: unknown prefix in lock IRI returns Error, no exception escapes" {
        let mapping = mkMapping "Some.Type" (Some "unknown:Foo") []

        let lock =
            { emptyLock with
                Mappings = [ mapping ] }

        match ResolvedModel.build baseRegistry lock with
        | Ok _ -> failwith "Expected Error for unknown prefix"
        | Error msg -> Expect.stringContains msg "unknown" "error message names the type or IRI"
    }

// ── AT-RM4: LocalName collision among ClassIri resources ─────────────────────

[<Tests>]
let at_rm4 =
    test "AT-RM4: LocalName collision among resources with ClassIri returns Error" {
        let m1 = mkMapping "Ns.A.Game" (Some "schema:Game") []
        let m2 = mkMapping "Ns.B.Game" (Some "ex:Game") []

        let lock =
            { lockWithSchemaAndEx with
                Mappings = [ m1; m2 ] }

        match ResolvedModel.build baseRegistry lock with
        | Ok _ -> failwith "Expected Error for LocalName collision"
        | Error msg -> Expect.stringContains msg "Game" "error message names the colliding local name"
    }

// ── AT-RM5: mapping with no ClassIri is exempt from collision check ───────────

[<Tests>]
let at_rm5 =
    test "AT-RM5: duplicate LocalName is allowed when ClassIri is None" {
        let m1 = mkMapping "Ns.A.Game" None []
        let m2 = mkMapping "Ns.B.Game" None []
        let lock = { emptyLock with Mappings = [ m1; m2 ] }

        match ResolvedModel.build baseRegistry lock with
        | Error e -> failwith $"Expected Ok but got Error: {e}"
        | Ok model -> Expect.hasLength model.Resources 2 "two resources"
    }

// ── AT-RM6: prefixes and using propagated ────────────────────────────────────

[<Tests>]
let at_rm6 =
    test "AT-RM6: model carries registry Prefixes and Using" {
        let registry =
            { baseRegistry with
                Using = Set.ofList [ "schema" ] }

        let lock = emptyLock

        match ResolvedModel.build registry lock with
        | Error e -> failwith e
        | Ok model ->
            Expect.equal model.Prefixes prefixes "Prefixes forwarded"
            Expect.equal model.Using (Set.ofList [ "schema" ]) "Using forwarded"
    }

// ── AT-RM7: order follows lock.Mappings ──────────────────────────────────────

[<Tests>]
let at_rm7 =
    test "AT-RM7: Resources order matches lock.Mappings order" {
        let m1 = mkMapping "A.One" None []
        let m2 = mkMapping "B.Two" None []
        let m3 = mkMapping "C.Three" None []

        let lock =
            { emptyLock with
                Mappings = [ m1; m2; m3 ] }

        match ResolvedModel.build baseRegistry lock with
        | Error e -> failwith e
        | Ok model ->
            let names = model.Resources |> List.map (fun r -> r.FSharpType)
            Expect.equal names [ "A.One"; "B.Two"; "C.Three" ] "order preserved"
    }

// ── AT-RM-KW: reserved keyword local name returns Error ──────────────────────

[<Tests>]
let at_rm_kw =
    testList
        "AT-RM-KW: reserved keyword local names rejected for class-mapped resources"
        [ test "type named 'Type' (F# reserved) with ClassIri returns Error" {
              let mapping = mkMapping "Ns.Type" (Some "schema:Thing") []

              let lock =
                  { lockWithSchema with
                      Mappings = [ mapping ] }

              match ResolvedModel.build baseRegistry lock with
              | Ok _ -> failwith "Expected Error for reserved keyword local name"
              | Error msg ->
                  Expect.stringContains msg "Type" "error names the type"
                  Expect.stringContains msg "reserved keyword" "error mentions reserved keyword"
          }

          test "type named 'End' (F# reserved) with ClassIri returns Error" {
              let mapping = mkMapping "Ns.End" (Some "schema:End") []

              let lock =
                  { lockWithSchema with
                      Mappings = [ mapping ] }

              match ResolvedModel.build baseRegistry lock with
              | Ok _ -> failwith "Expected Error for reserved keyword local name 'End'"
              | Error msg -> Expect.stringContains msg "reserved keyword" "error mentions reserved keyword"
          }

          test "type with reserved keyword LocalName but no ClassIri is allowed (not emitted)" {
              let mapping = mkMapping "Ns.Type" None []

              let lock =
                  { emptyLock with
                      Mappings = [ mapping ] }

              match ResolvedModel.build baseRegistry lock with
              | Error e -> failwith $"Expected Ok for unmapped reserved-keyword type, got Error: {e}"
              | Ok model -> Expect.hasLength model.Resources 1 "resource present but not class-mapped"
          } ]

// ── AT-RM8: property — every Uri in model IsAbsoluteUri ──────────────────────

[<Tests>]
let at_rm8 =
    test "AT-RM8: every Uri in the model IsAbsoluteUri" {
        let gameType = "TicTacToe.Model.Game"

        let registry =
            { baseRegistry with
                EquivalentClasses = Map.ofList [ gameType, Uri "https://schema.org/Game" ]
                SeeAlso = Map.ofList [ gameType, [ Uri "https://www.wikidata.org/wiki/Q11907" ] ]
                FieldSeeAlso = Map.ofList [ (gameType, "Id"), [ Uri "https://schema.org/identifier" ] ] }

        let idField = mkField "Id" (Some "schema:identifier")
        let mapping = mkMapping gameType (Some "schema:Game") [ idField ]

        let lock =
            { lockWithSchema with
                Mappings = [ mapping ] }

        match ResolvedModel.build registry lock with
        | Error e -> failwith e
        | Ok model ->
            let allUris =
                model.Resources
                |> List.collect (fun r ->
                    let classUris = r.ClassIri |> Option.toList
                    let ecUris = r.EquivalentClass |> Option.toList
                    let saUris = r.SeeAlso

                    let fieldUris =
                        r.Fields |> List.collect (fun f -> (f.Iri |> Option.toList) @ f.SeeAlso)

                    classUris @ ecUris @ saUris @ fieldUris)

            Expect.isNonEmpty allUris "should have at least one URI to check"

            for uri in allUris do
                Expect.isTrue uri.IsAbsoluteUri $"URI must be absolute: {uri}"
    }

// ── AT-RM10: Excluded mappings are filtered out ──────────────────────────────

[<Tests>]
let at_rm10 =
    test "AT-RM10: Excluded mappings are absent from model.Resources" {
        let confirmed =
            { mkMapping "MyApp.Game" (Some "schema:Game") [] with
                Status = Confirmed }

        let excluded =
            { mkMapping "MyApp.Player" (Some "schema:Person") [] with
                Status = Excluded }

        let lock =
            { lockWithSchema with
                Mappings = [ confirmed; excluded ] }

        match ResolvedModel.build baseRegistry lock with
        | Error e -> failwith $"Expected Ok but got Error: {e}"
        | Ok model ->
            Expect.hasLength model.Resources 1 "only the Confirmed mapping is present"
            Expect.equal (model.Resources |> List.head).FSharpType "MyApp.Game" "confirmed type present"
    }

// ── AT-RM-EF: excluded fields do not leak into built resource ────────────────

[<Tests>]
let at_rm_ef =
    test "AT-RM-EF: Excluded fields on a Confirmed mapping are absent from built resource.Fields" {
        let gameType = "MyApp.Game"

        let confirmedField: FieldMapping =
            { Name = "Name"
              Iri = Some "schema:name"
              Confidence = 1.0
              Source = Convention
              Status = Confirmed }

        let excludedField: FieldMapping =
            { Name = "Id"
              Iri = Some "schema:Play"
              Confidence = 0.5
              Source = Convention
              Status = Excluded }

        let mapping =
            { mkMapping gameType (Some "schema:Game") [ confirmedField; excludedField ] with
                Status = Confirmed }

        let lock =
            { lockWithSchema with
                Mappings = [ mapping ] }

        match ResolvedModel.build baseRegistry lock with
        | Error e -> failwith $"Expected Ok but got Error: {e}"
        | Ok model ->
            let res = model.Resources |> List.head
            let fieldIris = res.Fields |> List.choose (fun f -> f.Iri) |> List.map string
            Expect.hasLength res.Fields 1 "only the Confirmed field present"
            Expect.equal (res.Fields |> List.head).Name "Name" "confirmed field present"

            Expect.isFalse
                (fieldIris |> List.exists (fun s -> s.Contains "Play"))
                "schema:Play (excluded) must not appear"
    }

// ── AT-RM-CI: confirmed case with Iri=None is a lock inconsistency → Error ────

[<Tests>]
let at_rm_ci =
    test "AT-RM-CI: confirmed union case with Iri=None returns Error naming the case" {
        let lock: LockFile =
            { SchemaVersion = 1
              Generated = DateTimeOffset.UtcNow
              Vocabularies =
                Map.ofList
                    [ "ex",
                      { Uri = "http://example.com/vocab#"
                        FetchedAt = DateTimeOffset.UtcNow
                        Hash = "test" } ]
              Mappings =
                [ { FSharpType = "MyApp.Color"
                    Iri = Some "ex:Color"
                    Confidence = 1.0
                    Source = Convention
                    Status = Confirmed
                    Alternates = []
                    Shape =
                      MappingShape.Union
                          [ { Name = "Red"
                              Iri = None
                              Confidence = 0.0
                              Source = Convention
                              Status = Confirmed
                              Payload = [] } ] } ] }

        let registry =
            { baseRegistry with
                Prefixes = Map.ofList [ "ex", Uri "http://example.com/vocab#" ] }

        match ResolvedModel.build registry lock with
        | Ok _ -> failwith "Expected Error for confirmed case with Iri=None (lock inconsistency)"
        | Error msg ->
            Expect.stringContains msg "Red" "error mentions the case name"
            Expect.stringContains msg "confirmed" "error mentions confirmed status"
    }

// ── AT-RM-ET: enrichTypes fills type/cardinality ────────────────────────────

// Real TypeName strings produced by Extractor.normalizeTypeName (verified by probe):
//   scalar int         → "int"
//   string option      → "string option"
//   int list           → "int list"
//   int list option    → "int list option"

let private enrichFsharpType = "MyApp.DataRecord"

let private enrichFields =
    [ mkField "Count" (Some "schema:numberOfItems")
      mkField "Note" (Some "schema:description")
      mkField "Scores" (Some "schema:value")
      mkField "MaybeScores" (Some "schema:value") ]

let private enrichMapping =
    mkMapping enrichFsharpType (Some "schema:Thing") enrichFields

let private enrichLock =
    { lockWithSchema with
        Mappings = [ enrichMapping ] }

let private mkFieldInfo name typeName : FieldInfo =
    { Name = name
      TypeName = typeName
      Attributes = Map.empty
      DocComment = None }

let private enrichTypeInfo: TypeInfo =
    { FullName = enrichFsharpType
      Namespace = "MyApp"
      LocalName = "DataRecord"
      Shape =
        TypeShape.Record
            [ mkFieldInfo "Count" "int"
              mkFieldInfo "Note" "string option"
              mkFieldInfo "Scores" "int list"
              mkFieldInfo "MaybeScores" "int list option" ]
      Attributes = Map.empty
      DocComment = None }

let private typesByName = Map.ofList [ enrichFsharpType, enrichTypeInfo ]

let private okOrFail (r: Result<ResolvedModel, string>) : ResolvedModel =
    match r with
    | Ok m -> m
    | Error e -> failwith $"Expected Ok but got Error: {e}"

let private typesByNameWithEmpty =
    let tiWithEmpty =
        { enrichTypeInfo with
            Shape = TypeShape.Record [ mkFieldInfo "Count" "" ] }

    Map.ofList [ enrichFsharpType, tiWithEmpty ]

[<Tests>]
let at_rm_et =
    testList
        "AT-RM-ET: enrichTypes fills type/cardinality from TypeInfo"
        [ test "enrichTypes fills unwrapped type + cardinality for a scalar int field" {
              let enriched =
                  ResolvedModel.build baseRegistry enrichLock
                  |> Result.bind (ResolvedModel.enrichTypes typesByName)
                  |> okOrFail

              let f =
                  (List.head enriched.Resources).Fields |> List.find (fun f -> f.Name = "Count")

              Expect.equal f.TypeName "int" "unwrapped type"
              Expect.isFalse f.IsOptional "not optional"
              Expect.isFalse f.IsCollection "not collection"
          }

          test "enrichTypes fills option flag for string option field" {
              let enriched =
                  ResolvedModel.build baseRegistry enrichLock
                  |> Result.bind (ResolvedModel.enrichTypes typesByName)
                  |> okOrFail

              let f =
                  (List.head enriched.Resources).Fields |> List.find (fun f -> f.Name = "Note")

              Expect.equal f.TypeName "string" "unwrapped inner type"
              Expect.isTrue f.IsOptional "is optional"
              Expect.isFalse f.IsCollection "not collection"
          }

          test "enrichTypes fills collection flag for int list field" {
              let enriched =
                  ResolvedModel.build baseRegistry enrichLock
                  |> Result.bind (ResolvedModel.enrichTypes typesByName)
                  |> okOrFail

              let f =
                  (List.head enriched.Resources).Fields |> List.find (fun f -> f.Name = "Scores")

              Expect.equal f.TypeName "int" "unwrapped inner type"
              Expect.isFalse f.IsOptional "not optional"
              Expect.isTrue f.IsCollection "is collection"
          }

          test "enrichTypes fills both flags for int list option field" {
              let enriched =
                  ResolvedModel.build baseRegistry enrichLock
                  |> Result.bind (ResolvedModel.enrichTypes typesByName)
                  |> okOrFail

              let f =
                  (List.head enriched.Resources).Fields
                  |> List.find (fun f -> f.Name = "MaybeScores")

              Expect.equal f.TypeName "int" "unwrapped inner type"
              Expect.isTrue f.IsOptional "is optional"
              Expect.isTrue f.IsCollection "is collection"
          }

          test "enrichTypes Errors on a present-but-empty TypeName and names the field" {
              match
                  ResolvedModel.build baseRegistry enrichLock
                  |> Result.bind (ResolvedModel.enrichTypes typesByNameWithEmpty)
              with
              | Error msg ->
                  Expect.stringContains msg "Count" "error names the offending field"
                  Expect.stringContains msg "TypeName must not be empty" "error preserves inner message"
              | Ok _ -> failtest "expected Error for empty TypeName"
          }

          test "build alone leaves new fields at defaults" {
              let m = ResolvedModel.build baseRegistry enrichLock |> okOrFail

              let f = (List.head m.Resources).Fields |> List.head
              Expect.equal f.TypeName "" "default empty until enriched"
              Expect.isFalse f.IsOptional "default false until enriched"
              Expect.isFalse f.IsCollection "default false until enriched"
          } ]

// ── AT-RM9: lock IRI self-contained resolution ────────────────────────────────

[<Tests>]
let at_rm9 =
    testList
        "AT-RM9: lock IRI resolution is self-contained from lock.Vocabularies"
        [ test "lock IRI resolves from lock.Vocabularies — not registry.Prefixes" {
              let mapping = mkMapping "Ns.Thing" (Some "schema:Thing") []

              // Lock carries schema: but registry has NO prefixes at all.
              let lock =
                  { lockWithSchema with
                      Mappings = [ mapping ] }

              let registryNoPrefixes = VocabularyRegistry.empty

              match ResolvedModel.build registryNoPrefixes lock with
              | Error e -> failwith $"Expected Ok when schema: is in lock.Vocabularies: {e}"
              | Ok model ->
                  let res = model.Resources |> List.head
                  Expect.equal res.ClassIri (Some(Uri "https://schema.org/Thing")) "IRI resolved from lock vocab"
          }

          test "lock IRI with prefix absent from lock.Vocabularies returns Error (not silent)" {
              let mapping = mkMapping "Ns.Widget" (Some "myns:Widget") []

              // myns: is absent from lock.Vocabularies — even if registry carries it, still Error.
              let lock =
                  { emptyLock with
                      Mappings = [ mapping ] }

              let registryWithMyns =
                  { VocabularyRegistry.empty with
                      Prefixes = Map.ofList [ "myns", Uri "http://my.ns/" ] }

              match ResolvedModel.build registryWithMyns lock with
              | Ok _ -> failwith "Expected Error: prefix 'myns' not in lock.Vocabularies"
              | Error msg -> Expect.stringContains msg "myns" "error names the unresolvable prefix"
          } ]
