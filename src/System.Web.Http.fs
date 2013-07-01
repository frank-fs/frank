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
                methods |> Seq.iter response.Content.Headers.Allow.Add
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
        | (request: HttpRequestMessage) when request.Method.Method = httpMethod -> Some(handler request)
        | _ -> None

    // Helpers to more easily map `HttpApplication` functions to methods to be composed into `HttpResource`s.
    let mapResourceHandler(httpMethod, handler) = [httpMethod], makeHandler(httpMethod, handler)
    let get handler = mapResourceHandler(HttpMethod.Get.Method, handler)
    let post handler = mapResourceHandler(HttpMethod.Post.Method, handler)
    let put handler = mapResourceHandler(HttpMethod.Put.Method, handler)
    let delete handler = mapResourceHandler(HttpMethod.Delete.Method, handler)

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
