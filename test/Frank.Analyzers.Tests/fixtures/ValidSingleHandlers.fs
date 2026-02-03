module TestFixtures.ValidSingleHandlers

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should NOT trigger any warnings - different HTTP methods
let validResource =
    resource "/test" {
        get handler
        post handler
    }
