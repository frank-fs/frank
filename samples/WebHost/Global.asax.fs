namespace WebHost

open System
open System.Net
open System.Net.Http
open System.Web
open System.Web.Http
open System.Web.Http.HttpResource
open Newtonsoft.Json

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

        let carsHandler (request: HttpRequestMessage) =
            request.CreateResponse(HttpStatusCode.OK, values)
            |> async.Return
            
        let carsResource = route "/api/cars" <| get carsHandler

        GlobalConfiguration.Configure(fun config -> register [carsResource] config)
