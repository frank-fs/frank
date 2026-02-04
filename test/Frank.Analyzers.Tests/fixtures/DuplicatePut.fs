module TestFixtures.DuplicatePut

open Frank.Builder

let handler1 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let handler2 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - duplicate PUT handler
let duplicatePutResource =
    resource "/test" {
        put handler1
        put handler2  // Duplicate PUT - should warn
    }
