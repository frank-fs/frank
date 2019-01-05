namespace Test

/// Lightweight content negotiation from AspNetCore.Mvc.Core.
/// Based on https://www.strathweb.com/2018/09/running-asp-net-core-content-negotiation-by-hand/
module ContentNegotiation =

    open System.Threading.Tasks
    open Microsoft.AspNetCore.Http
    open Microsoft.AspNetCore.Mvc.Formatters
    open Microsoft.AspNetCore.Mvc.Infrastructure
    open Microsoft.Extensions.DependencyInjection

    let notAcceptable (ctx:HttpContext) : Task =
        ctx.Response.StatusCode <- 406
        upcast Task.FromResult()

    let negotiate statusCode (body:'a) (ctx:HttpContext) =
        let selector = ctx.RequestServices.GetRequiredService<OutputFormatterSelector>()
        let writerFactory = ctx.RequestServices.GetRequiredService<IHttpResponseStreamWriterFactory>()
        let formatterContext = OutputFormatterWriteContext(ctx, (fun stream encoding -> writerFactory.CreateWriter(stream, encoding)), typeof<'a>, body)
        let formatter = selector.SelectFormatter(formatterContext, [||], MediaTypeCollection())
        if isNull formatter then
            notAcceptable ctx
        else
            ctx.Response.StatusCode <- statusCode
            formatter.WriteAsync(formatterContext)
    
    type HttpContext with
        member ctx.Negotiate(statusCode, body) =
            negotiate statusCode body ctx
