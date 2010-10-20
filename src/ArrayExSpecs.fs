module ArrayExSpecs
open Frank.Types
open NaturalSpec

let slicing start stop arr =
  printMethod ""
  arr |> ArrayEx.slice start stop

[<ScenarioTemplate(0, 1, [|1|])>]
[<ScenarioTemplate(0, 2, [|1;2|])>]
[<ScenarioTemplate(2, 5, [|3;4;5|])>]
[<ScenarioTemplate(2,-3, [|3;4|])>]
let ``When slicing an array`` (arr, start, stop, result) =
  Given [|1..7|]
  |> When (slicing start stop)
  |> It should equal result
  |> Verify