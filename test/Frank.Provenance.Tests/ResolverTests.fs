module Frank.Provenance.Tests.ResolverTests

open Expecto
open Frank.Semantic
open Frank.Provenance

type FakeGeneratedProvenance() =
    static member val provClasses: (string * (string * string)) list =
        [ "MyApp.OrderPlaced", ("Activity", "https://schema.org/OrderAction")
          "MyApp.Ping", ("Activity", "") ] with get
    static member val knownNamespaces: string[] = [| "https://schema.org/" |] with get

[<Tests>]
let tests =
    testList "GeneratedProvenanceResolver" [
        test "resolveFromType maps case name + IRI, empty IRI -> None" {
            match GeneratedProvenanceResolver.resolveFromType typeof<FakeGeneratedProvenance> with
            | Ok cfg ->
                Expect.equal
                    cfg.ProvClasses.["MyApp.OrderPlaced"]
                    (ProvOClass.Activity, Some(System.Uri "https://schema.org/OrderAction"))
                    "typed entry"

                Expect.equal cfg.ProvClasses.["MyApp.Ping"] (ProvOClass.Activity, None) "empty IRI -> None"
            | Error e -> failtestf "expected Ok, got %s" e
        }
    ]
