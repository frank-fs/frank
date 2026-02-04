module TestFixtures.DuplicateConnect

open Frank.Builder

let handler1 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

let handler2 (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK001 warning - duplicate CONNECT handler
let duplicateConnectResource =
    resource "/test" {
        connect handler1
        connect handler2  // Duplicate CONNECT - should warn
    }
