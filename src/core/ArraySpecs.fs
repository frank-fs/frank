module ArraySpecs
open Frack
open NaturalSpec

let slicing start stop arr =
  printMethod ""
  arr |> Array.slice start stop

[<Scenario>]
let ``When slicing the first element of an array``() =
  Given [|1..7|]
  |> When (slicing 0 1)
  |> It should equal [|1|]
  |> Verify

[<Scenario>]
let ``When slicing the first two elements of an array``() =
  Given [|1..7|]
  |> When (slicing 0 2)
  |> It should equal [|1;2|]
  |> Verify

[<Scenario>]
let ``When slicing the third through fifth elements of an array``() =
  Given [|1..7|]
  |> When (slicing 2 5)
  |> It should equal [|3;4;5|]
  |> Verify

[<Scenario>]
let ``When slicing the third through the third from last elements of an array``() =
  Given [|1..7|]
  |> When (slicing 2 -3)
  |> It should equal [|3;4|]
  |> Verify
