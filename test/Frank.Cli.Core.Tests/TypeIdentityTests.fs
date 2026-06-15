module Frank.Cli.Core.Tests.TypeIdentityTests

open System
open System.IO
open System.Reflection
open Expecto
open Frank.Semantic
open Frank.Cli.Core

// ── Assembly path helpers ─────────────────────────────────────────────────────

let private frankSemanticDll () =
    Assembly.GetAssembly(typeof<VocabularyRegistry>).Location

let private fsharpCoreDll () =
    Assembly.GetAssembly(typeof<int list>).Location

// ── Helper: write source to temp .fsx and run evalRegistry ───────────────────

let private withTempVocab (source: string) (bindingName: string) (f: Result<VocabularyRegistry, string> -> unit) =
    let tmpDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"))
    Directory.CreateDirectory(tmpDir) |> ignore

    try
        let src = Path.Combine(tmpDir, "Vocabulary.fsx")
        File.WriteAllText(src, source)

        let result =
            VocabularyEvaluator.evalRegistry [ frankSemanticDll (); fsharpCoreDll () ] [ src ] bindingName

        f result
    finally
        Directory.Delete(tmpDir, true)

// ─────────────────────────────────────────────────────────────────────────────
// Group A: CHARACTERIZATION — must stay GREEN through the refactor
// ─────────────────────────────────────────────────────────────────────────────

// Simple record in a module — FSI script style, matching the existing test pattern.
// Types declared in the same module as the registry; names are realistic.
let private simpleRecordVocabSrc =
    """
module ProbeVocab

open Frank.Semantic

type Game = { Id: int; Name: string }

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        equivalentClass typeof<Game> "schema:Game"
        seeAlso typeof<Game> "schema:VideoGame"
    }
"""

[<Tests>]
let groupACharacterizationTests =
    testList
        "TypeIdentity - A: characterization (must stay GREEN)"
        [ test "A1: evalRegistry returns Ok for simple record vocab" {
              withTempVocab simpleRecordVocabSrc "ProbeVocab.registry" (fun result ->
                  Expect.isOk result "evalRegistry should succeed for simple record vocab")
          }

          test "A2: schema prefix URI is https://schema.org/" {
              withTempVocab simpleRecordVocabSrc "ProbeVocab.registry" (fun result ->
                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  let schemaUri = reg.Prefixes |> Map.tryFind "schema"
                  Expect.isSome schemaUri "schema prefix present"
                  Expect.equal schemaUri.Value.AbsoluteUri "https://schema.org/" "schema URI")
          }

          test "A3: EquivalentClasses contains exactly one entry" {
              withTempVocab simpleRecordVocabSrc "ProbeVocab.registry" (fun result ->
                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  Expect.isNonEmpty reg.EquivalentClasses "EquivalentClasses must have one entry"
                  Expect.equal reg.EquivalentClasses.Count 1 "exactly one EquivalentClass entry")
          }

          test "A4: EquivalentClasses value is schema:Game URI" {
              withTempVocab simpleRecordVocabSrc "ProbeVocab.registry" (fun result ->
                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  let values = reg.EquivalentClasses |> Map.toList |> List.map snd
                  let hasGame = values |> List.exists (fun u -> u.AbsoluteUri.Contains("Game"))
                  Expect.isTrue hasGame "EquivalentClasses value contains 'Game'")
          }

          test "A5: SeeAlso value points to schema:VideoGame" {
              withTempVocab simpleRecordVocabSrc "ProbeVocab.registry" (fun result ->
                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  Expect.isNonEmpty reg.SeeAlso "SeeAlso must have one entry"
                  let allUris = reg.SeeAlso |> Map.toList |> List.collect snd

                  let hasVideoGame =
                      allUris |> List.exists (fun u -> u.AbsoluteUri.Contains("VideoGame"))

                  Expect.isTrue hasVideoGame "SeeAlso URI contains 'VideoGame'")
          } ]

