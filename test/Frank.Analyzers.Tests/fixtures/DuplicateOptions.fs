module TestFixtures.DuplicateOptions

open Frank.Builder

let handler1 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let handler2 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - duplicate OPTIONS handler
let duplicateOptionsResource =
    resource "/test" {
        options handler1
        options handler2  // Duplicate OPTIONS - should warn
    }
