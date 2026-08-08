module Frank.Alps.Sample.Program

open Microsoft.AspNetCore.Authentication
open Microsoft.Extensions.DependencyInjection
open Frank.Builder
open Frank.Auth
open Frank.Alps
open Frank.Alps.Sample.Catalog
open Frank.Alps.Sample.PingPong
open Frank.Alps.Sample.TrafficLight

[<EntryPoint>]
let main args =
    webHost args {
        useDefaults

        useAuthentication (fun auth ->
            // Same DefaultScheme rationale as sample/Frank.JsonHome.Sample/Program.fs: lets
            // UseAuthentication populate ctx.User without every requireRole-guarded resource
            // having to name a scheme explicitly.
            auth.Services.Configure<AuthenticationOptions>(fun (o: AuthenticationOptions) ->
                o.DefaultScheme <- PingPongAuth.SchemeName
                o.DefaultAuthenticateScheme <- PingPongAuth.SchemeName)
            |> ignore

            auth.AddScheme<AuthenticationSchemeOptions, PingPongAuth.ApiKeyAuthHandler>(
                PingPongAuth.SchemeName,
                fun _ -> ()
            ))

        useAuthorization

        resource gameResource
        resource sessionsResource
        resource sessionResource
        resource pingResource
        resource pongResource
        resource intersectionsResource
        resource intersectionResource
        resource walkResource
        resource emergencyOverrideResource
        resource emergencyClearResource

        useAlps
            ([ openState
               closedState
               game
               viewGame
               makeMove
               participant
               awaitingPing
               awaitingPong
               session
               listSessions
               createSession
               viewSession
               ping
               pong ]
             @ profile)
    }

    0
