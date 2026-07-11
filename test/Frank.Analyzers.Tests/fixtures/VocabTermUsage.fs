module TestFixtures.VocabTermUsage

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// References vocabulary terms as CURIE strings — used for AT-term tests
let gameTerm = "schema:Game"
let personTerm = "schema:Person"
