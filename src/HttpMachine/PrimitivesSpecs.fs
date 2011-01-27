module PrimitivesSpecs
open System
open HttpMachine.Primitives
open NUnit.Framework
open BaseSpecs

let escapedSegments = [|
  [| "%20"B; " "B |]
  [| "%2F"B; "/"B |]
  [| "%2f"B; "/"B |]
|]

[<Test>]
[<TestCaseSource("escapedSegments")>]
let ``test escaped parser returns the byte representing the full hexadecimal character``(esc, expected:byte[]) =
  let segment = ArraySegment<_>(esc)
  let actual, rest = escaped segment |> Option.get
  actual == expected.[0]