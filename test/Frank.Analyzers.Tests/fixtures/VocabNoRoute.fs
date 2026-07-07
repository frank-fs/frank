module TestFixtures.VocabNoRoute

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// No resource declarations - used to test AT1/AT3/AT5 scenarios
// (route list will be empty when analyzing this file)
let doNothing = ()
