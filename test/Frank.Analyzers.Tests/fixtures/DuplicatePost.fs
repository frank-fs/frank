module TestFixtures.DuplicatePost

open Frank.Builder

let handler1 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let handler2 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - duplicate POST handler
let duplicatePostResource =
    resource "/test" {
        post handler1
        post handler2  // Duplicate POST - should warn
    }
