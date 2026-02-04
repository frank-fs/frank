module TestFixtures.DuplicateDelete

open Frank.Builder

let handler1 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let handler2 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - duplicate DELETE handler
let duplicateDeleteResource =
    resource "/test" {
        delete handler1
        delete handler2  // Duplicate DELETE - should warn
    }
