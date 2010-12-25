module Frack.RequestSpecs
open System
open System.Collections.Specialized
open System.IO
open System.Text
open Owin
open Frack
open NUnit.Framework
open BaseSpecs

// For Response and Application tests
//let hdrs = dict [| ("Content_Type", seq { yield "text/plain" })
//                   ("Content_Length", seq { yield "5" }) |] 
//
//let app = Application(fun request -> async {
//  // Need to get rid of the need for casting.
//  return Response("200 OK", hdrs, (fun () -> "Howdy"B |> Seq.map (fun o -> o :> obj))) :> Owin.IResponse })
//[<Scenario>]
//let ``When running an app that just returns pre-defined values, those values should be returned.``() =
//  let ``running an app with predefined values`` request =
//    printMethod "200, type = text/plain and length = 5, Howdy"
//    app request
//  Given getEnv "GET"
//  |> When ``running an app with predefined values``
//  |> It should equal ( 200, hdrs, body )
//  |> Verify

// Arrange
let hdrs = dict [| ("Accept", seq { yield "text/plain;application/xml" }) |]
let items = new System.Collections.Generic.Dictionary<string, obj>()
let asyncReadBody(buffer, offset, count) = async {
  let stream = new SeqStream("Howdy"B)
  return! stream.AsyncRead(buffer, offset, count) }
let getRequest m = Request.FromAsync(m, "/something/awesome?name=test&why=how", hdrs, items, asyncReadBody)

// Act
let request = getRequest "GET"
  
// Assert
[<Test>]
let ``Request should have an HTTP method of GET``() = request.Method == "GET"
  
[<Test>]
let ``Request should have a Uri of /something/awesome?name=test&why=how``() =
  request.Uri == "/something/awesome?name=test&why=how" 
    
[<Test>]
let ``Request should contain the headers``() = request.Headers == hdrs