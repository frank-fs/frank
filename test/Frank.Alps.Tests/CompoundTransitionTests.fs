module Frank.Alps.Tests.CompoundTransitionTests

open Expecto
open Frank.Alps

[<Tests>]
let tests =
    testList
        "guardedBy, entersRegions"
        [ test "guardedBy sets Guard, leaving From untouched" {
              let vehicleRed = semantic "vehicleRed"
              let pedWaiting = semantic "pedWaiting"
              let d = unsafe "walk" |> guardedBy (StateGuard.All [ StateGuard.State vehicleRed; StateGuard.State pedWaiting ])
              Expect.isTrue d.Guard.IsSome ""
              Expect.equal d.From [] ""
          }

          test "entersRegions sets Targets, leaving Rt untouched" {
              let vehicleFlashing = semantic "vehicleFlashing"
              let pedFlashing = semantic "pedFlashing"
              let d = unsafe "emergencyOverride" |> entersRegions [ TransitionTarget.EnterState vehicleFlashing; TransitionTarget.EnterState pedFlashing ]
              Expect.equal d.Targets.Length 2 ""
              Expect.equal d.Rt None ""
          }

          test "guardedBy and entersRegions compose with each other and with from/rt" {
              let a, b, c = semantic "a", semantic "b", semantic "c"
              let d =
                  unsafe "x"
                  |> from [ a ]
                  |> rt b
                  |> guardedBy (StateGuard.State a)
                  |> entersRegions [ TransitionTarget.EnterState c ]
              Expect.isTrue (d.From <> [] && d.Guard.IsSome && d.Rt.IsSome && d.Targets <> []) ""
          } ]