// ─────────────────────────────────────────────────────────────────────────────
// Group B: IDENTITY CONTRACT
// FSI normalizes type keys via normalizeFsiKey:
//   "FSI_NNNN.ProbeVocab+Game" → "ProbeVocab.Game"
// FCS TryFullName for the same source gives "ProbeVocab.Game".
// B1/B2/B4a/B4b: expected GREEN today (normalizeFsiKey covers simple cases).
// B3/B4c: expected RED today — generic type key form may diverge between FSI
//   reflection and FCS TryFullName; locking in the exact required string.
// ─────────────────────────────────────────────────────────────────────────────

let private recordIdentitySrc =
    """
module ProbeVocab

open Frank.Semantic

type Game = { Id: int; Name: string }

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        equivalentClass typeof<Game> "schema:Game"
    }
"""

// Nested module — type ends up as ProbeVocab.Moves.Move in FCS TryFullName
// and "FSI_NNNN.ProbeVocab+Moves+Move" → normalizeFsiKey → "ProbeVocab.Moves.Move"
let private nestedDuIdentitySrc =
    """
module ProbeVocab

open Frank.Semantic

module Moves =
    type Move =
        | Rock
        | Paper
        | Scissors

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        equivalentClass typeof<Moves.Move> "schema:Move"
    }
"""

// Generic definition — typedefof<Holder<_>> inside FSI gives
// "FSI_NNNN.ProbeVocab+Holder`1" → normalizeFsiKey → "ProbeVocab.Holder`1"
// FCS TryFullName → "ProbeVocab.Holder`1"
// RED until FCS-identity refactor (Unit 1) if normalization diverges.
let private genericIdentitySrc =
    """
module ProbeVocab

open Frank.Semantic

type Holder<'T> = { Value: 'T; Tag: string }

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        equivalentClass typedefof<Holder<obj>> "schema:Holder"
    }
"""

