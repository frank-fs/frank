module TestFixtures.MultipleResources

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should NOT trigger any warnings - different resources can have same method
let resource1 =
    resource "/resource1" {
        get handler
    }

let resource2 =
    resource "/resource2" {
        get handler
    }
