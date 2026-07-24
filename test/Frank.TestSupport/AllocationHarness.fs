/// #437: reusable allocation-growth gate. Perf-fix issues attach their assertions to this
/// harness rather than re-inventing GC-delta measurement per issue.
module Frank.TestSupport.AllocationHarness

open System

/// Calls `warmup` once (to trigger lazy init/caching), then calls `request` `iterations` times,
/// returning the per-call allocation delta (bytes, current thread) for each of those calls.
/// Side-effecting: runs `warmup` and then `request` on the calling thread; callers own any
/// async plumbing (e.g. block on an HttpClient call inside `request`).
let measureAllocationDeltas (warmup: unit -> unit) (request: unit -> unit) (iterations: int) : int64[] =
    if iterations < 3 then
        invalidArg (nameof iterations) "need at least 3 repeats to establish and compare against a baseline"

    warmup ()

    Array.init iterations (fun _ ->
        let before = GC.GetAllocatedBytesForCurrentThread()
        request ()
        GC.GetAllocatedBytesForCurrentThread() - before)

/// Pure: true when no call after the second allocates more than the second call's allocation
/// plus `toleranceBytes`. The second call (not the first) is the baseline because the first
/// call can still carry one-time cost (e.g. JIT of that exact code path) even after an
/// external `warmup`.
let noGrowthTrend (toleranceBytes: int64) (deltas: int64[]) : bool =
    if deltas.Length < 2 then
        invalidArg (nameof deltas) "need at least 2 measurements to compare against a baseline"

    let baseline = deltas.[1]
    deltas.[1..] |> Array.forall (fun delta -> delta <= baseline + toleranceBytes)

/// Asserts that, once warmed, `request` shows no per-call allocation growth trend across
/// `iterations` repeats (within `toleranceBytes`). Raises via `failwith` on violation.
let assertNoAllocationGrowth
    (warmup: unit -> unit)
    (request: unit -> unit)
    (iterations: int)
    (toleranceBytes: int64)
    : unit =
    let deltas = measureAllocationDeltas warmup request iterations

    if not (noGrowthTrend toleranceBytes deltas) then
        failwith
            $"Allocation growth detected across {iterations} repeats (tolerance={toleranceBytes} bytes): %A{deltas}"
