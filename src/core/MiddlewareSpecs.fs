module MiddlewareSpecs
open Frack
open Frack.Middleware
open NaturalSpec
open BaseSpecs

let head (app:App) = fun request ->
  let status, hdrs, body = app.Invoke(request)
  match request?HTTP_METHOD with
  | Str "HEAD" -> ( status, hdrs, Seq.empty )
  | _ -> ( status, hdrs, body )
  
let ``running a middleware for a`` m request =
  printMethod m
  head app request
    
[<Scenario>]
let ``When running a middleware on an app handling a GET request, the body should be left alone.``() =
  let request = getUtility "GET"
  Given request
  |> When ``running a middleware for a`` "GET"
  |> It should have (fun result -> match result with _, _, bd -> bd = body)
  |> Verify
  
[<Scenario>]
let ``When running a middleware on an app handling a HEAD request, the body should be empty.``() =
  let request = getUtility "HEAD"
  Given request
  |> When ``running a middleware for a`` "HEAD"
  |> It should have (fun result -> match result with _, _, bd -> bd = Seq.empty)
  |> Verify
    
[<Scenario>]
let ``When adding the printRequest middleware, the body should include more than 1 value.``() =
  let request = getUtility "GET"
  let ``running a middleware to print the request`` request =
    printMethod ""
    let printUtility = printRequest app 
    let result = printUtility request
    match result with
    | _, _, bd -> bd |> Seq.map (ByteString.toString) |> Seq.iter (printfn "%s")
    result
  Given request
  |> When ``running a middleware to print the request``
  |> It should have (fun result -> match result with _, _, bd -> bd |> Seq.length > 1)
  |> Verify