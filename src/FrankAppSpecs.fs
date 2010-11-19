module FrankSpecs
open System
open System.Collections.Generic
open System.Text
open Microsoft.Http
open Frank
open NaturalSpec

module HelloAppSpecs =
  let formatters = seq {
    yield { ContentType = [| "application/xml";"text/xml" |]
            Format = (fun (o,s,r) ->
                        let f = new System.Xml.Serialization.XmlSerializer(o.GetType()) in f.Serialize(s, o)) }
  }

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
  let ``When creating a Frank application, it should respond with Hello world!``() =
    let helloworld = App [ get "/" (render "Hello world!") ] 
    let ``invoking an hello world app`` (app:App) =
      printMethod ""
      let url = Uri("http://wizardsofsmart.net/") 
      let request = new HttpRequestMessage("GET", url)
      let response = app.Invoke(request)
      response.Content.ReadAsString()
    Given helloworld
    |> When ``invoking an hello world app``
    |> It should equal "Hello world!" 
    |> Verify
