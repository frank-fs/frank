module Frank.Semantic.Tests.VocabularyRegistryTests

open System
open Expecto
open Frank.Semantic

// Domain types used in tests.
type Order = { Id: int; Total: decimal }
type TicTacToeGame = { Board: string }
type OrderPlaced = { OrderId: int }
type Address = { ZipCode: string }
type Product = { Name: string }

// AT1: CE evaluates to a VocabularyRegistry value.
[<Tests>]
let at1 =
    test "AT1: vocabulary CE produces a VocabularyRegistry with Prefixes populated" {
        let r = vocabulary { prefix "schema" "https://schema.org/" }
        Expect.equal r.Prefixes.["schema"] (Uri "https://schema.org/") "schema prefix"
    }

// AT2: All operations write to their corresponding fields.
[<Tests>]
let at2 =
    test "AT2: all CE operations populate their corresponding registry fields" {
        let r =
            vocabulary {
                prefix "schema" "https://schema.org/"
                prefix "wikidata" "https://www.wikidata.org/wiki/"
                prefix "ex" "http://example.com/vocab#"
                using "ex"
                using "schema"
                equivalentClass typeof<Order> "schema:Order"
                seeAlso typeof<TicTacToeGame> "wikidata:Q11907"
                fieldSeeAlso typeof<Order> "LineItems" "schema:orderedItem"
                provClass typeof<OrderPlaced> ProvOClass.Activity
                constrainPattern typeof<Address> "ZipCode" @"^\d{5}$"
            }

        Expect.equal r.Prefixes.["schema"] (Uri "https://schema.org/") "schema prefix"
        Expect.equal r.Prefixes.["wikidata"] (Uri "https://www.wikidata.org/wiki/") "wikidata prefix"
        Expect.equal r.Prefixes.["ex"] (Uri "http://example.com/vocab#") "ex prefix"
        Expect.isTrue (Set.contains "ex" r.Using) "ex in Using"
        Expect.isTrue (Set.contains "schema" r.Using) "schema in Using"

        Expect.equal
            (VocabularyRegistry.tryFindEquivalentClass typeof<Order> r)
            (Some(Uri "https://schema.org/Order"))
            "EquivalentClass for Order"

        Expect.equal
            (VocabularyRegistry.tryFindSeeAlso typeof<TicTacToeGame> r)
            (Some [ Uri "https://www.wikidata.org/wiki/Q11907" ])
            "SeeAlso for TicTacToeGame"

        Expect.equal
            (VocabularyRegistry.tryFindFieldSeeAlso typeof<Order> "LineItems" r)
            (Some [ Uri "https://schema.org/orderedItem" ])
            "FieldSeeAlso for Order.LineItems"

        Expect.equal
            (VocabularyRegistry.tryFindProvClass typeof<OrderPlaced> r)
            (Some ProvOClass.Activity)
            "ProvClass for OrderPlaced"

        Expect.equal
            (VocabularyRegistry.tryFindConstraintPattern typeof<Address> "ZipCode" r)
            (Some @"^\d{5}$")
            "ConstraintPattern for Address.ZipCode"
    }

