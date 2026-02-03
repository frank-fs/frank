module TestFixtures.DuplicateTrace

open Frank.Builder

let handler1 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let handler2 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - duplicate TRACE handler
let duplicateTraceResource =
    resource "/test" {
        trace handler1
        trace handler2  // Duplicate TRACE - should warn
    }
