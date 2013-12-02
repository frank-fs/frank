#r "System.Net"
#r "System.Net.Http"
#r "System.Net.Http.Formatting"
#r "System.Web.Http"
#r @"..\packages\FSharpx.Core.1.8.29\lib\40\Fsharpx.Core.dll"
#r @"..\packages\Newtonsoft.Json.4.5.10\lib\net40\Newtonsoft.Json.dll"
#load @"..\src\Frank.fs"

open System
open System.Net
open System.Net.Http
open System.Web.Http

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
