(* # Frank.Middleware

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
module Frank.Middleware

open System
open System.Collections.Generic
open System.Json
open System.Net.Http
open Microsoft.ApplicationServer.Http
open Frank
open ImpromptuInterface.FSharp

/// Logs the incoming request and the time to respond.
let log app = fun (request : HttpRequestMessage) -> 
  let sw = System.Diagnostics.Stopwatch.StartNew()
  let response = app request
  printfn "Received a %A request from %A. Responded in %i ms."
          request.Method.Method request.RequestUri.PathAndQuery sw.ElapsedMilliseconds
  sw.Reset()
  response

/// Intercepts a request using the HEAD method and strips away the returned body from a GET response.
let head app = fun (request : HttpRequestMessage) ->
  if request.Method <> HttpMethod.Head then app request
  else
    request.Method <- HttpMethod.Get
    let (response : HttpResponseMessage) = app request
    let emptyContent = new ByteArrayContent([||])
    for KeyValue(header, value) in response.Content.Headers do
      emptyContent.Headers.Add(header, value)
    response.Content <- emptyContent
    response

/// The overridable HTTP methods, used in the methodOverride middleware.
let private overridableHttpMethods =
  [ HttpMethod.Delete
    HttpMethod.Get
    HttpMethod.Head
    HttpMethod.Options
    HttpMethod.Put
    HttpMethod.Trace ]

/// Intercepts a request and checks for use of X_HTTP_METHOD_OVERRIDE.
let methodOverride app =
  // Leave out POST, as that is the method we are overriding.
  fun (request : HttpRequestMessage) ->
    let request' =
      if request.Method <> HttpMethod.Post ||
         request.Content.Headers.ContentType.MediaType <> "application/x-http-form-urlencoded" then request
      else
        let form = request.Content.ReadAs<JsonValue>().AsDynamic
        let httpMethod =
          if (not << String.IsNullOrEmpty) form?_method then
            new HttpMethod(form?_method)
          elif request.Content.Headers.Contains("HTTP_X_HTTP_METHOD_OVERRIDE") then
            let m = request.Content.Headers.GetValues("HTTP_X_HTTP_METHOD_OVERRIDE") |> Seq.head in new HttpMethod(m)
          else request.Method
        if overridableHttpMethods |> List.exists ((=) httpMethod) then
          request.Content.Headers.Add("methodoverride_original_method", httpMethod.Method)
          request.Method <- HttpMethod.Post
        request
    app request'
