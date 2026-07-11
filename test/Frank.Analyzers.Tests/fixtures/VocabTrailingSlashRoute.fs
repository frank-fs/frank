module TestFixtures.VocabTrailingSlashRoute

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// Route /games/ with trailing slash — used for AT8 matcher (trailing slash covers /games path)
let gamesResource =
    resource "/games/" {
        get handler
    }
