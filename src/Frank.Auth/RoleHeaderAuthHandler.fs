namespace Frank.Auth

open System.Security.Claims
open System.Text.Encodings.Web
open System.Threading.Tasks
open Microsoft.AspNetCore.Authentication
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Options
open Microsoft.Extensions.Primitives

/// Authentication handler that resolves roles from the X-Role HTTP header.
/// For development/testing — not production auth. Creates a ClaimsIdentity
/// with ClaimTypes.Role claims from the header value.
type RoleHeaderAuthHandler(options: IOptionsMonitor<AuthenticationSchemeOptions>, logger: ILoggerFactory, encoder: UrlEncoder) =
    inherit AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)

    static member val SchemeName = "RoleHeader" with get

    override this.HandleAuthenticateAsync() =
        let mutable values = StringValues.Empty
        let found = this.Request.Headers.TryGetValue("X-Role", &values)
        if found && values.Count > 0 then
            let roleValue = values.[0]
            if System.String.IsNullOrWhiteSpace(roleValue) then
                Task.FromResult(AuthenticateResult.NoResult())
            else
                let claims = [ Claim(ClaimTypes.Role, roleValue) ]
                let identity = ClaimsIdentity(claims, this.Scheme.Name)
                let principal = ClaimsPrincipal(identity)
                let ticket = AuthenticationTicket(principal, this.Scheme.Name)
                Task.FromResult(AuthenticateResult.Success(ticket))
        else
            Task.FromResult(AuthenticateResult.NoResult())
