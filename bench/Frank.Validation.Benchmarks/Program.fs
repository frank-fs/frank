module Program

open BenchmarkDotNet.Running
open Benchmarks

[<EntryPoint>]
let main _ =
    BenchmarkRunner.Run<ValidationBenchmarks>() |> ignore
    0
