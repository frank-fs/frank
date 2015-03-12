#r "System.Net"
#r "System.Net.Http"
#I "../../packages"
#r "Microsoft.AspNet.WebApi.Client/lib/net45/System.Net.Http.Formatting.dll"
#r "Microsoft.AspNet.WebApi.Core/lib/net45/System.Web.Http.dll"
#r "Newtonsoft.Json/lib/net45/Newtonsoft.Json.dll"
#r "bin/Debug/Frank.dll"

open System
open System.Net
open System.Net.Http
open System.Web.Http
open Frank.Web.Http

[<Route("api/test")>]
module Test =
    let get(request: HttpRequestMessage) = 3

module Test2 =
    [<Route("api/test2/{x}")>]
    let get(request: HttpRequestMessage) x = x

module Test3 =
    [<Route("api/test3/{x}")>]
    let get(request: HttpRequestMessage, x) = x

module Test4 =
    [<HttpGet>]
    let get(request: HttpRequestMessage) = 3

type TestApi() =
    inherit ApiController()
    member x.Get() = 3

type TestIApi() =
    interface Controllers.IHttpController with
        member x.ExecuteAsync(controllerContext, cancellationToken) =
            controllerContext.Request.CreateResponse(HttpStatusCode.OK, 2)
            |> Threading.Tasks.Task.FromResult

let types = Reflection.Assembly.GetExecutingAssembly().GetTypes()
let validControllers =
    types
    |> Array.filter (fun x -> FlexControllerTypeResolver.IsControllerTypePredicate x)


//[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
//module Home =
//    let actions: HttpAction[] = [|
//        HttpMethod.Get, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "Hello, world!")
//    |]
//
//[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
//module Contact =
//    let actions: HttpAction[] = [|
//        HttpMethod.Get, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
//        HttpMethod.Post, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
//    |]
//
//[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
//module Account =
//    let actions: HttpAction[] = [|
//        HttpMethod.Get, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
//    |]
//
//[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
//module Addresses =
//    let actions: HttpAction[] = [|
//        HttpMethod.Get, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
//        HttpMethod.Post, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
//    |]
//
//[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
//module Address =
//    let actions: HttpAction[] = [|
//        HttpMethod.Get, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
//        HttpMethod.Put, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
//        HttpMethod.Delete, fun request -> async.Return <| request.CreateResponse(HttpStatusCode.OK, "<html></html>")
//    |]
//    
//[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
//module Demo =
//    // NOTE: Ultimately, a Resource should be able to use a type as its name and generate its URI
//    type RouteName =
//        | Home
//        | Contacts
//        | Account
//        | Addresses
//        | Address
//
//    let resourceTree =
//        Resource("Home", "", Home.actions,
//          [| Resource("Contacts", "contacts", Contact.actions)
//             Resource("Account", "account", Account.actions,
//              [| Resource("Addresses", "addresses", Addresses.actions,
//                  [| Resource("Address", "{addressId}", Address.actions) |])
//              |])
//          |])
