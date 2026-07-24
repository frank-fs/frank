module Frank.Tests.AllocationHarnessTests

open Expecto
open Frank.TestSupport.AllocationHarness

/// #437: this harness is infrastructure the memoization/BoundedCache perf-fix issues (#438/#439)
/// attach their assertions to. Proven here against a deliberately growing (known-bad) handler
/// and a constant-allocation (known-good) handler before any consumer relies on it.

[<Tests>]
let tests =
    testList
        "AllocationHarness (#437)"
        [ testCase "known-good: constant per-call allocation passes"
          <| fun _ ->
              let request () = Array.zeroCreate<byte> 64 |> ignore

              assertNoAllocationGrowth ignore request 50 2_048L

          testCase "known-bad: per-call allocation that grows with call count is detected"
          <| fun _ ->
              let mutable callCount = 0

              let request () =
                  callCount <- callCount + 1
                  Array.zeroCreate<byte> (callCount * 64) |> ignore

              Expect.throwsC (fun () -> assertNoAllocationGrowth ignore request 50 256L) (fun ex ->
                  Expect.stringContains
                      ex.Message
                      "growth"
                      "per-call allocation growing linearly with call count must be flagged with a growth-detection message")

          testCase "measureAllocationDeltas rejects fewer than 3 iterations"
          <| fun _ ->
              Expect.throwsT<System.ArgumentException>
                  (fun () -> measureAllocationDeltas ignore ignore 2 |> ignore)
                  "need at least 3 repeats to establish and compare against a baseline"

          testCase "noGrowthTrend rejects fewer than 2 measurements"
          <| fun _ ->
              Expect.throwsT<System.ArgumentException>
                  (fun () -> noGrowthTrend 0L [| 1L |] |> ignore)
                  "a single measurement has no baseline to compare against" ]
