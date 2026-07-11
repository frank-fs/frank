module TestFixtures.VocabCaseRoute

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// Route /Games with different case — used for AT8 matcher (case-insensitive match covers /games)
let gamesResource =
    resource "/Games" {
        get handler
    }
