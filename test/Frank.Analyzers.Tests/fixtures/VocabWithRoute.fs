module TestFixtures.VocabWithRoute

open Frank.Builder

let handler (ctx: Microsoft.AspNetCore.Http.HttpContext) =
    task { return () }

// Route /tictactoe declared - used to test AT2 scenario
// (extractRoutes should return ["/tictactoe"])
let tictactoeResource =
    resource "/tictactoe" {
        get handler
    }
