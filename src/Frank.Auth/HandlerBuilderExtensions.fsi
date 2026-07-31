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
        /// and handler-level requirements alike. ASP.NET Core's own
        /// authorization middleware treats IAllowAnonymous metadata as an
        /// unconditional short-circuit: if present anywhere on an endpoint,
        /// every IAuthorizeData/AuthorizationPolicy on that endpoint is
        /// skipped, regardless of how many are declared or where they came
        /// from. This is therefore a full bypass, not a downgrade to a
        /// laxer-but-still-restricted policy -- co-declaring, say,
        /// requireRole on the same handler has no effect once allowAnonymous
        /// is present.
        [<CustomOperation("allowAnonymous")>]
        member AllowAnonymous: def: HandlerDefinition -> HandlerDefinition
