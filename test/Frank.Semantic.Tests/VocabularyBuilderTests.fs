module Frank.Semantic.Tests.VocabularyBuilderTests

open System
open Expecto
open Frank.Semantic

type SampleOrder = { Id: int }
type SampleGame = { Board: string }
type SampleAddress = { ZipCode: string }
type SampleEvent = { At: DateTimeOffset }

[<Tests>]
let at1 =
    testList
        "AT1: CE evaluates to VocabularyRegistry"
        [ test "prefix populates Prefixes map" {
              let r = vocabulary { prefix "schema" "https://schema.org/" }
              Expect.equal r.Prefixes.["schema"] (Uri("https://schema.org/")) "prefix should be Uri"
          }

          test "multiple prefixes accumulate" {
              let r =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      prefix "ex" "http://example.com/vocab#"
                  }

              Expect.equal r.Prefixes.["schema"] (Uri("https://schema.org/")) "schema prefix"
              Expect.equal r.Prefixes.["ex"] (Uri("http://example.com/vocab#")) "ex prefix"
          } ]

[<Tests>]
let at2 =
    testList
        "AT2: All operations populate fields correctly"
        [ test "using populates Using set" {
              let r =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      using "schema"
                  }

              Expect.contains r.Using "schema" "using should add to Using set"
          }

          test "equivalentClass populates EquivalentClasses" {
              let r =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      equivalentClass typeof<SampleOrder> "schema:Order"
                  }

              Expect.equal
                  r.EquivalentClasses.[typeof<SampleOrder>]
                  (Uri("https://schema.org/Order"))
                  "equivalentClass should map type to Uri"
          }

          test "seeAlso populates SeeAlso" {
              let r =
                  vocabulary {
                      prefix "wikidata" "https://www.wikidata.org/wiki/"
                      seeAlso typeof<SampleGame> "wikidata:Q11907"
                  }

              let uris = r.SeeAlso.[typeof<SampleGame>]
              Expect.equal uris [ Uri("https://www.wikidata.org/wiki/Q11907") ] "seeAlso should map type to Uri list"
          }

          test "fieldSeeAlso populates FieldSeeAlso" {
              let r =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      fieldSeeAlso typeof<SampleOrder> "LineItems" "schema:orderedItem"
                  }

              let key = (typeof<SampleOrder>, "LineItems")
              let uris = r.FieldSeeAlso.[key]

              Expect.equal
                  uris
                  [ Uri("https://schema.org/orderedItem") ]
                  "fieldSeeAlso should map (type, field) to Uri list"
          }

          test "provClass populates ProvClasses" {
              let r = vocabulary { provClass typeof<SampleEvent> Activity }
              Expect.equal r.ProvClasses.[typeof<SampleEvent>] Activity "provClass should map type to ProvOClass"
          }

          test "constrainPattern populates ConstraintPatterns" {
              let r = vocabulary { constrainPattern typeof<SampleAddress> "ZipCode" "^\d{5}$" }
              let key = (typeof<SampleAddress>, "ZipCode")
              Expect.equal r.ConstraintPatterns.[key] "^\d{5}$" "constrainPattern should map (type, field) to pattern"
          }

          test "all operations together" {
              let r =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      prefix "wikidata" "https://www.wikidata.org/wiki/"
                      using "schema"
                      equivalentClass typeof<SampleOrder> "schema:Order"
                      seeAlso typeof<SampleGame> "wikidata:Q11907"
                      fieldSeeAlso typeof<SampleOrder> "LineItems" "schema:orderedItem"
                      provClass typeof<SampleEvent> Entity
                      constrainPattern typeof<SampleAddress> "ZipCode" "^\d{5}$"
                  }

              Expect.contains r.Using "schema" "using"
              Expect.equal r.EquivalentClasses.[typeof<SampleOrder>] (Uri("https://schema.org/Order")) "equivalentClass"
              Expect.equal r.SeeAlso.[typeof<SampleGame>] [ Uri("https://www.wikidata.org/wiki/Q11907") ] "seeAlso"

              Expect.equal
                  r.FieldSeeAlso.[(typeof<SampleOrder>, "LineItems")]
                  [ Uri("https://schema.org/orderedItem") ]
                  "fieldSeeAlso"

              Expect.equal r.ProvClasses.[typeof<SampleEvent>] Entity "provClass"
              Expect.equal r.ConstraintPatterns.[(typeof<SampleAddress>, "ZipCode")] "^\d{5}$" "constrainPattern"
          } ]

