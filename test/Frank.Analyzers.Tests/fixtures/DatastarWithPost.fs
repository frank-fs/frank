module TestFixtures.DatastarWithPost

open Frank.Builder
open Frank.Datastar
open Microsoft.AspNetCore.Http

let handler (ctx: HttpContext) =
    task { return () }

let datastarHandler (ctx: HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - datastar with explicit POST conflicts with post
let datastarWithPostResource =
    resource "/test" {
        datastar HttpMethods.Post datastarHandler  // Registers POST
        post handler                               // Duplicate POST - should warn
    }
