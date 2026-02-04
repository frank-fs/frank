module TestFixtures.DuplicatePatch

open Frank.Builder

let handler1 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let handler2 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - duplicate PATCH handler
let duplicatePatchResource =
    resource "/test" {
        patch handler1
        patch handler2  // Duplicate PATCH - should warn
    }