// AT3: include composes registries.
[<Tests>]
let at3 =
    testList
        "AT3: include"
        [ test "include deep-unions non-conflicting registries" {
              let base' =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      equivalentClass typeof<Order> "schema:Order"
                  }

              let other =
                  vocabulary {
                      prefix "ex" "http://example.com/vocab#"
                      equivalentClass typeof<Product> "ex:Product"
                  }

              let merged =
                  vocabulary {
                      ``include`` base'
                      ``include`` other
                  }

              Expect.equal merged.Prefixes.["schema"] (Uri "https://schema.org/") "schema prefix preserved"
              Expect.equal merged.Prefixes.["ex"] (Uri "http://example.com/vocab#") "ex prefix included"

              Expect.isSome (VocabularyRegistry.tryFindEquivalentClass typeof<Order> merged) "Order EC"
              Expect.isSome (VocabularyRegistry.tryFindEquivalentClass typeof<Product> merged) "Product EC"
          }

          test "include absorbs identical-value duplicates silently" {
              let a =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      equivalentClass typeof<Order> "schema:Order"
                  }

              let b =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      equivalentClass typeof<Order> "schema:Order"
                  }

              let merged =
                  vocabulary {
                      ``include`` a
                      ``include`` b
                  }

              Expect.equal merged.Prefixes.["schema"] (Uri "https://schema.org/") "identical prefix absorbed"
          }

          test "include raises on conflicting prefix values" {
              let a = vocabulary { prefix "schema" "https://schema.org/" }
              let b = vocabulary { prefix "schema" "https://DIFFERENT.org/" }

              Expect.throws
                  (fun () ->
                      vocabulary {
                          ``include`` a
                          ``include`` b
                      }
                      |> ignore)
                  "conflicting prefix should raise"
          }

          test "include raises on conflicting EquivalentClass values" {
              let a =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      equivalentClass typeof<Order> "schema:Order"
                  }

              let b =
                  vocabulary {
                      prefix "ex" "http://example.com/vocab#"
                      equivalentClass typeof<Order> "ex:SomethingElse"
                  }

              Expect.throws
                  (fun () ->
                      vocabulary {
                          ``include`` a
                          ``include`` b
                      }
                      |> ignore)
                  "conflicting EC should raise"
          }

          test "include raises on conflicting ProvClass values" {
              let a = vocabulary { provClass typeof<OrderPlaced> ProvOClass.Activity }

              let b = vocabulary { provClass typeof<OrderPlaced> ProvOClass.Entity }

              Expect.throws
                  (fun () ->
                      vocabulary {
                          ``include`` a
                          ``include`` b
                      }
                      |> ignore)
                  "conflicting ProvClass should raise"
          }

          test "include raises on conflicting ConstraintPattern values" {
              let a = vocabulary { constrainPattern typeof<Address> "ZipCode" @"^\d{5}$" }

              let b = vocabulary { constrainPattern typeof<Address> "ZipCode" @"^\d{4}$" }

              Expect.throws
                  (fun () ->
                      vocabulary {
                          ``include`` a
                          ``include`` b
                      }
                      |> ignore)
                  "conflicting ConstraintPattern should raise"
          } ]

// AT4: Undefined prefix raises at CE evaluation time.
[<Tests>]
let at4 =
    test "AT4: equivalentClass with undeclared prefix raises InvalidOperationException citing prefix name" {
        let result =
            try
                vocabulary {
                    prefix "schema" "https://schema.org/"
                    equivalentClass typeof<Order> "wikidata:Q123"
                }
                |> Some
            with :? InvalidOperationException as ex ->
                Expect.stringContains ex.Message "wikidata" "message must cite unknown prefix"
                None

        Expect.isNone result "CE evaluation must have raised"
    }

// AT5: using raises on duplicate.
[<Tests>]
let at5 =
    test "AT5: declaring the same using prefix twice raises" {
        Expect.throws
            (fun () ->
                vocabulary {
                    prefix "schema" "https://schema.org/"
                    using "schema"
                    using "schema"
                }
                |> ignore)
            "duplicate using should raise"
    }

// AT6: ProvOClass cases are Entity, Activity, Agent.
[<Tests>]
let at6 =
    test "AT6: ProvOClass DU has Entity, Activity, Agent cases" {
        let r =
            vocabulary {
                provClass typeof<Order> ProvOClass.Entity
                provClass typeof<TicTacToeGame> ProvOClass.Activity
                provClass typeof<Address> ProvOClass.Agent
            }

        Expect.equal (VocabularyRegistry.tryFindProvClass typeof<Order> r) (Some ProvOClass.Entity) "Entity"

        Expect.equal (VocabularyRegistry.tryFindProvClass typeof<TicTacToeGame> r) (Some ProvOClass.Activity) "Activity"

        Expect.equal (VocabularyRegistry.tryFindProvClass typeof<Address> r) (Some ProvOClass.Agent) "Agent"
    }

