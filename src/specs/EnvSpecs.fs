module Frack.Specs.EnvSpecs
open System
open System.Web
open Frack
open Frack.Specs.Fakes
open NaturalSpec

let ``creating the environment`` (ctx:HttpContextBase) =
  printMethod ""
  Env.create ctx

[<Scenario>]
let ``When reading in a context, it should return an environment``() =
  Given context
  |> When ``creating the environment``
  |> It should be (fun e -> e.GetType() = typeof<System.Text.StringBuilder * Environment>) 
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
  |> It should be (fun p -> p.GetType() = typeof<string * string>)
  |> It should equal (String.Empty,String.Empty)
  |> Verify 
  
[<Scenario>]
let ``When getting the parts of /something, the parts should be two empty strings``() =
  Given "/something"
  |> When ``getting path parts``
  |> It should be (fun p -> p.GetType() = typeof<string * string>)
  |> It should equal ("/something",String.Empty)
  |> Verify

[<Scenario>]
let ``When getting the parts of /something/, the parts should be two empty strings``() =
  Given "/something/"
  |> When ``getting path parts``
  |> It should be (fun p -> p.GetType() = typeof<string * string>)
  |> It should equal ("/something",String.Empty)
  |> Verify

[<Scenario>]
let ``When getting the parts of /something/awesome, the parts should be two empty strings``() =
  Given "/something/awesome"
  |> When ``getting path parts``
  |> It should be (fun p -> p.GetType() = typeof<string * string>)
  |> It should equal ("/something","/awesome")
  |> Verify 

[<Scenario>]
let ``When getting the parts of /something/awesome/, the parts should be two empty strings``() =
  Given "/something/awesome/"
  |> When ``getting path parts``
  |> It should be (fun p -> p.GetType() = typeof<string * string>)
  |> It should equal ("/something","/awesome")
  |> Verify 

[<Scenario>]
let ``When getting the parts of /something/awesome/and/brilliant, the parts should be two empty strings``() =
  Given "/something/awesome/and/brilliant"
  |> When ``getting path parts``
  |> It should be (fun p -> p.GetType() = typeof<string * string>)
  |> It should equal ("/something","/awesome/and/brilliant")
  |> Verify 
  
[<Scenario>]
let ``When getting the parts of /something/awesome/and/brilliant/, the parts should be two empty strings``() =
  Given "/something/awesome/and/brilliant/"
  |> When ``getting path parts``
  |> It should be (fun p -> p.GetType() = typeof<string * string>)
  |> It should equal ("/something","/awesome/and/brilliant")
  |> Verify 