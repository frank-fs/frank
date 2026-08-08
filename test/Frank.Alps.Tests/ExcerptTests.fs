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
          }

          test "satisfiesGuard: State is existential match against a single leaf" {
              let target = System.Uri "https://example.org/states/a"
              let a = semantic "a" |> def "https://example.org/states/a"
              Expect.isTrue (Excerpt.satisfiesGuard [ target ] (StateGuard.State a)) ""
              Expect.isFalse (Excerpt.satisfiesGuard [] (StateGuard.State a)) ""
          }

          test "satisfiesGuard: All requires every element satisfied" {
              let ua, ub = System.Uri "https://example.org/a", System.Uri "https://example.org/b"
              let a = semantic "a" |> def "https://example.org/a"
              let b = semantic "b" |> def "https://example.org/b"
              let guard = StateGuard.All [ StateGuard.State a; StateGuard.State b ]
              Expect.isTrue (Excerpt.satisfiesGuard [ ua; ub ] guard) ""
              Expect.isFalse (Excerpt.satisfiesGuard [ ua ] guard) ""
          }

          test "satisfiesGuard: Any requires at least one element satisfied" {
              let ua = System.Uri "https://example.org/a"
              let a = semantic "a" |> def "https://example.org/a"
              let b = semantic "b" |> def "https://example.org/b"
              let guard = StateGuard.Any [ StateGuard.State a; StateGuard.State b ]
              Expect.isTrue (Excerpt.satisfiesGuard [ ua ] guard) ""
          }

          test "satisfiesGuard: Not negates" {
              let ua = System.Uri "https://example.org/a"
              let a = semantic "a" |> def "https://example.org/a"
              Expect.isFalse (Excerpt.satisfiesGuard [ ua ] (StateGuard.Not(StateGuard.State a))) ""
              Expect.isTrue (Excerpt.satisfiesGuard [] (StateGuard.Not(StateGuard.State a))) ""
          }

          test "satisfiesGuard: nested All/Any" {
              let ua, ub, uc =
                  System.Uri "https://example.org/a", System.Uri "https://example.org/b", System.Uri "https://example.org/c"
              let a = semantic "a" |> def "https://example.org/a"
              let b = semantic "b" |> def "https://example.org/b"
              let c = semantic "c" |> def "https://example.org/c"
              let guard = StateGuard.All [ StateGuard.State a; StateGuard.Any [ StateGuard.State b; StateGuard.State c ] ]
              Expect.isTrue (Excerpt.satisfiesGuard [ ua; uc ] guard) ""
              Expect.isFalse (Excerpt.satisfiesGuard [ ua ] guard) ""
          } ]
