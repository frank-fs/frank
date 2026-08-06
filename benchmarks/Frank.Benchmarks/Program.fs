module Frank.Benchmarks.Program

open BenchmarkDotNet.Running

[<EntryPoint>]
let main argv =
    BenchmarkSwitcher.FromAssembly(typeof<SingleRepresentationBenchmarks>.Assembly).Run(argv) |> ignore
    0
