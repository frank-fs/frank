namespace Frank.Provenance.Tests

open System
open Frank.Semantic
open Frank.Provenance

// Fixture for GAP 2a: empty provClasses — resolver must return Ok with Map.empty, no crash.
type EmptyGeneratedProvenance() =
    static member val provClasses: (string * (string * string)) list = [] with get
    static member val knownNamespaces: string[] = [||] with get

// Fixture for resolveFromType tests — dotted names stay as-is (no resolveClrName call).
type FakeGeneratedProvenance() =
    static member val provClasses: (string * (string * string)) list =
        [ "MyApp.OrderPlaced", ("Activity", "https://schema.org/OrderAction")
          "MyApp.Ping", ("Activity", "") ] with get

    static member val knownNamespaces: string[] = [| "https://schema.org/" |] with get

// Fixture for resolveGeneratedConfig — must be named "GeneratedProvenance" so that
// findSinglePublicType "GeneratedProvenance" finds exactly one match.
// provClasses carries the F#/dotted name as emitted by the code generator.
// No key rewriting occurs; the middleware normalises the runtime CLR name at lookup time.
type GeneratedProvenance() =
    static member val provClasses: (string * (string * string)) list =
        [ "Frank.Provenance.Tests.ResolverTests.CapstoneLike.MovePlaced", ("Activity", "https://schema.org/MoveAction") ] with get

    static member val knownNamespaces: string[] = [||] with get

module ResolverTests =

    open Expecto

    // CapstoneLike is a nested module — MovePlaced compiles to a CLR nested type.
    // typeof<CapstoneLike.MovePlaced>.FullName contains '+' delimiters, not '.'.
    // The generator emits dotted names; the resolver keeps them; the middleware normalises at lookup.
    module CapstoneLike =
        type MovePlaced = { Pos: int }

    [<Tests>]
    let tests =
        testList
            "GeneratedProvenanceResolver"
            [ test "resolveFromType on empty provClasses returns Ok with Map.empty — no crash (GAP 2a)" {
                  match GeneratedProvenanceResolver.resolveFromType typeof<EmptyGeneratedProvenance> with
                  | Ok cfg ->
                      Expect.isTrue (Map.isEmpty cfg.ProvClasses) "ProvClasses is empty — no entries"
                      Expect.equal cfg.KnownNamespaces [||] "KnownNamespaces is empty array"
                  | Error e -> failtestf "expected Ok, got %s" e
              }

              test "resolveFromType maps case name + IRI, empty IRI -> None" {
                  match GeneratedProvenanceResolver.resolveFromType typeof<FakeGeneratedProvenance> with
                  | Ok cfg ->
                      Expect.equal
                          cfg.ProvClasses.["MyApp.OrderPlaced"]
                          (ProvOClass.Activity, Some(Uri "https://schema.org/OrderAction"))
                          "typed entry"

                      Expect.equal cfg.ProvClasses.["MyApp.Ping"] (ProvOClass.Activity, None) "empty IRI -> None"
                  | Error e -> failtestf "expected Ok, got %s" e
              }

              test "resolveGeneratedConfig keeps dotted key as generated; middleware normalises at lookup" {
                  let asm = typeof<CapstoneLike.MovePlaced>.Assembly
                  let clrName = typeof<CapstoneLike.MovePlaced>.FullName
                  let dottedName = clrName.Replace('+', '.')

                  match GeneratedProvenanceResolver.resolveGeneratedConfig [| asm |] with
                  | Ok cfg ->
                      Expect.isTrue
                          (Map.containsKey dottedName cfg.ProvClasses)
                          $"dotted key '{dottedName}' must be present as generated"

                      Expect.isFalse
                          (Map.containsKey clrName cfg.ProvClasses)
                          $"CLR nested key '{clrName}' must NOT be present (no scan/rewrite in resolver)"
                  | Error e -> failtestf "expected Ok, got %s" e
              } ]
