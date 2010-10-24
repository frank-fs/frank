module PathSpecs
open System
open System.Text
open Frack
open NaturalSpec
open BaseSpecs

let env = getEnv "GET"

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
  getPathParts path
  
[<Scenario>]
[<FailsWithType (typeof<ArgumentNullException>)>]
let ``When getting the parts of null, the env should fail with an ArgumentNullException``() =
  Given null
  |> When ``getting path parts``
  |> Verify 
    
[<Scenario>]
[<FailsWithType (typeof<ArgumentNullException>)>]
let ``When getting the parts of "", the env should fail with an ArgumentNullException``() =
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