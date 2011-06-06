module Frack.SystemWeb.Tests.SystemWebSpecs

open System
open System.Collections.Specialized
open System.IO
open System.Web
open Frack
open Frack.Hosting.SystemWeb
open NUnit.Framework
open FsUnit

let mutable queryString = new NameValueCollection()
let mutable headers = new NameValueCollection()
  
let mutable status = 0
let mutable statusDescription = ""
let mutable respHeaders = new NameValueCollection()
let mutable outputStream = new MemoryStream()
  
let createContext m uri contentType input =
  { new HttpContextBase() with
      override this.Request =
        { new HttpRequestBase() with
            override this.HttpMethod = m
            override this.Url = new Uri(uri)
            override this.QueryString = queryString
            override this.Headers = headers
            override this.ContentType = contentType
            override this.ContentLength = 5
            override this.InputStream = new MemoryStream(input, false) :> Stream }
      override this.Response =
        { new HttpResponseBase() with
            override this.OutputStream = outputStream :> Stream } }

// Set up the context and other test state
let context = createContext "GET" "http://wizardsofsmart.net/foo/bar?who=you&when=now" "text/plain" "Howdy!"B
headers.Add("x-owin-test", "present")
headers.Add("x-owin-tester", "ryan")
queryString.Add("who", "you")
queryString.Add("when", "now")

let request = context.ToOwinRequest()
    
[<Test>]
let ``It should have a RequestMethod``() =
  request.Keys |> should contain "RequestMethod"
    
[<Test>]
let ``It should have a RequestMethod of GET``() =
  request?RequestMethod |> should equal "GET"

[<Test>]
let ``It should have a RequestUri``() =
  request.Keys |> should contain "RequestUri"

[<Test>]
let ``It should have a RequestUri of /foo/bar?who=you&when=now``() =
  request?RequestUri |> should equal "/foo/bar?who=you&when=now"

[<Test>]
let ``It should have a RequestBody``() =
  request.Keys |> should contain "RequestBody"

[<Test>]
let ``It should have a RequestBody that is an Async computation``() =
  Assert.IsInstanceOf(typeof<Async<ArraySegment<byte>>>, request?RequestBody)

[<Test>]
[<Ignore>]
let ``It should have a RequestBody that yields "Howdy!"``() =
  async {
    let! bs = request?RequestBody :?> Async<_> |> Stream.readToEnd
    bs |> should equal "Howdy!"B } |> Async.RunSynchronously