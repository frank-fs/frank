module TestFixtures.DatastarConflict

open Frank.Builder
open Frank.Datastar

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let datastarHandler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - datastar defaults to GET, conflicts with explicit get
let datastarConflictResource =
    resource "/test" {
        datastar datastarHandler  // Registers GET by default
        get handler               // Duplicate GET - should warn
    }
