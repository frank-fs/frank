namespace Frank.Provenance.Tests

open System
open Frank.Semantic
open Frank.Provenance

// Fixture for resolveFromType tests — dotted names stay as-is (no resolveClrName call).
type FakeGeneratedProvenance() =
    static member val provClasses: (string * (string * string)) list =
        [ "MyApp.OrderPlaced", ("Activity", "https://schema.org/OrderAction")
          "MyApp.Ping", ("Activity", "") ] with get

    static member val knownNamespaces: string[] = [| "https://schema.org/" |] with get

// Fixture for resolveGeneratedConfig — must be named "GeneratedProvenance" so that
// findSinglePublicType "GeneratedProvenance" finds exactly one match.
// provClasses carries the F#/dotted name; resolveClrName must rewrite it to CLR name (+).
type GeneratedProvenance() =
    static member val provClasses: (string * (string * string)) list =
        [ "Frank.Provenance.Tests.ResolverTests.CapstoneLike.MovePlaced", ("Activity", "https://schema.org/MoveAction") ] with get

    static member val knownNamespaces: string[] = [||] with get

module ResolverTests =

    open Expecto

    // CapstoneLike is a nested module — MovePlaced compiles to a CLR nested type.
    // typeof<CapstoneLike.MovePlaced>.FullName contains '+' delimiters, not '.'.
    // If resolveClrName were a no-op, cfg.ProvClasses would be keyed by the dotted name
    // and Map.containsKey clrName would return false.  This test proves rewriting occurs.
    module CapstoneLike =
        type MovePlaced = { Pos: int }

    [<Tests>]
    let tests =
        testList
            "GeneratedProvenanceResolver"
            [ test "resolveFromType maps case name + IRI, empty IRI -> None" {
                  match GeneratedProvenanceResolver.resolveFromType typeof<FakeGeneratedProvenance> with
                  | Ok cfg ->
                      Expect.equal
                          cfg.ProvClasses.["MyApp.OrderPlaced"]
                          (ProvOClass.Activity, Some(Uri "https://schema.org/OrderAction"))
                          "typed entry"

                      Expect.equal cfg.ProvClasses.["MyApp.Ping"] (ProvOClass.Activity, None) "empty IRI -> None"
                  | Error e -> failtestf "expected Ok, got %s" e
              }

              test "resolveGeneratedConfig rewrites dotted F# name to CLR nested-type name" {
                  let asm = typeof<CapstoneLike.MovePlaced>.Assembly
                  let clrName = typeof<CapstoneLike.MovePlaced>.FullName
                  let dottedName = clrName.Replace('+', '.')

                  match GeneratedProvenanceResolver.resolveGeneratedConfig [| asm |] with
                  | Ok cfg ->
                      Expect.isTrue
                          (Map.containsKey clrName cfg.ProvClasses)
                          $"CLR name '{clrName}' must be present after resolveClrName rewrite"

                      Expect.isFalse
                          (Map.containsKey dottedName cfg.ProvClasses)
                          $"dotted name '{dottedName}' must NOT be present (proves rewrite happened)"
                  | Error e -> failtestf "expected Ok, got %s" e
              } ]
