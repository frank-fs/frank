module Frack.Specs.EnvSpecs
open System
open System.Collections.Generic
open System.Text
open System.Web
open Frack
open Frack.Specs.Fakes
open Frack.Utility
open NaturalSpec

let errors = StringBuilder()
let env = Env.createEnvironment (createContext "GET") errors

let ``with value of`` expected key (col:IDictionary<string,Value>) =
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
let ``When given an environment, it should provide a version of 0.1``() =
  let ``retrieving the version`` env =
    printMethod ""
    env?version
  Given env
  |> When ``retrieving the version``
  |> It should equal (Ver (0,1))
  |> Verify
  
let ``getting path parts`` (path:string) =
  printMethod ""
  Env.getPathParts path

[<Scenario>]
[<FailsWithType (typeof<ArgumentNullException>)>]
let ``When getting the parts of null, the request should fail with an ArgumentNullException``() =
  Given null
  |> When ``getting path parts``
  |> Verify 
  
[<Scenario>]
[<FailsWithType (typeof<ArgumentNullException>)>]
let ``When getting the parts of "", the request should fail with an ArgumentNullException``() =
  Given ""
  |> When ``getting path parts``
  |> Verify 
  
[<Scenario>]
let ``When getting the parts of /, the parts should be two empty strings``() =
  Given "/"
  |> When ``getting path parts``
  |> It should equal (String.Empty,String.Empty)
  |> Verify 
  
[<Scenario>]
let ``When getting the parts of /something, the parts should be two empty strings``() =
  Given "/something"
  |> When ``getting path parts``
  |> It should equal ("/something",String.Empty)
  |> Verify

[<Scenario>]
let ``When getting the parts of /something/, the parts should be two empty strings``() =
  Given "/something/"
  |> When ``getting path parts``
  |> It should equal ("/something",String.Empty)
  |> Verify

[<Scenario>]
let ``When getting the parts of /something/awesome, the parts should be two empty strings``() =
  Given "/something/awesome"
  |> When ``getting path parts``
  |> It should equal ("/something","/awesome")
  |> Verify 

[<Scenario>]
let ``When getting the parts of /something/awesome/, the parts should be two empty strings``() =
  Given "/something/awesome/"
  |> When ``getting path parts``
  |> It should equal ("/something","/awesome")
  |> Verify 

[<Scenario>]
let ``When getting the parts of /something/awesome/and/brilliant, the parts should be two empty strings``() =
  Given "/something/awesome/and/brilliant"
  |> When ``getting path parts``
  |> It should equal ("/something","/awesome/and/brilliant")
  |> Verify 
  
[<Scenario>]
let ``When getting the parts of /something/awesome/and/brilliant/, the parts should be two empty strings``() =
  Given "/something/awesome/and/brilliant/"
  |> When ``getting path parts``
  |> It should equal ("/something","/awesome/and/brilliant")
  |> Verify 