(* # F# Implementation of System.Web.Http.Controllers

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011-2012, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
namespace FSharp.Web.Http.Controllers

open System
open System.Collections.ObjectModel
open System.Linq
open System.Net
open System.Net.Http
open System.Threading.Tasks
open System.Web.Http
open System.Web.Http.Controllers
open System.Web.Http.ModelBinding

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Constants =
    [<CompiledName("Actions")>]
    let actions = "FSharp.Actions"

/// All FSharp applications take a single "request" parameter of type HttpRequestMessage.
type FSharpHttpParameterDescriptor(actionDescriptor) =
    // TODO: Determine if we want to do any model binding or also pass route data params.
    inherit HttpParameterDescriptor(actionDescriptor)
    override x.ParameterName = "request"
    override x.ParameterType = typeof<HttpRequestMessage>
    override x.Prefix = Unchecked.defaultof<_>
    override x.ParameterBinderAttribute
        with get() = Unchecked.defaultof<_>
        and set(v) = ()

/// All FSharp actions take the request parameter and return an HttpResponseMessage.
type FSharpHttpActionDescriptor(controllerDescriptor, actionName, app: HttpApplication) as x =
    inherit HttpActionDescriptor(controllerDescriptor)
    let parameters = new Collection<HttpParameterDescriptor>([| new FSharpHttpParameterDescriptor(x) |])
    override x.ActionName = actionName
    override x.ResultConverter = Unchecked.defaultof<_>
    override x.ReturnType = typeof<HttpResponseMessage>
    override x.GetParameters() = parameters
    override x.ExecuteAsync(controllerContext, arguments, cancellationToken) =
        // TODO: use route data as the arguments?
        // TODO: insert the controller context into the Request.Properties?
        // TODO: just use the controller context?
        // NOTE: You are a fool to use this.
        let runner = async {
            let! value = app controllerContext.Request
            return value :> obj }
        Async.StartAsTask(runner, cancellationToken = cancellationToken)

    member x.AsyncExecute(controllerContext: HttpControllerContext) = app controllerContext.Request

/// The FSharpControllerActionSelector pattern matches the HttpMethod and matches to the appropriate handler.
type FSharpControllerActionSelector() =
    member x.GetActionMapping (controllerDescriptor: HttpControllerDescriptor) =
        if controllerDescriptor = null then raise <| ArgumentNullException("controllerDescriptor")
        // TODO: Cache the results
        let actions = controllerDescriptor.Properties.[Constants.actions] :?> HttpAction[]
        actions
        |> Array.map (fun (httpMethod, app) -> new FSharpHttpActionDescriptor(controllerDescriptor, httpMethod.Method, app))

    interface IHttpActionSelector with
        member x.GetActionMapping(controllerDescriptor) =
            // TODO: Cache the results
            x.GetActionMapping(controllerDescriptor).ToLookup((fun desc -> desc.ActionName), (fun (desc) -> desc :> HttpActionDescriptor), StringComparer.OrdinalIgnoreCase)

        member x.SelectAction(controllerContext) =
            if controllerContext = null then raise <| ArgumentNullException("controllerContext")
            let httpMethod = controllerContext.Request.Method
            let controllerDescriptor = controllerContext.ControllerDescriptor
            let actionMapping = x.GetActionMapping(controllerDescriptor) 
            let matchingActions = actionMapping |> Array.filter (fun desc -> desc.ActionName = httpMethod.Method)
            if matchingActions.Length = 0 then
                raise (new HttpResponseException(controllerContext.Request.CreateErrorResponse(HttpStatusCode.MethodNotAllowed, "Method Not Supported")))
            else
                // TODO: Test for ambiguity, but should only ever have a single use of a given HTTP method per resource.
                matchingActions.[0] :> HttpActionDescriptor
