module Frack.Specs.AppSpecs
open System.Text
open Frack
open Frack.Specs.Fakes
open Frack.Utility
open NaturalSpec

let getEnv m = Env.createEnvironment (createContext m) (StringBuilder())
let hdrs = dict [| ("Content_Type","text/plain");("Content_Length","5") |] 
let body = seq { yield "Howdy" } 
let app env = ( 200, hdrs, body )

let head app =
  fun env -> let status, hdrs, body = app env
             match env?HTTP_METHOD with
               | Str "HEAD" -> ( status, hdrs, Seq.empty )
               | _ -> ( status, hdrs, body )

[<Scenario>]
let ``When running an app that just returns pre-defined values, those values should be returned.``() =
  let ``running an app with predefined values`` env =
    printMethod "200, type = text/plain and length = 5, Howdy"
    app env
  let env = getEnv "GET"
  Given env
  |> When ``running an app with predefined values``
  |> It should equal ( 200, hdrs, body )
  |> Verify

let ``running a middleware for a`` m env =
  printMethod m
  head app env

[<Scenario>]
let ``When running a middleware on an app handling a GET request, the body should be left alone.``() =
  let env = getEnv "GET"
  Given env
  |> When ``running a middleware for a`` "GET"
  |> It should have (fun result -> match result with _, _, bd -> bd = body)
  |> Verify

[<Scenario>]
let ``When running a middleware on an app handling a HEAD request, the body should be empty.``() =
  let env = getEnv "HEAD"
  Given env
  |> When ``running a middleware for a`` "HEAD"
  |> It should have (fun result -> match result with _, _, bd -> bd = Seq.empty)
  |> Verify

[<Scenario>]
let ``When adding the printEnvironment middleware, the body should include more than 1 value.``() =
  let env = getEnv "GET"
  let ``running a middleware to print the environment`` env =
    printMethod ""
    let printEnv = Frack.Middleware.printEnvironment app 
    let result = printEnv env
    match result with
    | _, _, bd -> bd |> Seq.iter (printfn "%s")
    result
  Given env
  |> When ``running a middleware to print the environment``
  |> It should have (fun result -> match result with _, _, bd -> bd |> Seq.length > 1)
  |> Verify