[<Tests>]
let groupBIdentityTests =
    testList
        "TypeIdentity - B: identity contract (refactor target)"
        [ test "B1: record Game key is exactly 'ProbeVocab.Game' (no FSI_, no +, no assembly qual)" {
              withTempVocab recordIdentitySrc "ProbeVocab.registry" (fun result ->
                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  let key = reg.EquivalentClasses |> Map.toList |> List.head |> fst
                  Expect.equal key "ProbeVocab.Game" "key must be dotted FullName, no FSI_ prefix")
          }

          test "B2: nested-module DU Move key is exactly 'ProbeVocab.Moves.Move'" {
              withTempVocab nestedDuIdentitySrc "ProbeVocab.registry" (fun result ->
                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  let key = reg.EquivalentClasses |> Map.toList |> List.head |> fst
                  Expect.equal key "ProbeVocab.Moves.Move" "nested module DU key must use dots")
          }

          // RED until FCS-identity refactor (Unit 1).
          // FSI: typedefof<Holder<obj>>.FullName → "FSI_NNNN.ProbeVocab+Holder`1"
          // normalizeFsiKey → "ProbeVocab.Holder`1"
          // FCS TryFullName → "ProbeVocab.Holder`1"
          // If normalizeFsiKey already produces this form, this will be GREEN — locking in the value.
          // If it diverges (e.g. includes type args or assembly refs), this pins the RED gap.
          test "B3 [RED until FCS-identity]: generic Holder key is exactly 'ProbeVocab.Holder`1'" {
              withTempVocab genericIdentitySrc "ProbeVocab.registry" (fun result ->
                  let reg = Expect.wantOk result "evalRegistry should succeed"
                  let key = reg.EquivalentClasses |> Map.toList |> List.head |> fst
                  Expect.equal key "ProbeVocab.Holder`1" "generic key must be backtick-arity, no type args")
          }

          test "B4a: evalRegistry key for record Game equals Extractor FullName" {
              let extractorSrc =
                  """
module ProbeVocab
type Game = { Id: int; Name: string }
"""

              let extractorResult = Extractor.extractTypeInfosFromSource extractorSrc
              let types = Expect.wantOk extractorResult "extractor should succeed"
              let extractorKey = (types |> List.find (fun t -> t.LocalName = "Game")).FullName

              withTempVocab recordIdentitySrc "ProbeVocab.registry" (fun evalResult ->
                  let reg = Expect.wantOk evalResult "evalRegistry should succeed"
                  let evalKey = reg.EquivalentClasses |> Map.toList |> List.head |> fst

                  Expect.equal
                      evalKey
                      extractorKey
                      $"evalRegistry key '{evalKey}' must equal Extractor FullName '{extractorKey}'")
          }

          test "B4b: evalRegistry key for nested DU Move equals Extractor FullName" {
              let extractorSrc =
                  """
module ProbeVocab

module Moves =
    type Move =
        | Rock
        | Paper
        | Scissors
"""

              let extractorResult = Extractor.extractTypeInfosFromSource extractorSrc
              let types = Expect.wantOk extractorResult "extractor should succeed"
              let extractorKey = (types |> List.find (fun t -> t.LocalName = "Move")).FullName

              withTempVocab nestedDuIdentitySrc "ProbeVocab.registry" (fun evalResult ->
                  let reg = Expect.wantOk evalResult "evalRegistry should succeed"
                  let evalKey = reg.EquivalentClasses |> Map.toList |> List.head |> fst

                  Expect.equal
                      evalKey
                      extractorKey
                      $"evalRegistry key '{evalKey}' must equal Extractor FullName '{extractorKey}'")
          }

          // RED until FCS-identity refactor (Unit 1).
          // Generic entity: FCS TryFullName → "ProbeVocab.Holder`1" (logical name with arity).
          // FSI: typedefof<Holder<obj>>.FullName → may include type args or assembly-qualified noise.
          // This test locks in that both sides agree on "ProbeVocab.Holder`1".
          test "B4c [RED until FCS-identity]: evalRegistry key for generic Holder equals Extractor FullName" {
              let extractorSrc =
                  """
module ProbeVocab
type Holder<'T> = { Value: 'T; Tag: string }
"""

              let extractorResult = Extractor.extractTypeInfosFromSource extractorSrc
              let types = Expect.wantOk extractorResult "extractor should succeed"

              let extractorKey =
                  (types |> List.find (fun t -> t.LocalName.StartsWith("Holder"))).FullName

              withTempVocab genericIdentitySrc "ProbeVocab.registry" (fun evalResult ->
                  let reg = Expect.wantOk evalResult "evalRegistry should succeed"
                  let evalKey = reg.EquivalentClasses |> Map.toList |> List.head |> fst

                  Expect.equal
                      evalKey
                      extractorKey
                      $"evalRegistry key '{evalKey}' must equal Extractor FullName '{extractorKey}'")
          } ]

// ─────────────────────────────────────────────────────────────────────────────
// Group C: LOUD-FAIL — RED today; refactor must make these pass with Error
// ─────────────────────────────────────────────────────────────────────────────

// C1: type abbreviation. typeof<GameState> erases to IReadOnlyDictionary<int,int>
// at reflection time. The abbreviation name "GameState" is permanently lost.
// Today: returns Ok with a garbage BCL key containing assembly-qualified names.
// After refactor: must return Error naming the unmappable abbreviation.
// RED until FCS-identity refactor (Unit 1).
let private typeAbbreviationSrc =
    """
module ProbeVocab

open Frank.Semantic
open System.Collections.Generic

type GameState = IReadOnlyDictionary<int, int>

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        equivalentClass typeof<GameState> "schema:GameState"
    }
"""

// C2: raw BCL/constructed generic. typeof<List<int>> has a real CLR FullName
// but there is no corresponding declared type in the source.
// Today: returns Ok with the full assembly-qualified BCL key.
// After refactor: must return Error.
// RED until FCS-identity refactor (Unit 1).
let private bclTypeSrc =
    """
module ProbeVocab

open Frank.Semantic

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        equivalentClass typeof<System.Collections.Generic.List<int>> "schema:List"
    }
"""

