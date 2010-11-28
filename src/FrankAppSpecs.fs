module Frank.AppSpecs
open System
open System.Collections.Generic
open System.Runtime.Serialization
open System.Text
open Microsoft.Http
open Frack
open Frank
open NaturalSpec

module HelloAppSpecs =
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

  [<Scenario>]
  let ``When creating a Frank application, it should accept a sequence of routes``() =
    let ``creating an app`` routes =
      printMethod ""
      App routes
    Given [ get "/" (render "Hello world!") ]
    |> When ``creating an app``
    |> It should be (fun app -> app.GetType() = typeof<App>)
    |> Verify

  [<Scenario>]
  let ``When invoking a Frank application, it should respond with Hello world!``() =
    let ``invoking an hello world app`` request =
      printMethod ""
      let response = App([get "/" (render "Hello world!")]).Invoke(request)
      response.Content.ReadAsString()
    Given (new HttpRequestMessage("GET", Uri("http://wizardsofsmart.net/")))
    |> When ``invoking an hello world app``
    |> It should equal "Hello world!" 
    |> Verify

  [<Scenario>]
  let ``When running a Frank application that renders an object, it should format the object.``() =
    let request = new HttpRequestMessage("GET", Uri("http://wizardsofsmart.net/ryan"))
    request.Headers.Add("Accept","application/xml")

    let ryan = {Name = "Ryan"; Age = 31}

    let expected =
      let s = new DataContractSerializer(ryan.GetType())
      let sb = new System.Text.StringBuilder()
      use writer = System.Xml.XmlWriter.Create(sb)
      s.WriteObject(writer, ryan)
      sb.ToString()
      
    let ``invoking the application`` request =
      printMethod ""
      let response = App([get "/ryan" (render ryan)], formatters = [|xmlFormatter;jsonFormatter|]).Invoke(request)
      response.Content.ReadAsByteString() |> ByteString.toString

    Given request
    |> When ``invoking the application``
    |> It should equal expected 
    |> Verify