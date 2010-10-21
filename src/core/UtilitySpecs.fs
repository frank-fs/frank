module UtilitySpecs
open System.Collections.Generic
open System.Text
open Frack
open NaturalSpec
open BaseSpecs

let errors = StringBuilder()
let request = getRequest "GET" errors
  
let ``with value of`` expected key (col:IDictionary<string, Value>) =
  printMethod expected
  col.[key] = expected
  
[<Scenario>]
let ``When given an requestironment, it should provide the url scheme``() =
  let ``retrieving the url scheme`` request =
    printMethod ""
    request?url_scheme
  Given request
  |> When ``retrieving the url scheme``
  |> It should equal (Str "http")
  |> Verify
  
[<Scenario>]
let ``When given an requestironment, it should provide the http method``() =
  Given request
  |> It should have ("HTTP_METHOD" |> ``with value of`` (Str "GET"))
  |> Verify
  
[<Scenario>]
let ``When given an requestironment, it should provide the content type``() =
  Given request
  |> It should have ("CONTENT_TYPE" |> ``with value of`` (Str "text/plain"))
  |> Verify

[<Scenario>]
let ``When given an requestironment, it should provide the content length``() =
  Given request
  |> It should have ("CONTENT_LENGTH" |> ``with value of`` (Int 5))
  |> Verify
  
[<Scenario>]
let ``When given an requestironment, it should provide the server name``() =
  Given request
  |> It should have ("SERVER_NAME" |> ``with value of`` (Str "wizardsofsmart.net"))
  |> Verify
  
[<Scenario>]
let ``When given an requestironment, it should provide the port``() =
  Given request
  |> It should have ("SERVER_PORT" |> ``with value of`` (Str "80"))
  |> Verify
  
[<Scenario>]
let ``When given an requestironment, it should provide the script name``() =
  Given request
  |> It should have ("SCRIPT_NAME" |> ``with value of`` (Str "/something"))
  |> Verify
  
[<Scenario>]
let ``When given an requestironment, it should provide the path info``() =
  Given request
  |> It should have ("PATH_INFO" |> ``with value of`` (Str "/awesome"))
  |> Verify
    
[<Scenario>]
let ``When given an requestironment, it should provide the stringified query string``() =
  Given request
  |> It should have ("QUERY_STRING" |> ``with value of`` (Str "name=test&why=how"))
  |> Verify
    
[<Scenario>]
let ``When given an requestironment, it should provide the headers``() =
  Given request
  |> It should have ("HTTP_TEST" |> ``with value of`` (Str "value"))
  |> Verify