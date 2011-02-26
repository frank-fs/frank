module Frank.AppSpecs
open System
open System.Collections.Generic
open System.Runtime.Serialization
open System.Text
open Microsoft.Http
open Frack.Collections
open Frank
open NUnit.Framework

module HelloAppSpecs =

  [<Test>]
  let ``When invoking a Frank application, it should respond with Hello world!``() =
    let request = new HttpRequestMessage("GET", Uri("http://wizardsofsmart.net/"))
    let response = App([get "/" (render "Hello world!")]).Invoke(request)
    Assert.AreEqual("Hello world!", response.Content.ReadAsString())

module ConnegSpecs =
  [<DataContract>]
  type TestType = {
    [<field: DataMember(Name = "name")>]
    Name:string;
    [<field: DataMember(Name = "age")>]
    Age:int }

  let xmlFormatter = {
    ContentType = [| "application/xml";"text/xml" |]
    Format = (fun (o,s,r) -> let f = new DataContractSerializer(o.GetType()) in f.WriteObject(s, o)) }

  let jsonFormatter = {
    ContentType = [| "application/json";"text/json" |]
    Format = (fun (o,s,r) -> let f = new Json.DataContractJsonSerializer(o.GetType()) in f.WriteObject(s, o)) }

  let ryan = {Name = "Ryan"; Age = 31}

  let requestAccepting (contentType:string) =
    let request = new HttpRequestMessage("GET", Uri("http://wizardsofsmart.net/ryan"))
    request.Headers.Add("Accept", contentType)
    request

  let application = App([get "/ryan" (render ryan)], formatters = [|xmlFormatter;jsonFormatter|])

  let expect formatter request =
    use stream = new System.IO.MemoryStream()
    formatter.Format(ryan :> obj, stream :> System.IO.Stream, request)
    stream.ToArray() |> ByteString.toString
      
  let invoke (application:App) request =
    let response = application.Invoke(request) 
    response.Content.ReadAsByteString() |> ByteString.toString

  [<Test>]
  let ``When running a Frank application that renders an object, it should format the object as XML.``() =
    let request = requestAccepting "application/xml"
    let expected = expect xmlFormatter request 
    let actual = request |> invoke application
    Assert.AreEqual(expected, actual)

  [<Test>]
  let ``When running a Frank application that renders an object, it should format the object as JSON.``() =
    let request = requestAccepting "application/json"
    let expected = expect jsonFormatter request 
    let actual = request |> invoke application
    Assert.AreEqual(expected, actual)

  [<Test>]
  [<ExpectedException (typeof<ArgumentException>)>]
  let ``When running a Frank application that doesn't respond to the Accept header, it should fail.``() =
    let request = requestAccepting "application/x-some-unknown-type"
    let expected = expect xmlFormatter request
    let actual = request |> invoke application
    Assert.AreEqual(expected, actual)
