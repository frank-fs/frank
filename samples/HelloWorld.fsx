#r "System.Net"
#r "System.Net.Http"
#r "System.Net.Http.Formatting"
#r "System.Web.Http"
#r "System.Web.Http.SelfHost"
#r @"..\packages\FSharpx.Core.1.8.29\lib\40\Fsharpx.Core.dll"
#r @"..\packages\Newtonsoft.Json.4.5.10\lib\net40\Newtonsoft.Json.dll"
#load @"..\src\System.Net.Http.fs"
#load @"..\src\System.Web.Http.fs"
#load @"..\src\Frank.fs"

open System
open System.Net
open System.Net.Http
open System.Web.Http
open System.Web.Http.SelfHost
open FSharp.Web.Http

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Home =
    let actions: HttpAction[] = [|
        HttpMethod.Get, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "Hello, world!")
    |]

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Contact =
    let actions: HttpAction[] = [|
        HttpMethod.Get, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
        HttpMethod.Post, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
    |]

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Account =
    let actions: HttpAction[] = [|
        HttpMethod.Get, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
    |]

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Addresses =
    let actions: HttpAction[] = [|
        HttpMethod.Get, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
        HttpMethod.Post, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
    |]

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Address =
    let actions: HttpAction[] = [|
        HttpMethod.Get, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
        HttpMethod.Put, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
        HttpMethod.Delete, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
    |]
    
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Demo =
    // NOTE: Ultimately, a Resource should be able to use a type as its name and generate its URI
    type RouteName =
        | Home
        | Contacts
        | Account
        | Addresses
        | Address

    let resourceTree =
        Resource("Home", "", Home.actions,
          [| Resource("Contacts", "contacts", Contact.actions)
             Resource("Account", "account", Account.actions,
              [| Resource("Addresses", "addresses", Addresses.actions,
                  [| Resource("Address", "{addressId}", Address.actions) |])
              |])
          |])

let baseUri = "http://127.0.0.1:1000/"
let config = new System.Web.Http.SelfHost.HttpSelfHostConfiguration(baseUri)
config |> Resource.register Demo.resourceTree 
let server = new HttpSelfHostServer(config)
server.OpenAsync().Wait()

Console.WriteLine("Running on " + baseUri)
Console.WriteLine("Press any key to stop.")
Console.ReadKey() |> ignore

server.CloseAsync().Wait()
