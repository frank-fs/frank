module TestFixtures.HrefVarValid

open Frank.Builder
open Frank.JsonHome

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should NOT trigger FRANK003 - "id" matches the template exactly
let hrefVarValidResource =
    resource "/products/{id}" {
        rel "tag:example.com,2026:product"
        hrefVar "id" "https://example.com/param/product-id"
        get handler
    }
