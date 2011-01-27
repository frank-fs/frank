module ArraySpecs
open Frack.Collections
open NUnit.Framework
open BaseSpecs

[<TestCase([|1;2;3;4;5;6;7|], 0, 1, Result = [|1|])>]
[<TestCase([|1;2;3;4;5;6;7|], 0, 2, Result = [|1;2|])>]
[<TestCase([|1;2;3;4;5;6;7|], 2, 5, Result = [|3;4;5|])>]
[<TestCase([|1;2;3;4;5;6;7|], 2, -3, Result = [|3;4|])>]
let ``Slicing an array should return``(arr: int[], start: int, stop: int) = arr |> Array.slice start stop
