(* # F# Implementation of System.Web.Http.Dispatcher

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011-2012, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
namespace FSharp.Web.Http.Dispatcher

open System
open System.Collections.Generic
open System.Collections.ObjectModel
open System.Diagnostics.Contracts
open System.Net
open System.Net.Http
open System.Reflection
open System.Threading.Tasks
open System.Web.Http
open System.Web.Http.Controllers
open System.Web.Http.Dispatcher
open System.Web.Http.Filters
open System.Web.Http.Hosting
open System.Web.Http.Properties
open System.Web.Http.Routing
open FSharp.Web.Http.Controllers

// Ultimately, `name` should be able to be a Discriminated Union. However, the generics are tricky at this time.
type Resource(name: string, routeTemplate, actions: HttpAction[], ?nestedResources) =
    let nestedResources = defaultArg nestedResources Array.empty

    member x.Name = name
    member x.RouteTemplate = routeTemplate
    member x.Actions = actions
    member x.NestedResources = nestedResources

    static member private GroupFilters (filters: Collection<FilterInfo>) =
        if filters <> null && filters.Count > 0 then
            let rec split i (actionFilters, authFilters, exceptionFilters) =
                let result =
                    match filters.[i].Instance with
                    | :? IActionFilter as actionFilter ->
                        actionFilter::actionFilters, authFilters, exceptionFilters
                    | :? IAuthorizationFilter as authFilter ->
                        actionFilters, authFilter::authFilters, exceptionFilters
                    | :? IExceptionFilter as exceptionFilter ->
                        actionFilters, authFilters, exceptionFilter::exceptionFilters
                    | _ -> actionFilters, authFilters, exceptionFilters
                if i < filters.Count then
                    split (i+1) result
                else result
            split 0 ([], [], [])
        else [], [], []

    static member internal InvokeWithAuthFilters(actionContext, cancellationToken, filters, continuation) =
        Contract.Assert(actionContext <> null)
        List.fold (fun cont (filter: IAuthorizationFilter) ->
            fun () -> filter.ExecuteAuthorizationFilterAsync(actionContext, cancellationToken, Func<_>(cont)))
            continuation
            filters

    static member internal InvokeWithActionFilters(actionContext, cancellationToken, filters, continuation) =
        Contract.Assert(actionContext <> null)
        List.fold (fun cont (filter: IActionFilter) ->
            fun () -> filter.ExecuteActionFilterAsync(actionContext, cancellationToken, Func<_>(cont)))
            continuation
            filters

    static member internal InvokeWithExceptionFilters(result: Task<HttpResponseMessage>, actionContext, cancellationToken, filters: IExceptionFilter list) =
        Contract.Assert(result <> null)
        Contract.Assert(actionContext <> null)

        Async.StartAsTask(async {
            try
                return! Async.AwaitTask result
            with
            | ex ->
                let executedContext = new HttpActionExecutedContext(actionContext, ex)
                let rec loop response (filters: IExceptionFilter list) = async {
                    match filters with
                    | [] -> return response
                    | filter::filters ->
                        let! _ = Async.AwaitIAsyncResult <| filter.ExecuteExceptionFilterAsync(executedContext, cancellationToken)
                        if executedContext.Response <> null then
                            return! loop executedContext.Response filters
                        else
                            // Return immediately with an error response and status code 500.
                            // TODO: Can we make the status code configurable or intelligent?
                            return executedContext.Request.CreateErrorResponse(HttpStatusCode.InternalServerError, executedContext.Exception)
                }
                return! loop Unchecked.defaultof<HttpResponseMessage> filters
        }, cancellationToken = cancellationToken)

    interface IHttpController with
        member x.ExecuteAsync(controllerContext, cancellationToken) =
            let controllerDescriptor = controllerContext.ControllerDescriptor
            let services = controllerDescriptor.Configuration.Services
            let actionSelector = services.GetActionSelector()
            let actionDescriptor = actionSelector.SelectAction(controllerContext) :?> FSharpHttpActionDescriptor
            let actionContext = new HttpActionContext(controllerContext, actionDescriptor)
            let filters = actionDescriptor.GetFilterPipeline()
            let actionFilters, authFilters, exceptionFilters = Resource.GroupFilters filters
            let authResult =
                (Resource.InvokeWithAuthFilters(actionContext, cancellationToken, authFilters, fun () ->
                    // Ignore binding for now.
                    Resource.InvokeWithActionFilters(actionContext, cancellationToken, actionFilters, fun () ->
                        Async.StartAsTask(async {
                            try
                                // No IHttpActionInvoker is necessary as we always return an HttpResponseMessage.
                                // In this case, we also expose an Async<HttpResponseMessage> rather than the standard Task<obj>.
                                return! actionDescriptor.AsyncExecute controllerContext
                            with
                            | :? HttpResponseException as ex -> 
                                // Return the response from the HttpResponseException.
                                let response = ex.Response
                                // Ensure the response has the original request message.
                                if response <> null && response.RequestMessage = null then
                                    response.RequestMessage <- actionContext.Request
                                return response
                        }, cancellationToken = cancellationToken)
                    )()
                )())
            let result = Resource.InvokeWithExceptionFilters(authResult, actionContext, cancellationToken, exceptionFilters)
            result

type FSharpControllerTypeResolver(resource: Resource) =
    let rec aggregate (resource: Resource) =
        [|
            yield resource.GetType()
            if resource.NestedResources |> Seq.isEmpty then () else
            for res in resource.NestedResources do
                yield! aggregate res
        |]
    let types = aggregate resource :> ICollection<_>

    interface IHttpControllerTypeResolver with
        // GetControllerTypes takes an IAssembliesResolver, which is unnecessary in this implementation.
        member x.GetControllerTypes(assembliesResolver) = types

type FSharpControllerSelector(configuration: HttpConfiguration, controllerMapping: IDictionary<_,_>) =
    let ControllerKey = "controller"

    member x.GetControllerName (request: HttpRequestMessage) =
        if request = null then raise <| ArgumentNullException("request")
        let routeData = request.GetRouteData()
        if routeData = null then Unchecked.defaultof<_> else
        routeData.Route.RouteTemplate
//        let success, controllerName = routeData.Values.TryGetValue(ControllerKey)
//        controllerName :?> string

    member x.SelectController(request) =
        if request = null then
            raise <| ArgumentNullException("request")

        let controllerName = x.GetControllerName request
//        if String.IsNullOrEmpty controllerName then
        if controllerName = null then
            raise <| new HttpResponseException(request.CreateErrorResponse(HttpStatusCode.NotFound, request.RequestUri.AbsoluteUri))

        let success, controllerDescriptor = controllerMapping.TryGetValue(controllerName)
        if not success then
            raise <| new HttpResponseException(request.CreateErrorResponse(HttpStatusCode.NotFound, request.RequestUri.AbsoluteUri))

        controllerDescriptor

    interface IHttpControllerSelector with
        member x.GetControllerMapping() = controllerMapping
        member x.SelectController(request) = x.SelectController(request)

type FSharpControllerDescriptor(configuration, resource: Resource) =
    inherit HttpControllerDescriptor(configuration, resource.Name, resource.GetType())
    override x.CreateController(request) = resource :> IHttpController
    static member Create(configuration, resource) =
        let controllerDescriptor = new FSharpControllerDescriptor(configuration, resource) :> HttpControllerDescriptor
        controllerDescriptor.Properties.[Constants.actions] <- resource.Actions
        controllerDescriptor

type MappedResource = {
    RouteTemplate : string
    Resource : Resource
}

type FSharpControllerDispatcher(configuration: HttpConfiguration) =
    inherit HttpMessageHandler()
    do if configuration = null then raise <| new ArgumentNullException("configuration")

    let controllerSelector = configuration.Services.GetHttpControllerSelector()

    member x.Configuration = configuration

    override x.SendAsync(request, cancellationToken) =
        let task = async {
            try
                let! success = Async.AwaitTask <| x.InternalSendAsync(request, cancellationToken)
                return success
            with
            | ex ->
                let unwrappedException = ex.GetBaseException()
                let httpResponseException = unwrappedException :?> HttpResponseException
                if httpResponseException <> null then
                    return httpResponseException.Response
                else
                    return request.CreateErrorResponse(HttpStatusCode.InternalServerError, unwrappedException)
        }
        Async.StartAsTask(task, cancellationToken = cancellationToken)

    member private x.InternalSendAsync(request, cancellationToken) =
        if request = null then
            raise <| ArgumentNullException("request")
        
        let errorTask message =
            let tcs = new TaskCompletionSource<_>()
            tcs.SetResult(request.CreateErrorResponse(HttpStatusCode.NotFound, "Resource Not Found: " + request.RequestUri.AbsoluteUri, exn(message)))
            tcs.Task
            
        // TODO: Move text into resources.
        let routeData = request.GetRouteData()
        Contract.Assert(routeData <> null)
        let controllerDescriptor = controllerSelector.SelectController(request)
        if controllerDescriptor = null then errorTask "Resource not selected" else

        let controller = controllerDescriptor.CreateController(request)
        if controller = null then errorTask "No controller created" else
        // TODO: Appropriately handle other "error" scenarios such as 405 and 406.
        // TODO: Bake in an OPTIONS handler?

        let config = controllerDescriptor.Configuration
        let requestConfig = request.GetConfiguration()
        if requestConfig = null then
            request.Properties.Add(HttpPropertyKeys.HttpConfigurationKey, config)
        elif requestConfig <> config then
            request.Properties.[HttpPropertyKeys.HttpConfigurationKey] <- config

        // Create context
        let controllerContext = new HttpControllerContext(config, routeData, request)
        controllerContext.Controller <- controller
        controllerContext.ControllerDescriptor <- controllerDescriptor
        controller.ExecuteAsync(controllerContext, cancellationToken)

[<System.Runtime.CompilerServices.Extension>]
[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module Resource =

    // Merge the current template with the parent path.
    let private makeTemplate parentPath (resource: Resource) =
        if String.IsNullOrWhiteSpace parentPath then
            resource.RouteTemplate
        else parentPath + "/" + resource.RouteTemplate

    [<CompiledName("Flatten")>]
    let flatten (resource: Resource) =
        // This is likely horribly inefficient.
        let rec loop resource parentPath =
            [|
                let template = makeTemplate parentPath resource
                yield { RouteTemplate = template; Resource = resource }
                if resource.NestedResources |> Array.isEmpty then () else
                for nestedResource in resource.NestedResources do
                    yield! loop nestedResource template 
            |]

        if resource.NestedResources |> Array.isEmpty then
            [| { RouteTemplate = resource.RouteTemplate; Resource = resource } |]
        else loop resource ""

    /// Flattens the resource tree, merging route path segments into complete routes.
    [<CompiledName("MapResourceRoute")>]
    let route (configuration: HttpConfiguration) resource =
        // Would we be better off avoiding a lot of this and just mapping handlers to route paths? Will people use filters with this approach?
        let routes = configuration.Routes
        let resourceMappings = flatten resource 
        let controllerMapping =
            resourceMappings
            |> Array.map (fun x -> x.RouteTemplate, FSharpControllerDescriptor.Create(configuration, x.Resource))
            |> dict
        configuration.Services.Replace(typeof<IHttpControllerSelector>, new FSharpControllerSelector(configuration, controllerMapping))
        configuration.Services.Replace(typeof<IHttpControllerTypeResolver>, new FSharpControllerTypeResolver(resource))
        configuration.Services.Replace(typeof<IHttpActionSelector>, new FSharpControllerActionSelector())
        // TODO: Can we remove unused services objects? Does that have any benefit?
        // TODO: Investigate whether or not this is necessary anymore.
        let dispatcher = new FSharpControllerDispatcher(configuration)
        for mappedResource in resourceMappings do
            // TODO: probably want our own shortcut to allow embedding regex's in the route template.
            routes.MapHttpRoute(
                name = mappedResource.Resource.Name,
                routeTemplate = mappedResource.RouteTemplate,
                defaults = null,
                constraints = null,
                handler = dispatcher) |> ignore
