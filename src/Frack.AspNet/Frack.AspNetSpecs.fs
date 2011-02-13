namespace FrackAspNetSpecs
open System
open System.Collections.Specialized
open System.IO
open System.Net
open System.Web
open Frack
open Frack.Hosting.AspNet
open NUnit.Framework
open BaseSpecs

module Fakes =
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

module ``Given an ASPNET context`` =
  open Fakes

  // Set up the context and other test state
  let context = createContext "GET" "http://wizardsofsmart.net/foo/bar?who=you&when=now" "text/plain" "Howdy!"B
  headers.Add("x-owin-test", "present")
  headers.Add("x-owin-tester", "ryan")
  queryString.Add("who", "you")
  queryString.Add("when", "now")

  module ``When I convert it into an OWIN request`` =
    let request = context.ToOwinRequest()
        
    [<Test>]
    let ``It should have a RequestMethod``() =
      request.ContainsKey("RequestMethod") |> ``is true``
        
    [<Test>]
    let ``It should have a RequestMethod of GET``() =
      request?RequestMethod == "GET"

    [<Test>]
    let ``It should have a RequestUri``() =
      request.ContainsKey("RequestUri") |> ``is true``

    [<Test>]
    let ``It should have a RequestUri of /foo/bar?who=you&when=now``() =
      request?RequestUri == "/foo/bar?who=you&when=now"

    [<Test>]
    let ``It should have a RequestBody``() =
      request.ContainsKey("RequestBody") |> ``is true``

    [<Test>]
    let ``It should have a RequestBody that is an Action delegate``() =
      Assert.IsInstanceOf(typeof<Action<Action<ArraySegment<byte>>, Action<exn>>>, request?RequestBody)

    [<Test>]
    let ``It should have a RequestBody that yields "Howdy!" to a continuation``() =
      Request.readToEndWithContinuations(request, Action<_>(fun bs -> bs == "Howdy!"B), Action<_>(fun e -> ()))
