(* # Frank.Middleware

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011-2012, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
module Frank.Middleware

open System
open System.Collections.Generic
open System.Net.Http
open Frank
open Newtonsoft.Json.Linq

// TODO: add diagnostics and logging
// TODO: add messages to access diagnostics and logging info from the agent
  
/// Logs the incoming request and the time to respond.
let log app =
  fun (request: HttpRequestMessage) -> async {
    let sw = System.Diagnostics.Stopwatch.StartNew()
    let! response = app request
    printfn "Received a %A request from %A. Responded in %i ms."
            request.Method.Method request.RequestUri.PathAndQuery sw.ElapsedMilliseconds
    sw.Reset()
    return response }

/// Intercepts a request using the HEAD method and strips away the returned body from a GET response.
let head app =
  fun (request: HttpRequestMessage) ->
    if request.Method = HttpMethod.Head then 
      async {
        request.Method <- HttpMethod.Get
        let! (response : HttpResponseMessage) = app request
        let emptyContent = HttpContent.Empty
        for KeyValue(header, value) in response.Content.Headers do
          emptyContent.Headers.Add(header, value)
        return response }
    else app request

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
  fun (request: HttpRequestMessage) -> async {
    let content = request.Content
    if request.Method = HttpMethod.Post &&
       content.Headers.ContentType.MediaType <> "application/x-http-form-urlencoded" then
      let! form = content.AsyncReadAs<JToken>()
      let httpMethod =
        let _method = (form.SelectToken("_method") |> string)
        if (not << String.IsNullOrEmpty) _method then
          new HttpMethod(_method)
        elif request.Content.Headers.Contains("HTTP_X_HTTP_METHOD_OVERRIDE") then
          let m = request.Content.Headers.GetValues("HTTP_X_HTTP_METHOD_OVERRIDE") |> Seq.head in new HttpMethod(m)
        else request.Method
      if overridableHttpMethods |> List.exists ((=) httpMethod) then
        request.Content.Headers.Add("methodoverride_original_method", httpMethod.Method)
        request.Method <- HttpMethod.Post
    return! app request }
