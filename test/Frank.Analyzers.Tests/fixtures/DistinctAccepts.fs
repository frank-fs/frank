module TestFixtures.DistinctAccepts

open Frank.Builder

let jsonHandler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let htmlHandler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should NOT trigger any warnings -- different media types
let distinctAcceptsResource =
    resource "/test" {
        get (negotiate {
            accepts "application/json" jsonHandler
            accepts "text/html" htmlHandler
        })
    }
