module Frank.Alps.Tests.ExcerptTests

open System
open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "satisfiesState"
        [ test "a state with a matching Def satisfies directly" {
              let uri = Uri "https://example.org/states/open"
              let openState = semantic "open" |> def "https://example.org/states/open"
              Expect.isTrue (Excerpt.satisfiesState uri openState) ""
          }

          test "a state with a non-matching Def does not satisfy" {
              let uri = Uri "https://example.org/states/open"
              let closedState = semantic "closed" |> def "https://example.org/states/closed"
              Expect.isFalse (Excerpt.satisfiesState uri closedState) ""
          }

          test "a state with no Def never satisfies" {
              let uri = Uri "https://example.org/states/open"
              Expect.isFalse (Excerpt.satisfiesState uri (semantic "open")) ""
          }

          test "a composite (contains) state satisfies when a nested child's Def matches" {
              let uri = Uri "https://example.org/states/inProgress"
              let inProgress = semantic "inProgress" |> def "https://example.org/states/inProgress"
              let waiting = semantic "waiting" |> def "https://example.org/states/waiting"
              let openState = semantic "open" |> contains [ waiting; inProgress ]

              Expect.isTrue (Excerpt.satisfiesState uri openState) "matches via a nested descendant"
          }

          test "matching is recursive through more than one level of nesting" {
              let uri = Uri "https://example.org/states/deep"
              let deep = semantic "deep" |> def "https://example.org/states/deep"
              let mid = semantic "mid" |> contains [ deep ]
              let top = semantic "top" |> contains [ mid ]

              Expect.isTrue (Excerpt.satisfiesState uri top) ""
          } ]
