namespace WebHost

open System
open System.Net
open System.Net.Http
open System.Web
open System.Web.Http
open System.Web.Http.HttpResource
open Newtonsoft.Json
open Frank

[<CLIMutable>]
[<JsonObject(MemberSerialization=MemberSerialization.OptOut)>]
type Car = {
    Make : string
    Model : string
}

type Global() =
    inherit System.Web.HttpApplication() 

    member x.Application_Start() =
        let values = [| { Make = "Ford"; Model = "Mustang" }; { Make = "Nissan"; Model = "Titan" } |]

        // Use the HttpRequestMessage extension
//        let carsHandler (request: HttpRequestMessage) =
//            request.CreateResponse(HttpStatusCode.OK, values)
//            |> async.Return

        // Use the Frank runConneg helper to generate the response
        let carsHandler =
            runConneg GlobalConfiguration.Configuration.Formatters
            <| fun _ -> async.Return values
            
        // Create a resource using the HttpResource.route function
        let carsResource = route "/api/cars" <| get carsHandler

        // Register HTTP resources using the HttpResource.register function
        GlobalConfiguration.Configure(fun config -> register [carsResource] config)
