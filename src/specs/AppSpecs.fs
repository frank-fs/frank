module Frack.Specs.AppSpecs
open Frack
open Frack.Specs
open NaturalSpec

let errors, env = Env.create Fakes.context
let body = seq { yield "Howdy" } 
let app (env:Environment) =
  ( 200, Map.ofList [("Content_Type","text/plain");("Content_Length","5")], body )

let head (app:Environment -> int * Map<string,string> * seq<string>) =
  fun env -> let status, hdrs, body = app env
             if env.HTTP_METHOD = "HEAD" then
               ( status, hdrs, Seq.empty )
             else
               ( status, hdrs, body )

[<Scenario>]
let ``When running an app that just returns pre-defined values, those values should be returned.``() =
  let ``running an app with predefined values`` (env:Environment) =
    printMethod "200, text/plain and 5, Howdy"
    app env
  Given env
  |> When ``running an app with predefined values``
  |> It should equal ( 200, Map.ofList [("Content_Type","text/plain");("Content_Length","5")], body )
  |> Verify

let ``running a middleware for a `` (m:string) (env:Environment) =
  printMethod m
  let e = { env with HTTP_METHOD = m }
  head app e

[<Scenario>]
let ``When running a middleware on an app handling a GET request, the body should be left alone.``() =
  Given env
  |> When ``running a middleware for a `` "GET"
  |> It should have (fun result -> let st, hd, bd = result
                                   bd = body)
  |> Verify

[<Scenario>]
let ``When running a middleware on an app handling a HEAD request, the body should be left alone.``() =
  Given env
  |> When ``running a middleware for a `` "HEAD"
  |> It should have (fun result -> let st, hd, bd = result
                                   bd = Seq.empty)
  |> Verify