[<Tests>]
let groupCLoudFailTests =
    testList
        "TypeIdentity - C: loud-fail contracts (RED today; refactor must satisfy)"
        [ // RED until FCS-identity refactor (Unit 1).
          // Today: returns Ok with erased BCL key like
          //   "System.Collections.Generic.IReadOnlyDictionary`2[[System.Int32,...],[System.Int32,...]]"
          // The refactor must return Error because the abbreviation name is unrecoverable.
          test "C1 [RED until FCS-identity]: type abbreviation GameState → Error (name erased at reflection)" {
              withTempVocab typeAbbreviationSrc "ProbeVocab.registry" (fun result ->
                  Expect.isError
                      result
                      "type abbreviation must return Error — abbreviation name is lost at reflection time")
          }

          // RED until FCS-identity refactor (Unit 1).
          // Today: returns Ok with the assembly-qualified BCL key.
          // The refactor must return Error because there is no declared source type to match.
          test "C2 [RED until FCS-identity]: BCL constructed generic List<int> → Error (no declared source type)" {
              withTempVocab bclTypeSrc "ProbeVocab.registry" (fun result ->
                  Expect.isError result "BCL constructed generic must return Error — no declared source type to match")
          } ]

// ─────────────────────────────────────────────────────────────────────────────
// Group D: include support through evalRegistry (Task A)
// ─────────────────────────────────────────────────────────────────────────────

let private includeTwoBindingsSrc =
    """
module IncludeVocab

open Frank.Semantic

type Order = { Id: int; Total: decimal }
type Product = { Name: string }

let base' =
    vocabulary {
        prefix "schema" "https://schema.org/"
        equivalentClass typeof<Order> "schema:Order"
    }

let registry =
    vocabulary {
        prefix "ex" "http://example.com/vocab#"
        equivalentClass typeof<Product> "ex:Product"
        ``include`` base'
    }
"""

let private includeConflictSrc =
    """
module IncludeConflict

open Frank.Semantic

type Order = { Id: int }

let base' =
    vocabulary {
        prefix "schema" "https://schema.org/"
        equivalentClass typeof<Order> "schema:Order"
    }

let registry =
    vocabulary {
        prefix "ex" "http://example.com/vocab#"
        equivalentClass typeof<Order> "ex:DifferentThing"
        ``include`` base'
    }
"""

let private includeInlineSrc =
    """
module IncludeInline

open Frank.Semantic

type Order = { Id: int }
type Product = { Name: string }

let extra =
    vocabulary {
        prefix "ex" "http://example.com/"
        equivalentClass typeof<Product> "ex:Product"
    }

let registry =
    vocabulary {
        prefix "schema" "https://schema.org/"
        equivalentClass typeof<Order> "schema:Order"
        ``include`` extra
    }
"""

[<Tests>]
let groupDIncludeTests =
    testList
        "TypeIdentity - D: include through evalRegistry"
        [ test "D1: include of another binding merges both registries" {
              withTempVocab includeTwoBindingsSrc "IncludeVocab.registry" (fun result ->
                  let reg = Expect.wantOk result "evalRegistry with include should succeed"
                  Expect.isTrue (reg.Prefixes.ContainsKey "schema") "schema prefix from included binding"
                  Expect.isTrue (reg.Prefixes.ContainsKey "ex") "ex prefix from outer registry"
                  let keys = reg.EquivalentClasses |> Map.toList |> List.map fst
                  Expect.equal keys.Length 2 "two EquivalentClass entries (Order + Product)")
          }

          test "D2: include conflict → evalRegistry returns Error" {
              withTempVocab includeConflictSrc "IncludeConflict.registry" (fun result ->
                  Expect.isError result "conflicting EquivalentClass via include must return Error")
          }

          test "D3: include of sibling binding in same module merges prefixes and equivalentClasses" {
              withTempVocab includeInlineSrc "IncludeInline.registry" (fun result ->
                  let reg = Expect.wantOk result "sibling include should succeed"
                  Expect.isTrue (reg.Prefixes.ContainsKey "schema") "schema present"
                  Expect.isTrue (reg.Prefixes.ContainsKey "ex") "ex present from sibling include"
                  Expect.equal reg.EquivalentClasses.Count 2 "both types mapped")
          } ]
