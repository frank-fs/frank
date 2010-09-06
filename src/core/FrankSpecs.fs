module FrankSpecs
open System.Collections.Generic
open Frack
open Frank
open NaturalSpec

[<Scenario>]
let ``When creating a Frank applicaion, it should accept a seq of request mappings``() =
  let ``creating an app`` handlers =
    printMethod ""
    FrankApp handlers
  Given [| get "/" (fun _ -> Object(Str("Hello world!"))) |]
  |> When ``creating an app``
  |> It should be (fun app -> app.GetType() = typeof<FrankApp>)
  |> Verify