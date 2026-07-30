namespace Frank.Auth

open Frank.Builder

[<AutoOpen>]
module HandlerBuilderExtensions =
    type HandlerBuilder with
        [<CustomOperation("requireAuth")>]
        member RequireAuth: def: HandlerDefinition -> HandlerDefinition

        [<CustomOperation("requireClaim")>]
        member RequireClaim: def: HandlerDefinition * claimType: string * claimValue: string -> HandlerDefinition

        member RequireClaim: def: HandlerDefinition * claimType: string * claimValues: string list -> HandlerDefinition

        [<CustomOperation("requireRole")>]
        member RequireRole: def: HandlerDefinition * role: string -> HandlerDefinition

        [<CustomOperation("requirePolicy")>]
        member RequirePolicy: def: HandlerDefinition * policyName: string -> HandlerDefinition

        /// Bypasses all authorization for this one handler -- resource-level
        /// and handler-level requirements alike -- via stock ASP.NET Core
        /// IAllowAnonymous semantics. See CLAUDE.md and the design doc for
        /// why this is a full bypass rather than a policy downgrade.
        [<CustomOperation("allowAnonymous")>]
        member AllowAnonymous: def: HandlerDefinition -> HandlerDefinition
