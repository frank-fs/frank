module Frack.Specs.EnvironmentSpecs
open Frack
open Frack.Specs.Fakes
open NaturalSpec

let errors, env = Env.create context

let ``an entry for`` (key:string) (col:Map<string,string>) =
  let msg = "an entry for " + key
  printMethod msg
  col.ContainsKey(key)

let ``with value of`` expected key (col:Map<string,string>) =
  let msg = "a value of " + expected + " for " + key
  printMethod msg
  col.[key] = expected

[<Scenario>]
let ``When given an environment, it should provide the http method``() =
  let ``retrieving the http method`` (env:Environment) =
    printMethod "http method"
    env.HTTP_METHOD
  Given env
  |> When ``retrieving the http method``
  |> It should equal "GET"
  |> Verify

[<Scenario>]
let ``When given an environment, it should provide the url scheme``() =
  let ``retrieving the url scheme`` (env:Environment) =
    printMethod "url scheme"
    env.UrlScheme
  Given env
  |> When ``retrieving the url scheme``
  |> It should equal "http"
  |> Verify

[<Scenario>]
let ``When given an environment, it should provide the content type``() =
  let ``retrieving the content type`` (env:Environment) =
    printMethod "content type"
    env.CONTENT_TYPE
  Given env
  |> When ``retrieving the content type``
  |> It should equal "text/plain"
  |> Verify

[<Scenario>]
let ``When given an environment, it should provide the content length``() =
  let ``retrieving the content length`` (env:Environment) =
    printMethod "content length"
    env.CONTENT_LENGTH
  Given env
  |> When ``retrieving the content length``
  |> It should equal 5
  |> Verify

[<Scenario>]
let ``When given an environment, it should provide the server name``() =
  let ``retrieving the server name`` (env:Environment) =
    printMethod "server name"
    env.SERVER_NAME
  Given env
  |> When ``retrieving the server name``
  |> It should equal "wizardsofsmart.net"
  |> Verify

[<Scenario>]
let ``When given an environment, it should provide the port``() =
  let ``retrieving the port`` (env:Environment) =
    printMethod "port"
    env.SERVER_PORT
  Given env
  |> When ``retrieving the port``
  |> It should equal 80
  |> Verify

[<Scenario>]
let ``When given an environment, it should provide the script name``() =
  let ``retrieving the script name`` (env:Environment) =
    printMethod "script name"
    env.SCRIPT_NAME
  Given env
  |> When ``retrieving the script name``
  |> It should equal "/something"
  |> Verify

[<Scenario>]
let ``When given an environment, it should provide the path info``() =
  let ``retrieving the path info`` (env:Environment) =
    printMethod "path info"
    env.PATH_INFO
  Given env
  |> When ``retrieving the path info``
  |> It should equal "/awesome"
  |> Verify
  
[<Scenario>]
let ``When given an environment, it should provide the stringified query string``() =
  let ``retrieving the query string`` (env:Environment) =
    printMethod "stringified query string"
    env.QUERY_STRING
  Given env
  |> When ``retrieving the query string``
  |> It should equal "name=test&why=how"
  |> Verify
  
[<Scenario>]
let ``When given an environment, it should provide the query string``() =
  let ``retrieving the query string`` (env:Environment) =
    printMethod "query string"
    env.QueryString
  Given env
  |> When ``retrieving the query string``
  |> It should have (``an entry for`` "name")
  |> It should have ("name" |> ``with value of`` "test")
  |> It should have (``an entry for`` "why")
  |> It should have ("why" |> ``with value of`` "how")
  |> Verify

[<Scenario>]
let ``When given an environment, it should provide the headers``() =
  let ``retrieving the headers`` (env:Environment) =
    printMethod "headers"
    env.HEADERS
  Given env
  |> When ``retrieving the headers``
  |> It should have (``an entry for`` "HTTP_TEST")
  |> It should have ("HTTP_TEST" |> ``with value of`` "value")
  |> Verify

[<Scenario>]
let ``When given an environment, it should provide a version of 0.1``() =
  let ``retrieving the version`` (env:Environment) =
    printMethod "version"
    env.Version
  Given env
  |> When ``retrieving the version``
  |> It should equal (0,1)
  |> Verify