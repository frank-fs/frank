module Frank.Semantic.Tests.ShapeTypesTests

open System
open Expecto
open Frank.Semantic

[<Tests>]
let tests =
    testList
        "ShapeTypes"
        [ test "NonEmptyList.ofList None on empty, Some on non-empty; toList round-trips" {
              Expect.isNone (NonEmptyList.ofList ([]: int list)) "empty → None"
              let nel = NonEmptyList.ofList [ 1; 2; 3 ] |> Option.get
              Expect.equal (NonEmptyList.toList nel) [ 1; 2; 3 ] "round-trip"
              Expect.equal nel.Head 1 "head"
          }
          test "ShapeDecl is a total DU over RecordShape | EnumShape" {
              let r =
                  RecordShape(
                      Uri "https://schema.org/MoveAction",
                      [ { Path = Uri "https://schema.org/position"
                          Datatype = Some XsdInteger
                          MinCount = 1
                          MaxCount = Some 1
                          Pattern = None } ]
                  )

              let e =
                  EnumShape(
                      Uri "https://schema.org/Status",
                      { Head = Uri "https://schema.org/Active"
                        Tail = [] }
                  )

              let describe =
                  function
                  | RecordShape _ -> "record"
                  | EnumShape _ -> "enum"

              Expect.equal (describe r) "record" "record case"
              Expect.equal (describe e) "enum" "enum case"
          } ]
