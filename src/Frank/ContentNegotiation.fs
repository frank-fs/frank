namespace Frank

/// Lightweight content negotiation from AspNetCore.Mvc.Core.
/// Based on https://www.strathweb.com/2018/09/running-asp-net-core-content-negotiation-by-hand/
module ContentNegotiation =

    open System.Threading.Tasks
    open Microsoft.AspNetCore.Http
    open Microsoft.AspNetCore.Mvc.Formatters
    open Microsoft.AspNetCore.Mvc.Infrastructure
    open Microsoft.Extensions.DependencyInjection

    let notAcceptable (ctx: HttpContext) : Task =
        ctx.Response.StatusCode <- 406
        upcast Task.FromResult()

    /// Builds the OutputFormatterWriteContext shared by `negotiate` and
    /// `viaOutputFormatter` -- both need one, differing only in how they
    /// select a formatter from it.
    let private buildFormatterContext (ctx: HttpContext) (body: 'a) : OutputFormatterWriteContext =
        let writerFactory =
            ctx.RequestServices.GetRequiredService<IHttpResponseStreamWriterFactory>()

        OutputFormatterWriteContext(
            ctx,
            (fun stream encoding -> writerFactory.CreateWriter(stream, encoding)),
            typeof<'a>,
            body
        )

    let negotiate statusCode (body: 'a) (ctx: HttpContext) =
        let selector = ctx.RequestServices.GetRequiredService<OutputFormatterSelector>()
        let formatterContext = buildFormatterContext ctx body

        let formatter =
            selector.SelectFormatter(formatterContext, [||], MediaTypeCollection())

        if isNull formatter then
            notAcceptable ctx
        else
            ctx.Response.StatusCode <- statusCode
            formatter.WriteAsync(formatterContext)

    let viaOutputFormatter (mediaType: string) (body: 'a) (ctx: HttpContext) : Task =
        let selector = ctx.RequestServices.GetRequiredService<OutputFormatterSelector>()
        let formatterContext = buildFormatterContext ctx body

        let requestedTypes = MediaTypeCollection()
        requestedTypes.Add(mediaType)

        let formatter =
            selector.SelectFormatter(formatterContext, [||], requestedTypes)

        if isNull formatter then
            failwithf
                "No IOutputFormatter is registered for media type '%s'. Ensure AddMvcCore() (and any extra formatter package, e.g. AddXmlSerializerFormatters()) is registered for this media type."
                mediaType
        else
            ctx.Response.ContentType <- mediaType
            formatter.WriteAsync(formatterContext)

    type HttpContext with
        member ctx.Negotiate(statusCode, body) = negotiate statusCode body ctx