// AT-TRI: tryResolveIri edge cases.
[<Tests>]
let at_tri =
    testList
        "AT-TRI: tryResolveIri guards"
        [ test "valid schema:Game expands to Ok(Some https://schema.org/Game)" {
              let prefixes = Map.ofList [ "schema", Uri "https://schema.org/" ]

              match VocabularyRegistry.tryResolveIri prefixes (Some "schema:Game") with
              | Ok(Some uri) -> Expect.equal (uri.AbsoluteUri) "https://schema.org/Game" "expanded IRI"
              | other -> failwith $"Expected Ok(Some uri), got {other}"
          }

          test "None input resolves to Ok None" {
              let prefixes = Map.ofList [ "schema", Uri "https://schema.org/" ]

              match VocabularyRegistry.tryResolveIri prefixes None with
              | Ok None -> ()
              | other -> failwith $"Expected Ok None, got {other}"
          }

          test "malformed expansion (local part with space) returns Error, not a bad Uri" {
              // A local part containing a space produces an IRI that Uri.IsWellFormedUriString rejects.
              let prefixes = Map.ofList [ "schema", Uri "https://schema.org/" ]

              match VocabularyRegistry.tryResolveIri prefixes (Some "schema:bad local name") with
              | Error msg -> Expect.stringContains msg "not a well-formed" "error must cite malformed IRI"
              | Ok _ -> failwith "Expected Error for malformed expansion"
          }

          // Finding #4: bare name without colon must return Error, not throw UriFormatException.
          test "Finding #4: bare name (no colon) returns Error and does not throw" {
              let prefixes = Map.ofList [ "schema", Uri "https://schema.org/" ]

              let result =
                  try
                      VocabularyRegistry.tryResolveIri prefixes (Some "Game")
                  with ex ->
                      failwith $"tryResolveIri must not throw; got {ex.GetType().Name}: {ex.Message}"

              Expect.isTrue (Result.isError result) "non-CURIE bare name must return Error"

              match result with
              | Error msg -> Expect.stringContains msg "Game" "error message must cite the input"
              | Ok _ -> failwith "Expected Error"
          } ]

// FsCheck property tests for include union laws.
[<Tests>]
let propertyTests =
    testList
        "Property: include union laws"
        [ testProperty "include is commutative for conflict-free registries" (fun () ->
              let a = vocabulary { prefix "ex" "http://example.com/" }
              let b = vocabulary { prefix "wd" "https://www.wikidata.org/wiki/" }
              let ab = VocabularyRegistry.include' a b
              let ba = VocabularyRegistry.include' b a
              ab.Prefixes = ba.Prefixes && ab.Using = ba.Using)

          testProperty "include is associative for conflict-free registries" (fun () ->
              let a = vocabulary { prefix "ex" "http://example.com/" }
              let b = vocabulary { prefix "wd" "https://www.wikidata.org/wiki/" }
              let c = vocabulary { prefix "prov" "http://www.w3.org/ns/prov#" }
              let ab_c = VocabularyRegistry.include' (VocabularyRegistry.include' a b) c
              let a_bc = VocabularyRegistry.include' a (VocabularyRegistry.include' b c)
              ab_c.Prefixes = a_bc.Prefixes)

          testProperty "include with empty is identity" (fun () ->
              let a = vocabulary { prefix "schema" "https://schema.org/" }
              let withEmpty = VocabularyRegistry.include' a VocabularyRegistry.empty
              let emptyWith = VocabularyRegistry.include' VocabularyRegistry.empty a
              withEmpty.Prefixes = a.Prefixes && emptyWith.Prefixes = a.Prefixes)

          testProperty "conflicting prefix merge always raises" (fun () ->
              let a = vocabulary { prefix "x" "http://a.example/" }
              let b = vocabulary { prefix "x" "http://b.example/" }

              try
                  VocabularyRegistry.include' a b |> ignore
                  false
              with :? InvalidOperationException ->
                  true) ]
