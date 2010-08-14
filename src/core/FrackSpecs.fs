namespace Frack.Specs
open System
open System.Collections.Generic
open System.Text
open System.Web
open Frack
open NaturalSpec

module Fakes =
  open System.Collections.Specialized
  open System.IO
  open System.Web

  let url = new Uri("http://wizardsofsmart.net/something/awesome?name=test&why=how")
    
  let mutable queryString = new NameValueCollection()
  queryString.Add("name","test")
  queryString.Add("why","how")
    
  let mutable headers = new NameValueCollection()
  headers.Add("HTTP_TEST", "value")
  headers.Add("REQUEST_METHOD", "GET")
  
  let createContext m =
    { new HttpContextBase() with
        override this.Request =
          { new HttpRequestBase() with
              override this.HttpMethod = m
              override this.Url = url
              override this.QueryString = queryString
              override this.Headers = headers
              override this.ContentType = "text/plain"
              override this.ContentLength = 5 
              override this.InputStream = new MemoryStream(Encoding.UTF8.GetBytes("Howdy")) :> Stream } }

module EnvSpecs =
  open Fakes

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
  

module PathSpecs =
  open Fakes

  let errors = StringBuilder()
  let env = Env.createEnvironment (createContext "GET") errors

  [<Scenario>]
  let ``When given an environment, it should provide a version of 0.1``() =
    let ``retrieving the version`` env =
      printMethod ""
      env?version
    Given env
    |> When ``retrieving the version``
    |> It should equal (Ver [|0;1|])
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


module AppSpecs =
  open Fakes

  let getEnv m = Env.createEnvironment (createContext m) (StringBuilder())
  let hdrs = dict [| ("Content_Type","text/plain");("Content_Length","5") |] 
  let body = seq { yield "Howdy" } 
  let app env = ( 200, hdrs, body )
  
  let head app =
    fun env -> let status, hdrs, body = app env
               match env?HTTP_METHOD with
                 | Str "HEAD" -> ( status, hdrs, Seq.empty )
                 | _ -> ( status, hdrs, body )
  
  [<Scenario>]
  let ``When running an app that just returns pre-defined values, those values should be returned.``() =
    let ``running an app with predefined values`` env =
      printMethod "200, type = text/plain and length = 5, Howdy"
      app env
    let env = getEnv "GET"
    Given env
    |> When ``running an app with predefined values``
    |> It should equal ( 200, hdrs, body )
    |> Verify
    
  let ``running a middleware for a`` m env =
    printMethod m
    head app env
    
  [<Scenario>]
  let ``When running a middleware on an app handling a GET request, the body should be left alone.``() =
    let env = getEnv "GET"
    Given env
    |> When ``running a middleware for a`` "GET"
    |> It should have (fun result -> match result with _, _, bd -> bd = body)
    |> Verify
  
  [<Scenario>]
  let ``When running a middleware on an app handling a HEAD request, the body should be empty.``() =
    let env = getEnv "HEAD"
    Given env
    |> When ``running a middleware for a`` "HEAD"
    |> It should have (fun result -> match result with _, _, bd -> bd = Seq.empty)
    |> Verify
    
  [<Scenario>]
  let ``When adding the printEnvironment middleware, the body should include more than 1 value.``() =
    let env = getEnv "GET"
    let ``running a middleware to print the environment`` env =
      printMethod ""
      let printEnv = Frack.Middleware.printEnvironment app 
      let result = printEnv env
      match result with
      | _, _, bd -> bd |> Seq.iter (printfn "%s")
      result
    Given env
    |> When ``running a middleware to print the environment``
    |> It should have (fun result -> match result with _, _, bd -> bd |> Seq.length > 1)
    |> Verify