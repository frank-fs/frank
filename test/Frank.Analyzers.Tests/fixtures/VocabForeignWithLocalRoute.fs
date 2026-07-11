module TestFixtures.VocabForeignWithLocalRoute

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// Route /vocab is present locally, but vocab IRI has a foreign authority.
// Used for AT6 (foreign authority still warns) and AT-route-hint (route is hint only).
let vocabResource =
    resource "/vocab" {
        get handler
    }
