module Frank.Semantic.Tests.ResolvedModelTests

open System
open Expecto
open Frank.Semantic
open Frank.Semantic.LockFile

// ── Helpers ──────────────────────────────────────────────────────────────────

let private schemaPrefix = Uri "https://schema.org/"
let private exPrefix = Uri "http://example.com/vocab#"

let private prefixes = Map.ofList [ "schema", schemaPrefix; "ex", exPrefix ]

let private emptyLock: LockFile =
    { SchemaVersion = 1
      Generated = DateTimeOffset.UtcNow
      Vocabularies = Map.empty
      Mappings = [] }

let private mkMapping fsharpType iri fields : Mapping =
    { FSharpType = fsharpType
      Iri = iri
      Confidence = 1.0
      Source = Convention
      Status = Confirmed
      Alternates = []
      Fields = fields }

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
            { emptyLock with
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
        let lock = { emptyLock with Mappings = [ m1; m2 ] }

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
            { emptyLock with
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
