module TestFixtures.DuplicateGet

open Frank.Builder

let handler1 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let handler2 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - duplicate GET handler
let duplicateGetResource =
    resource "/test" {
        get handler1
        get handler2  // Duplicate GET - should warn
    }
