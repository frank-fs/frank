module Program

open BenchmarkDotNet.Running
open Benchmarks

[<EntryPoint>]
let main _ =
    BenchmarkRunner.Run<ProvenanceBenchmarks>() |> ignore
    0
