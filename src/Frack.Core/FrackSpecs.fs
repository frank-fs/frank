module FrackSpecs
open Frack
open NaturalSpec
open BaseSpecs

let env = getEnv "GET"
  
let ``with value of`` expected key (col:Environment) =
  printMethod expected
  col.[key] = expected
  
[<Scenario>]
let ``When given an environment, it should provide the url scheme``() =
  let ``retrieving the url scheme`` env =
    printMethod ""
    env?url_scheme
  Given env
  |> When ``retrieving the url scheme``
  |> It should equal (Str "http")
  |> Verify
  
[<Scenario>]
let ``When given an environment, it should provide the http method``() =
  Given env
  |> It should have ("HTTP_METHOD" |> ``with value of`` (Str "GET"))
  |> Verify
  
[<Scenario>]
let ``When given an environment, it should provide the content type``() =
  Given env
  |> It should have ("CONTENT_TYPE" |> ``with value of`` (Str "text/plain"))
  |> Verify

[<Scenario>]
let ``When given an environment, it should provide the content length``() =
  Given env
  |> It should have ("CONTENT_LENGTH" |> ``with value of`` (Int 5))
  |> Verify
  
[<Scenario>]
let ``When given an environment, it should provide the server name``() =
  Given env
  |> It should have ("SERVER_NAME" |> ``with value of`` (Str "wizardsofsmart.net"))
  |> Verify
  
[<Scenario>]
let ``When given an environment, it should provide the port``() =
  Given env
  |> It should have ("SERVER_PORT" |> ``with value of`` (Str "80"))
  |> Verify
  
[<Scenario>]
let ``When given an environment, it should provide the script name``() =
  Given env
  |> It should have ("SCRIPT_NAME" |> ``with value of`` (Str "/something"))
  |> Verify
  
[<Scenario>]
let ``When given an environment, it should provide the path info``() =
  Given env
  |> It should have ("PATH_INFO" |> ``with value of`` (Str "/awesome"))
  |> Verify
    
[<Scenario>]
let ``When given an environment, it should provide the stringified query string``() =
  Given env
  |> It should have ("QUERY_STRING" |> ``with value of`` (Str "name=test&why=how"))
  |> Verify
    
[<Scenario>]
let ``When given an environment, it should provide the headers``() =
  Given env
  |> It should have ("HTTP_TEST" |> ``with value of`` (Str "value"))
  |> Verify

[<Scenario>]
let ``When running an app that just returns pre-defined values, those values should be returned.``() =
  let ``running an app with predefined values`` env =
    printMethod "200, type = text/plain and length = 5, Howdy"
    app env
  Given getEnv "GET"
  |> When ``running an app with predefined values``
  |> It should equal ( 200, hdrs, body )
  |> Verify