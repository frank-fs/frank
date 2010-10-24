module MiddlewaresSpecs
open Frack
open Frack.Middlewares
open NaturalSpec
open BaseSpecs

let head (app:App) = fun env ->
  let status, hdrs, body = app.Invoke(env)
  match env?HTTP_METHOD with
  | Str "HEAD" -> ( status, hdrs, Seq.empty )
  | _ -> ( status, hdrs, body )
  
let ``running a middleware for a`` m env =
  printMethod m
  head app env
    
[<Scenario>]
let ``When running a middleware on an app handling a GET env, the body should be left alone.``() =
  let env = getEnv "GET"
  Given env
  |> When ``running a middleware for a`` "GET"
  |> It should have (fun result -> match result with _, _, bd -> bd = body)
  |> Verify
  
[<Scenario>]
let ``When running a middleware on an app handling a HEAD env, the body should be empty.``() =
  let env = getEnv "HEAD"
  Given env
  |> When ``running a middleware for a`` "HEAD"
  |> It should have (fun result -> match result with _, _, bd -> bd = Seq.empty)
  |> Verify
    
[<Scenario>]
let ``When adding the printRequest middleware, the body should include more than 1 value.``() =
  let env = getEnv "GET"
  let ``running a middleware to print the env`` env =
    printMethod ""
    let result = match printRequest app env with _, _, bd -> bd
    result
  Given env
  |> When ``running a middleware to print the env``
  |> It should have (fun r -> r |> Seq.length > 1)
  |> Verify