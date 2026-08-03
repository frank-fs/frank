module Frank.Alps.Tests.ProtocolGraphTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "ProtocolGraph.ofProfile"
        [ test "a transition with from and rt yields one edge" {
              let openState = semantic "open"
              let move = semantic "move"
              let makeMove = unsafe "makeMove" |> from [ openState ] |> rt move

              let edges = ProtocolGraph.ofProfile [ openState; move; makeMove ]

              Expect.equal edges.Length 1 ""
              Expect.equal edges.[0].FromState.Id "open" ""
              Expect.equal edges.[0].Transition.Id "makeMove" ""
              Expect.equal edges.[0].ToState.Id "move" ""
          }

          test "from [A; B] |> rt C yields two edges, one per source state" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let t = unsafe "t" |> from [ a; b ] |> rt c

              let edges = ProtocolGraph.ofProfile [ a; b; c; t ]

              Expect.equal edges.Length 2 ""
              Expect.equal (edges |> List.map (fun e -> e.FromState.Id) |> List.sort) [ "a"; "b" ] ""
              Expect.isTrue (edges |> List.forall (fun e -> e.ToState.Id = "c")) ""
          }

          test "a transition with from but no rt yields no edge" {
              let openState = semantic "open"
              let t = unsafe "t" |> from [ openState ]
              Expect.equal (ProtocolGraph.ofProfile [ openState; t ]) [] ""
          }

          test "a transition with rt but no from yields no edge" {
              let move = semantic "move"
              let t = unsafe "t" |> rt move
              Expect.equal (ProtocolGraph.ofProfile [ move; t ]) [] ""
          }

          test "a plain semantic descriptor with neither yields no edge" {
              Expect.equal (ProtocolGraph.ofProfile [ semantic "x" ]) [] ""
          }

          test "a transition nested via contains is still found" {
              let openState = semantic "open"
              let move = semantic "move"
              let makeMove = unsafe "makeMove" |> from [ openState ] |> rt move
              let resource = semantic "resource" |> contains [ makeMove ]

              let edges = ProtocolGraph.ofProfile [ openState; move; resource ]

              Expect.equal edges.Length 1 ""
              Expect.equal edges.[0].Transition.Id "makeMove" ""
          }

          test "an empty profile yields no edges" { Expect.equal (ProtocolGraph.ofProfile []) [] "" } ]
