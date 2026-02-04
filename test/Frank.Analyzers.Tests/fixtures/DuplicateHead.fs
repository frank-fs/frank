module TestFixtures.DuplicateHead

open Frank.Builder

let handler1 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let handler2 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - duplicate HEAD handler
let duplicateHeadResource =
    resource "/test" {
        head handler1
        head handler2  // Duplicate HEAD - should warn
    }
