module TestFixtures.HrefVarMissing

open Frank.Builder
open Frank.JsonHome

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK003 - "id" has no hrefVar declaration
let hrefVarMissingResource =
    resource "/products/{id}" {
        rel "tag:example.com,2026:product"
        get handler
    }
