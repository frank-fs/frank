namespace Frank.Auth

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with
        [<CustomOperation("requireAuth")>]
        member RequireAuth : spec:ResourceSpec -> ResourceSpec

        [<CustomOperation("requireClaim")>]
        member RequireClaim : spec:ResourceSpec * claimType:string * claimValue:string -> ResourceSpec

        member RequireClaim : spec:ResourceSpec * claimType:string * claimValues:string list -> ResourceSpec

        [<CustomOperation("requireRole")>]
        member RequireRole : spec:ResourceSpec * role:string -> ResourceSpec

        [<CustomOperation("requirePolicy")>]
        member RequirePolicy : spec:ResourceSpec * policyName:string -> ResourceSpec
