module Sample.JsonHome.ApiKeyAuth

open System.Collections.Generic
open System.Security.Claims
open System.Text.Encodings.Web
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication

/// Demo-only authentication: reads an "X-Api-Key" header and maps it to a
/// user and roles from a hardcoded table. This exists purely to make the
/// authorization-filtering behavior curl-able without standing up a real
/// identity provider -- a real app would use JWT Bearer, cookies, OAuth, etc.
[<Literal>]
let SchemeName = "ApiKey"

let private users: IDictionary<string, string * string list> =
    dict [ "admin-key", ("alice", [ "admin" ]); "user-key", ("bob", ([]: string list)) ]

type ApiKeyAuthHandler(options, logger, encoder: UrlEncoder) =
    inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

    override this.HandleAuthenticateAsync() =
        let key = this.Request.Headers["X-Api-Key"].ToString()

        match users.TryGetValue key with
        | true, (name, roles) ->
            let claims = Claim(ClaimTypes.Name, name) :: (roles |> List.map (fun r -> Claim(ClaimTypes.Role, r)))
            let identity = ClaimsIdentity(claims, SchemeName)
            let ticket = AuthenticationTicket(ClaimsPrincipal identity, SchemeName)
            Task.FromResult(AuthenticateResult.Success ticket)
        | false, _ -> Task.FromResult(AuthenticateResult.NoResult())
