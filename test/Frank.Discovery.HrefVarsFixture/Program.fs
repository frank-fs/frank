module Frank.Discovery.HrefVarsFixture.Program

open Microsoft.AspNetCore.Http
open Frank.Builder
open Frank.Discovery

/// Config where the Game relation has NO mapping for the "gameId" template variable.
/// JsonHomeSerializer.serialize will throw invalidOp naming "gameId".
let private config =
    { DiscoveryConfig.Empty with
        ResourceHrefVars = Map.ofList [ "https://schema.org/Game", Map.empty ] }

let private gameResource =
    resource "/games/{gameId}" {
        relation "https://schema.org/Game"
        get (fun (ctx: HttpContext) -> ctx.Response.StatusCode <- 200)
    }

[<EntryPoint>]
let main args =
    webHost args {
        useDiscoveryWith config
        resource gameResource
    }

    0
