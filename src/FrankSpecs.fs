module FrankSpecs
open System
open System.Collections.Generic
open System.Text
open Microsoft.Http
open Frack
open Frank
open NaturalSpec

//module RouteSpecs =

module HelloAppSpecs =
  let helloHandler = frank { do! putContent (HttpContent.Create("Hello world!", "text/plain")) }

  [<Scenario>]
  let ``When creating a Frank applicaion, it should accept a sequence of routes``() =
    let ``creating an app`` routes =
      printMethod ""
      FrankApp.init routes
    Given [ get "/" helloHandler ]
    |> When ``creating an app``
    |> It should be (fun app -> app.GetType() = typeof<App>)
    |> Verify

  [<Scenario>]
  let ``When creating a Frank application, it should respond with Hello world!``() =
    let helloworld = FrankApp.init [ get "/" helloHandler ] 
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
