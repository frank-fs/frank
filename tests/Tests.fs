(* # Frank.Tests

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2010-2011, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
module Frank.Tests

open System
open System.Json
open System.Net
open System.Net.Http
open Microsoft.ApplicationServer.Http
open Frank
open ImpromptuInterface.FSharp
open NUnit.Framework
open Swensen.Unquote.Assertions

let formatter = FormUrlEncodedMediaTypeFormatter() :> Formatting.MediaTypeFormatter
let testBody = dict [("foo", "bar");("bar", "baz")]
let createTestRequest() =
  new HttpRequestMessage(
    HttpMethod.Post, Uri("http://frankfs.net/"),
    Version = Version(1,1),
    Content = new FormUrlEncodedContent(testBody))

// HttpRequestMessage -> HttpResponseMessage
let echo (request : HttpRequestMessage) = 
  let body = request.Content.ReadAs<JsonValue>(seq { yield formatter })
  let response = new HttpResponseMessage<JsonValue>(body, HttpStatusCode.OK)
  response.Content.Headers.ContentType <- Headers.MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded")
  response

[<Test>]
let ``test echo should return a response of 200 OK``() =
  let actual = echo <| createTestRequest()
  test <@ actual.StatusCode = HttpStatusCode.OK @>

[<Test>]
let ``test echo should return a response with one header for Content_Type of application/x-www-form-urlencoded``() =
  let expected = Headers.MediaTypeHeaderValue.Parse("application/x-www-form-urlencoded")
  let actual = echo <| createTestRequest()
  test <@ actual.Content.Headers.ContentType = expected @>

[<Test>]
let ``test echo should return a response with a body of echoing the request body``() =
  let response = echo <| createTestRequest()
  let actual = response.Content.ReadAs()
  test <@ actual?foo = "bar" @>
  test <@ actual?bar = "baz" @>

//(* FormUrlEncoded -> Async<Response> *)
//let transform (form:(string * string) seq) =
//  "200 OK",
//  dict [("Content_Type", "text/plain")],
//  Sequence(form |> Seq.map (fun (key, value) -> Str (key + "=" + value)))
//
//let parseFormUrlEncoded (request:IDictionary<string, obj>) = async {
//  let! body = request?RequestBody :?> Async<ArraySegment<byte>> |> Stream.readToEnd
//  return body |> Utility.parseForm |> Dict.toSeq } 
//
//let echo2 (request:IDictionary<string, obj>) =
//  transform <!> parseFormUrlEncoded request
//
//[<Test>]
//let ``test echo2 should return a response of 200 OK``() =
//  let status, _, _ = Async.RunSynchronously (echo2(test()))
//  status |> should equal "200 OK"
//
//[<Test>]
//let ``test echo2 should return a response with one header for Content_Type of text/plain``() =
//  let _, headers, _ = Async.RunSynchronously (echo2(test()))
//  headers |> should equal (dict [("Content_Type", "text/plain")])
//
//[<Test>]
//let ``test echo2 should return a response with the request body``() =
//  let _, _, body = Async.RunSynchronously (echo2(test()))
//  match body with
//  | Sequence bd ->
//      bd |> should contain (Str "foo=bar")
//      bd |> should contain (Str "bar=baz")
//  | _ -> failwith "Body was not matched successfully"
//
//(* FormUrlEncoded -> Async<FormUrlEncoded> *)
//let OK body = "200 OK", dict [("Content_Type", "text/plain")], body
//
//let app input = OK (Sequence [ for x, y in input -> Str (x + "=" + y) ])
//
//let echo3 (request:IDictionary<string, obj>) =
//  app <!> parseFormUrlEncoded request
//
//[<Test>]
//let ``test echo3 should return a response of 200 OK``() =
//  let status, _, _ = Async.RunSynchronously (echo3(test()))
//  status |> should equal "200 OK"
//
//[<Test>]
//let ``test echo3 should return a response with one header for Content_Type of text/plain``() =
//  let _, headers, _ = Async.RunSynchronously (echo3(test()))
//  headers |> should equal (dict [("Content_Type", "text/plain")])
//
//[<Test>]
//let ``test echo3 should return a response with the request body``() =
//  let _, _, body = Async.RunSynchronously (echo3(test()))
//  match body with
//  | Sequence bd ->
//      bd |> should contain (Str "foo=bar")
//      bd |> should contain (Str "bar=baz")
//  | _ -> failwith "Body was not matched successfully"
//
//(* Agent-based routing *)
//let echoAgent =
//  FrankResource("/",
//    [
//      get <| fun request ->
//        (fun input -> OK (Sequence [ for x, y in input -> Str (x + "=" + y) ])) <!> parseFormUrlEncoded request
//    ])
//
//[<Test>]
//let ``test echoAgent should return a response of 200 OK``() =
//  let status, _, _ = echoAgent.Process(test())
//  status |> should equal "200 OK"
//
//[<Test>]
//let ``test echoAgent should return a response with one header for Content_Type of text/plain``() =
//  let _, headers, _ = echoAgent.Process(test())
//  headers |> should equal (dict [("Content_Type", "text/plain")])
//
//[<Test>]
//let ``test echoAgent should return a response with the request body``() =
//  let _, _, body = echoAgent.Process(test())
//  match body with
//  | Sequence bd ->
//      bd |> should contain (Str "foo=bar")
//      bd |> should contain (Str "bar=baz")
//  | _ -> failwith "Body was not matched successfully"
//
//[<Test>]
//let ``test echoAgent should not respond to an OPTIONS request``() =
//  let optionsRequest =
//    dict [("RequestMethod", box "OPTIONS")
//          ("RequestUri", box "http://wizardsofsmart.net/")]
//  let status, _, _ = echoAgent.Process(optionsRequest)
//  status |> should equal "405 Method not allowed"
//
//[<Test>]
//let ``test echoAgent should respond to an OPTIONS request when extended with Extend withOptions``() =
//  let echoAgent =
//    createResourceAgent "/" [
//      get <| fun request ->
//        (fun input -> OK (Sequence [ for x, y in input -> Str (x + "=" + y) ])) <!> parseFormUrlEncoded request
//    ] |> Extend.withOptions
//
//  let optionsRequest =
//    dict [("RequestMethod", box "OPTIONS")
//          ("RequestUri", box "http://wizardsofsmart.net/")]
//
//  let _, headers, _ = echoAgent.PostAndReply(fun reply -> Process(optionsRequest, reply))
//  headers |> should equal (dict [("Allow", "GET")])
