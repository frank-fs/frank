namespace Frank.Auth

open Microsoft.AspNetCore.Authentication
open Microsoft.AspNetCore.Authorization
open Frank.Builder

[<AutoOpen>]
module WebHostBuilderExtensions =
    type WebHostBuilder with
        [<CustomOperation("useAuthentication")>]
        member UseAuthentication : spec:WebHostSpec * configure:(AuthenticationBuilder -> AuthenticationBuilder) -> WebHostSpec

        [<CustomOperation("useAuthorization")>]
        member UseAuthorization : spec:WebHostSpec -> WebHostSpec

        [<CustomOperation("authorizationPolicy")>]
        member AuthorizationPolicy : spec:WebHostSpec * name:string * configure:(AuthorizationPolicyBuilder -> unit) -> WebHostSpec
