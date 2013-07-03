(* # F# Extensions to System.Web.Http

## License

Author: Ryan Riley <ryan.riley@panesofglass.org>
Copyright (c) 2011-2012, Ryan Riley.

Licensed under the Apache License, Version 2.0.
See LICENSE.txt for details.
*)
namespace System.Web.Http

open System.Net
open System.Net.Http
open System.Web.Http

// ## HTTP Resources
module internal Helper =
    let internal resourceHandlerOrDefault methods handler (request: HttpRequestMessage) =
        match handler request with
        | Some response -> response
        | _ ->
            async {
                let response = request.CreateResponse(HttpStatusCode.MethodNotAllowed, Content = new StringContent("405 Method Not Allowed"))
                methods
                |> Seq.map (fun (m: HttpMethod) -> m.Method)
                |> Seq.iter response.Content.Headers.Allow.Add
                return response
            }

// HTTP resources expose an resource handler function at a given uri.
// In the common MVC-style frameworks, this would roughly correspond
// to a `Controller`. Resources should represent a single entity type,
// and it is important to note that a `Foo` is not the same entity
// type as a `Foo list`, which is where most MVC approaches go wrong. 
// The optional `uriMatcher` parameter allows the consumer to provide
// a more advanced uri matching algorithm, such as one using regular
// expressions.
// 
// Additional notes:
//   Should this type subclass HttpServer? If it did it could get
//   it's own configration and have its own route table. I'm not
//   convinced System.Web.Routing is worth it, but it's an option.
type HttpResource(template, methods, handler) =
    inherit System.Web.Http.Routing.HttpRoute(routeTemplate = template,
                                              defaults = null,
                                              constraints = null,
                                              dataTokens = null,
                                              handler = new AsyncHandler(Helper.resourceHandlerOrDefault methods handler))
    with
    member x.Name = template

[<CompilationRepresentation(CompilationRepresentationFlags.ModuleSuffix)>]
module HttpResource =
    let private makeHandler(httpMethod, handler) = function
        | (request: HttpRequestMessage) when request.Method = httpMethod -> Some(handler request)
        | _ -> None

    // Helpers to more easily map `HttpApplication` functions to methods to be composed into `HttpResource`s.
    let mapResourceHandler(httpMethod, handler) = [httpMethod], makeHandler(httpMethod, handler)
    let get handler = mapResourceHandler(HttpMethod.Get, handler)
    let post handler = mapResourceHandler(HttpMethod.Post, handler)
    let put handler = mapResourceHandler(HttpMethod.Put, handler)
    let delete handler = mapResourceHandler(HttpMethod.Delete, handler)
    let options handler = mapResourceHandler(HttpMethod.Options, handler)
    let trace handler = mapResourceHandler(HttpMethod.Trace, handler)

    // Helper to more easily access URL params
    let getParam<'T> (request:HttpRequestMessage) key =
        let values = request.GetRouteData().Values
        if values.ContainsKey(key) then
            Some(values.[key] :?> 'T)
        else None

    // We can use several methods to merge multiple handlers together into a single resource.
    // Our chosen mechanism here is merging functions into a larger function of the same signature.
    // This allows us to create resources as follows:
    // 
    //     let resource = get app1 <|> post app2 <|> put app3 <|> delete app4
    //
    // The intent here is to build a resource, with at most one handler per HTTP method. This goes
    // against a lot of the "RESTful" approaches that just merge a bunch of method handlers at
    // different URI addresses.
    let orElse left right =
        fst left @ fst right,
        fun request ->
            match snd left request with
            | None -> snd right request
            | result -> result

    let inline (<|>) left right = orElse left right

    let route uri handler = HttpResource(uri, fst handler, snd handler)

    let routeResource uri handlers = route uri <| Seq.reduce orElse handlers

    let ``404 Not Found`` (request: HttpRequestMessage) = async {
        return request.CreateResponse(HttpStatusCode.NotFound)
    }

    let register<'a when 'a :> System.Web.Http.HttpConfiguration> (resources: seq<HttpResource>) (config: 'a) =
        for resource in resources do
            config.Routes.Add(resource.Name, resource)
        config


namespace FSharp.Web.Http

open System
open System.Collections.Generic
open System.Collections.ObjectModel
open System.Diagnostics.Contracts
open System.Linq
open System.Net
open System.Net.Http
open System.Reflection
open System.Threading.Tasks
open System.Web.Http
open System.Web.Http.Controllers
open System.Web.Http.Dispatcher
open System.Web.Http.Filters
open System.Web.Http.Hosting
open System.Web.Http.ModelBinding
open System.Web.Http.Properties
open System.Web.Http.Routing

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
type FSharpHttpActionDescriptor(controllerDescriptor, httpMethod: HttpMethod, app: HttpApplication) as x =
    inherit HttpActionDescriptor(controllerDescriptor)
    let parameters = new Collection<HttpParameterDescriptor>([| new FSharpHttpParameterDescriptor(x) |])
    override x.ActionName = httpMethod.Method
    override x.ResultConverter = Unchecked.defaultof<_>
    override x.ReturnType = typeof<HttpResponseMessage>
    override x.GetParameters() = parameters
    override x.ExecuteAsync(controllerContext, arguments, cancellationToken) =
        // TODO: use route data as the arguments?
        // TODO: insert the controller context into the Request.Properties?
        // TODO: just use the controller context?
        let runner = async {
            let! value = app controllerContext.Request
            return value :> obj }
        Async.StartAsTask(runner, cancellationToken = cancellationToken)
    member x.AsyncExecute(controllerContext: HttpControllerContext) = app controllerContext.Request

/// Type alias for a tuple containing an `HttpMethod` and an `HttpApplication`.
type HttpAction = HttpMethod * HttpApplication

/// The FSharpControllerActionSelector pattern matches the HttpMethod and matches to the appropriate handler.
type FSharpControllerActionSelector() =
    member x.GetActionMapping (controllerDescriptor: HttpControllerDescriptor) =
        if controllerDescriptor = null then raise <| ArgumentNullException("controllerDescriptor")
        // TODO: Cache the results
        let actions = controllerDescriptor.Properties.[Constants.actions] :?> HttpAction[]
        actions
        |> Array.map (fun (httpMethod, app) ->
            new FSharpHttpActionDescriptor(controllerDescriptor, httpMethod, app))

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
    [<CompiledName("Regiser")>]
    let register resource (configuration: HttpConfiguration) =
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
