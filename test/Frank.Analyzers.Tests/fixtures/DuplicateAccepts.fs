module TestFixtures.DuplicateAccepts

open Frank.Builder

let jsonHandler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let anotherJsonHandler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK002 -- "application/json" registered twice
let duplicateAcceptsResource =
    resource "/test" {
        get (negotiate {
            accepts "application/json" jsonHandler
            accepts "application/json" anotherJsonHandler // Duplicate media type -- should warn
        })
    }
