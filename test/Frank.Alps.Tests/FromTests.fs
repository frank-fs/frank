module Frank.Alps.Tests.FromTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "from"
        [ test "from sets the From field to the given descriptor list" {
              let openState = semantic "open"
              let closedState = semantic "closed"
              let d = unsafe "makeMove" |> from [ openState; closedState ]
              Expect.equal (d.From |> List.map (fun x -> x.Id)) [ "open"; "closed" ] ""
          }

          test "a transition with no from has an empty From list" {
              let d = safe "viewResult"
              Expect.equal d.From [] ""
          }

          test "from and rt are independent fields" {
              let openState = semantic "open"
              let game = semantic "game"
              let d = safe "viewGame" |> from [ openState ] |> rt game
              Expect.equal d.From.Length 1 ""
              Expect.isTrue d.Rt.IsSome ""
          }

          test "from replaces, not appends, on a second call" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let d = unsafe "x" |> from [ a ] |> from [ b; c ]
              Expect.equal (d.From |> List.map (fun x -> x.Id)) [ "b"; "c" ] ""
          } ]
