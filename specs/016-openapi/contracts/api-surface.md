# API Surface: Frank.OpenApi

**Feature Branch**: `016-openapi`
**Date**: 2026-02-09

## Namespace: Frank.OpenApi

### Types

#### HandlerDefinition (record)

```fsharp
type HandlerDefinition =
    { Handler: RequestDelegate
      Name: string option
      Summary: string option
      Description: string option
      Tags: string list
      Produces: ProducesInfo list
      Accepts: AcceptsInfo list }
```

#### ProducesInfo (record)

```fsharp
type ProducesInfo =
    { StatusCode: int
      ResponseType: Type option
      ContentTypes: string list
      Description: string option }
```

#### AcceptsInfo (record)

```fsharp
type AcceptsInfo =
    { RequestType: Type
      ContentTypes: string list
      IsOptional: bool }
```

#### HandlerBuilder (sealed class)

```fsharp
[<Sealed>]
type HandlerBuilder() =
    member _.Yield(_) : HandlerDefinition
    member _.Run(def: HandlerDefinition) : HandlerDefinition

    [<CustomOperation("handle")>]
    member _.Handle(def, handler: HttpContext -> Task) : HandlerDefinition

    // Overloads for common handler signatures
    member _.Handle(def, handler: HttpContext -> Task<'a>) : HandlerDefinition
    member _.Handle(def, handler: HttpContext -> Async<'a>) : HandlerDefinition

    [<CustomOperation("name")>]
    member _.Name(def, operationName: string) : HandlerDefinition

    [<CustomOperation("summary")>]
    member _.Summary(def, summary: string) : HandlerDefinition

    [<CustomOperation("description")>]
    member _.Description(def, desc: string) : HandlerDefinition

    [<CustomOperation("tags")>]
    member _.Tags(def, tags: string list) : HandlerDefinition

    [<CustomOperation("produces")>]
    member _.Produces<'T>(def, statusCode: int) : HandlerDefinition

    [<CustomOperation("produces")>]
    member _.Produces<'T>(def, statusCode: int, description: string) : HandlerDefinition

    [<CustomOperation("producesEmpty")>]
    member _.ProducesEmpty(def, statusCode: int) : HandlerDefinition

    [<CustomOperation("accepts")>]
    member _.Accepts<'T>(def) : HandlerDefinition

    [<CustomOperation("accepts")>]
    member _.Accepts<'T>(def, contentTypes: string list) : HandlerDefinition
```

### Module: Frank.OpenApi

```fsharp
/// HandlerBuilder instance for use in computation expressions
val handler : HandlerBuilder
```

### ResourceBuilder Extensions

```fsharp
[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        // For each HTTP method: overload accepting HandlerDefinition
        [<CustomOperation("get")>]
        member _.Get(spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec

        [<CustomOperation("post")>]
        member _.Post(spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec

        [<CustomOperation("put")>]
        member _.Put(spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec

        [<CustomOperation("delete")>]
        member _.Delete(spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec

        [<CustomOperation("patch")>]
        member _.Patch(spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec

        [<CustomOperation("head")>]
        member _.Head(spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec

        [<CustomOperation("options")>]
        member _.Options(spec: ResourceSpec, def: HandlerDefinition) : ResourceSpec
```

### WebHostBuilder Extensions

```fsharp
[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with
        /// Enable OpenAPI with FSharpSchemaTransformer using default config
        [<CustomOperation("useOpenApi")>]
        member _.UseOpenApi(spec: WebHostSpec) : WebHostSpec

        /// Enable OpenAPI with custom configuration callback
        [<CustomOperation("useOpenApi")>]
        member _.UseOpenApi(spec: WebHostSpec, configure: OpenApiOptions -> unit) : WebHostSpec
```

## Internal Implementation Notes

### HandlerDefinition → ResourceSpec Conversion

When a `HandlerDefinition` is passed to a ResourceBuilder HTTP method operation:

```fsharp
// Pseudocode for the conversion
static member AddHandlerDefinition(httpMethod: string, spec: ResourceSpec, def: HandlerDefinition) =
    // 1. Add the handler
    let spec = { spec with Handlers = (httpMethod, def.Handler) :: spec.Handlers }

    // 2. Build metadata conventions from the definition
    let conventions = [
        // Name
        match def.Name with
        | Some name -> yield fun (b: EndpointBuilder) -> b.Metadata.Add(EndpointNameMetadata(name))
        | None -> ()

        // Summary
        match def.Summary with
        | Some s -> yield fun (b: EndpointBuilder) -> b.Metadata.Add(EndpointSummaryMetadata(s))
        | None -> ()

        // Description
        match def.Description with
        | Some d -> yield fun (b: EndpointBuilder) -> b.Metadata.Add(EndpointDescriptionMetadata(d))
        | None -> ()

        // Tags
        if not (List.isEmpty def.Tags) then
            yield fun (b: EndpointBuilder) -> b.Metadata.Add(TagsMetadata(def.Tags |> List.toArray))

        // Produces
        for p in def.Produces do
            yield fun (b: EndpointBuilder) ->
                match p.ResponseType with
                | Some t -> b.Metadata.Add(ProducesResponseTypeMetadata(t, p.StatusCode, p.ContentTypes |> Array.ofList))
                | None -> b.Metadata.Add(ProducesResponseTypeMetadata(p.StatusCode))

        // Accepts
        for a in def.Accepts do
            yield fun (b: EndpointBuilder) ->
                b.Metadata.Add(AcceptsMetadata(a.RequestType, a.IsOptional, a.ContentTypes |> Array.ofList))
    ]

    // 3. Append conventions to metadata
    { spec with Metadata = spec.Metadata @ conventions }
```

### useOpenApi Implementation Pattern

```fsharp
// Default: registers FSharpSchemaTransformer with default config
member _.UseOpenApi(spec: WebHostSpec) : WebHostSpec =
    { spec with
        Services = spec.Services >> fun services ->
            services.AddOpenApi(fun options ->
                options.AddSchemaTransformer<FSharpSchemaTransformer>()
            ) |> ignore
            services
        Middleware = spec.Middleware >> fun app ->
            app.MapOpenApi() |> ignore
            app }

// With configuration: user controls OpenApiOptions
member _.UseOpenApi(spec: WebHostSpec, configure: OpenApiOptions -> unit) : WebHostSpec =
    { spec with
        Services = spec.Services >> fun services ->
            services.AddOpenApi(fun options -> configure options) |> ignore
            services
        Middleware = spec.Middleware >> fun app ->
            app.MapOpenApi() |> ignore
            app }
```
