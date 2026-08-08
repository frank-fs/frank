module TestFixtures.HrefVarExtra

open Frank.Builder
open Frank.JsonHome

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// This should trigger FRANK003 - "prodId" matches no {..} in the template
let hrefVarExtraResource =
    resource "/products/{id}" {
        rel "tag:example.com,2026:product"
        hrefVar "prodId" "https://example.com/param/product-id" // Typo - should be "id"
        get handler
    }
