module TestFixtures.VocabForeignWithLocalRouteAndExtRef

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// A /vocab route (local) AND an explicit ext: CURIE reference (foreign authority).
// Used for AT6 authority test (GAP 3 scope pinning).
let extThing = "ext:Thing"

let vocabResource =
    resource "/vocab" {
        get handler
    }
