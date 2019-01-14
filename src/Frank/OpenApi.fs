module Frank.OpenApi

open System
open System.Collections.Generic
open System.IO
open System.Text.RegularExpressions
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.OpenApi
open Microsoft.OpenApi.Extensions
open Microsoft.OpenApi.Models
open FSharp.Control.Tasks.V2.ContextInsensitive

module OperationId =
    let format (httpMethod:string) (name:string) =
        httpMethod.ToLower() + Regex.Replace(name, "[-\s()\[\]{}\$]", "")

module OperationType =

    let ofHttpMethod httpMethod =
        if HttpMethods.IsGet httpMethod then OperationType.Get
        elif HttpMethods.IsPut httpMethod then OperationType.Put
        elif HttpMethods.IsPost httpMethod then OperationType.Post
        elif HttpMethods.IsDelete httpMethod then OperationType.Delete
        elif HttpMethods.IsOptions httpMethod then OperationType.Options
        elif HttpMethods.IsHead httpMethod then OperationType.Head
        elif HttpMethods.IsPatch httpMethod then OperationType.Patch
        elif HttpMethods.IsTrace httpMethod then OperationType.Trace
        else failwithf "Unrecognized Open API HTTP method %s." httpMethod

module OpenApiParameter =

    let rec private applyParameterPolicies (name, policies:seq<IParameterPolicy>, schema:OpenApiSchema) =
        for policy in policies do
        match policy with
        | :? Constraints.BoolRouteConstraint ->
            schema.Type <- "boolean"
        | :? Constraints.CompositeRouteConstraint as comp ->
            let policies = comp.Constraints |> Seq.map (fun c -> c :> IParameterPolicy)
            applyParameterPolicies (name, policies, schema)
        | :? Constraints.DateTimeRouteConstraint ->
            schema.Type <- "string"
            schema.Format <- "date-time"
        | :? Constraints.DecimalRouteConstraint ->
            schema.Type <- "number"
        | :? Constraints.DoubleRouteConstraint ->
            schema.Type <- "number"
            schema.Format <- "double"
        | :? Constraints.FloatRouteConstraint ->
            schema.Type <- "number"
            schema.Format <- "float"
        | :? Constraints.GuidRouteConstraint ->
            schema.Type <- "string"
            schema.Format <- "uuid"
        | :? Constraints.IntRouteConstraint ->
            schema.Type <- "integer"
            schema.Format <- "int32"
        | :? Constraints.LengthRouteConstraint as c ->
            schema.MaxLength <- Nullable(c.MaxLength)
            schema.MinLength <- Nullable(c.MinLength)
        | :? Constraints.LongRouteConstraint ->
            schema.Type <- "integer"
            schema.Format <- "int64"
        | :? Constraints.MaxRouteConstraint as c ->
            schema.Maximum <- Nullable(decimal c.Max)
        | :? Constraints.MaxLengthRouteConstraint as c ->
            schema.MaxLength <- Nullable(c.MaxLength)
        | :? Constraints.MinRouteConstraint as c ->
            schema.Minimum <- Nullable(decimal c.Min)
        | :? Constraints.MinLengthRouteConstraint as c ->
            schema.MinLength <- Nullable(c.MinLength)
        | :? Constraints.RangeRouteConstraint as c ->
            schema.Maximum <- Nullable(decimal c.Max)
            schema.Minimum <- Nullable(decimal c.Min)
        | :? Constraints.RegexRouteConstraint as c ->
            schema.Type <- "string"
            schema.Pattern <- c.Constraint.ToString()
        | :? Constraints.RequiredRouteConstraint ->
            schema.Required.Add(name) |> ignore
        | :? Constraints.StringRouteConstraint ->
            schema.Type <- "string"
        | _ -> ()

    let ofRouteParameters (parameters:IReadOnlyList<Patterns.RoutePatternParameterPart>) =
        [|for part in parameters do
            if part.ParameterPolicies.Count > 0 then
                let schema = OpenApiSchema()
                let policies = part.ParameterPolicies |> Seq.map (fun p -> p.ParameterPolicy)
                applyParameterPolicies(part.Name, policies, schema)
                yield OpenApiParameter(Name=part.Name, In=Nullable(ParameterLocation.Path), Required=not part.IsOptional, Schema=schema)
            else
                yield OpenApiParameter(Name=part.Name, In=Nullable(ParameterLocation.Path), Required=not part.IsOptional)|]

let emptyDocument (title, version) =
    OpenApiDocument(
        Info = OpenApiInfo(Title=title, Version=version),
        Servers = ResizeArray<OpenApiServer>(),
        Paths = OpenApiPaths())

let defaultResponse = "200", OpenApiResponse(Description="OK")

let emptyOperation (httpMethod, name) =
    let op = OpenApiOperation(OperationId=OperationId.format httpMethod name)
    op.Responses.Add(defaultResponse)
    op

let handler (document:OpenApiDocument) =
    RequestDelegate(fun ctx ->
        task {
            // TODO: check Accept header and negotiate json or yaml.
            use buffer = new MemoryStream()
            document.SerializeAsJson(buffer, OpenApiSpecVersion.OpenApi3_0)
            buffer.Position <- 0L
            ctx.Response.ContentType <- "application/json; charset=utf-8"
            do! buffer.CopyToAsync(ctx.Response.Body)
        } :> _)

let endpoint document =
    RouteEndpoint(
        requestDelegate=handler document,
        routePattern=Patterns.RoutePatternFactory.Parse "openapi",
        order=0,
        metadata=
            EndpointMetadataCollection(
                HttpMethodMetadata [|HttpMethods.Get|],
                EndpointNameMetadata("openapi"),
                RouteNameMetadata("openapi")),
        displayName="OpenAPI") :> Endpoint