[<Tests>]
let at3 =
    testList
        "AT3: extend composes registries"
        [ test "extend merges prefixes, using, and alignments" {
              let baseReg =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      equivalentClass typeof<SampleOrder> "schema:Order"
                  }

              let extended =
                  vocabulary {
                      prefix "wikidata" "https://www.wikidata.org/wiki/"
                      seeAlso typeof<SampleGame> "wikidata:Q11907"
                      extend baseReg
                  }

              Expect.equal extended.Prefixes.["schema"] (Uri("https://schema.org/")) "included prefix"
              Expect.equal extended.Prefixes.["wikidata"] (Uri("https://www.wikidata.org/wiki/")) "own prefix"

              Expect.equal
                  extended.EquivalentClasses.[typeof<SampleOrder>]
                  (Uri("https://schema.org/Order"))
                  "included equivalentClass"

              Expect.equal
                  extended.SeeAlso.[typeof<SampleGame>]
                  [ Uri("https://www.wikidata.org/wiki/Q11907") ]
                  "own seeAlso"
          }

          test "extend absorbs identical-value duplicate prefixes silently" {
              let a = vocabulary { prefix "schema" "https://schema.org/" }

              let b =
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      extend a
                  }

              Expect.equal b.Prefixes.["schema"] (Uri("https://schema.org/")) "identical prefix is absorbed"
          }

          test "extend raises InvalidOperationException on conflicting prefix" {
              let a = vocabulary { prefix "schema" "https://schema.org/" }

              Expect.throws
                  (fun () ->
                      vocabulary {
                          prefix "schema" "https://schema.org/v2/"
                          extend a
                      }
                      |> ignore)
                  "conflicting prefix should throw"
          }

          test "extend exception message cites the prefix name" {
              let a = vocabulary { prefix "schema" "https://schema.org/" }

              let mutable msg = ""

              try
                  vocabulary {
                      prefix "schema" "https://schema.org/v2/"
                      extend a
                  }
                  |> ignore
              with :? InvalidOperationException as ex ->
                  msg <- ex.Message

              Expect.stringContains msg "schema" "exception message should cite prefix name"
          } ]

[<Tests>]
let at4 =
    testList
        "AT4: Undefined prefix raises at CE evaluation time"
        [ test "using undeclared prefix in equivalentClass raises InvalidOperationException" {
              let result =
                  try
                      vocabulary {
                          prefix "schema" "https://schema.org/"
                          equivalentClass typeof<SampleOrder> "wikidata:Q123"
                      }
                      |> Some
                  with :? InvalidOperationException ->
                      None

              Expect.isNone result "should raise InvalidOperationException for undeclared prefix"
          }

          test "exception message cites the unknown prefix" {
              let mutable msg = ""

              try
                  vocabulary {
                      prefix "schema" "https://schema.org/"
                      equivalentClass typeof<SampleOrder> "wikidata:Q123"
                  }
                  |> ignore
              with :? InvalidOperationException as ex ->
                  msg <- ex.Message

              Expect.stringContains msg "wikidata" "exception message should cite the unknown prefix"
          }

          test "using undeclared prefix in seeAlso raises" {
              Expect.throws
                  (fun () ->
                      vocabulary {
                          prefix "schema" "https://schema.org/"
                          seeAlso typeof<SampleGame> "wikidata:Q11907"
                      }
                      |> ignore)
                  "undeclared prefix in seeAlso should throw"
          }

          test "using undeclared prefix in fieldSeeAlso raises" {
              Expect.throws
                  (fun () ->
                      vocabulary {
                          prefix "schema" "https://schema.org/"
                          fieldSeeAlso typeof<SampleOrder> "LineItems" "other:orderedItem"
                      }
                      |> ignore)
                  "undeclared prefix in fieldSeeAlso should throw"
          } ]
