module TestFixtures.DatastarNoConflict

open Frank.Builder
open Frank.Datastar

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let datastarHandler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should NOT trigger any warnings - datastar defaults to GET, post is different
let datastarNoConflictResource =
    resource "/test" {
        datastar datastarHandler  // Registers GET by default
        post handler              // POST is a different method - no conflict
    }
