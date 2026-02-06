namespace Frank.Auth

[<RequireQualifiedAccess>]
type AuthRequirement =
    | Authenticated
    | Claim of claimType: string * claimValues: string list
    | Policy of name: string
    | Role of name: string
