module Frank.Alps.Tests.ProtocolGraphTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "ProtocolGraph.ofProfile"
        [ test "from [A] |> rt B yields one edge, FromGuard = Some (State A)" {
              let a, t, b = semantic "a", unsafe "t" |> from [ semantic "a" ], semantic "b"
              // (author with matching identity, not two unrelated `a` values)
              let aState = semantic "a"
              let tt = unsafe "t" |> from [ aState ] |> rt b
              match ProtocolGraph.ofProfile [ aState; tt; b ] with
              | [ { FromGuard = Some(StateGuard.State s); ToTargets = [ TransitionTarget.EnterState target ] } ] ->
                  Expect.equal s.Id "a" ""
                  Expect.equal target.Id "b" ""
              | other -> failwithf "expected one State-guarded edge, got %A" other
          }

          test "from [A; B] |> rt C collapses into one edge, FromGuard = Some (Any [State A; State B])" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let t = unsafe "t" |> from [ a; b ] |> rt c
              match ProtocolGraph.ofProfile [ a; b; t; c ] with
              | [ { FromGuard = Some(StateGuard.Any [ StateGuard.State s1; StateGuard.State s2 ]) } ] ->
                  Expect.equal (s1.Id, s2.Id) ("a", "b") ""
              | other -> failwithf "expected one Any-guarded edge, got %A" other
          }

          test "rt alone (no from, no guardedBy) now yields one unconditional edge" {
              let c = semantic "c"
              let t = unsafe "t" |> rt c
              match ProtocolGraph.ofProfile [ t; c ] with
              | [ { FromGuard = None } ] -> ()
              | other -> failwithf "expected one unconditional edge, got %A" other
          }

          test "entersRegions alone (no from/guardedBy/rt) yields one unconditional fan-out edge" {
              let x, y = semantic "x", semantic "y"
              let t = unsafe "t" |> entersRegions [ TransitionTarget.EnterState x; TransitionTarget.EnterState y ]
              match ProtocolGraph.ofProfile [ t; x; y ] with
              | [ { FromGuard = None; ToTargets = [ TransitionTarget.EnterState _; TransitionTarget.EnterState _ ] } ] -> ()
              | other -> failwithf "expected one unconditional 2-target edge, got %A" other
          }

          test "guardedBy wins over from when both are present" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let t = unsafe "t" |> from [ a ] |> guardedBy (StateGuard.State b) |> rt c
              match ProtocolGraph.ofProfile [ a; b; t; c ] with
              | [ { FromGuard = Some(StateGuard.State s) } ] -> Expect.equal s.Id "b" ""
              | other -> failwithf "expected guardedBy's State b to win, got %A" other
          }

          test "entersRegions wins over rt when both are present" {
              let c, x = semantic "c", semantic "x"
              let t = unsafe "t" |> rt c |> entersRegions [ TransitionTarget.EnterState x ]
              match ProtocolGraph.ofProfile [ t; c; x ] with
              | [ { ToTargets = [ TransitionTarget.EnterState target ] } ] -> Expect.equal target.Id "x" ""
              | other -> failwithf "expected entersRegions's x to win, got %A" other
          }

          test "no rt, no entersRegions -- no edge, same as today" {
              let a = semantic "a"
              let t = unsafe "t" |> from [ a ]
              Expect.equal (ProtocolGraph.ofProfile [ a; t ]) [] ""
          }

          test "a semantic (non-transition) descriptor never yields an edge" {
              Expect.equal (ProtocolGraph.ofProfile [ semantic "x" ]) [] ""
          }

          test "an empty profile yields no edges" { Expect.equal (ProtocolGraph.ofProfile []) [] "" } ]
