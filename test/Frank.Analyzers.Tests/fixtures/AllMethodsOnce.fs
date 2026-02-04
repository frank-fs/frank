module TestFixtures.AllMethodsOnce

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should NOT trigger any warnings - one of each method
let allMethodsResource =
    resource "/test" {
        get handler
        post handler
        put handler
        delete handler
        patch handler
        head handler
        options handler
        connect handler
        trace handler
    }
