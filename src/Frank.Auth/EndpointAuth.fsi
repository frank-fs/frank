namespace Frank.Auth

open Frank.Builder

module EndpointAuth =
    /// The stock ASP.NET Core metadata objects a single requirement
    /// contributes: a bare AuthorizeAttribute for Authenticated/Policy, or an
    /// AuthorizeAttribute plus an explicit built AuthorizationPolicy for
    /// Claim/Role. Shared by the resource-level and handler-level paths so
    /// both produce identical metadata shapes.
    val toMetadataObjects: requirement: AuthRequirement -> obj list

    val applyAuth: config: AuthConfig -> spec: ResourceSpec -> ResourceSpec

    /// Handler-level counterpart to applyAuth: appends each requirement's
    /// metadata objects directly onto the HandlerDefinition. ResourceBuilder
    /// .AddHandlerDefinition later scopes them to just that handler's HTTP
    /// method -- this function does not need to know about scoping at all.
    val applyAuthToHandler: config: AuthConfig -> def: HandlerDefinition -> HandlerDefinition
