namespace Frank.Auth

open Frank.Builder

module EndpointAuth =
    val applyAuth: config: AuthConfig -> spec: ResourceSpec -> ResourceSpec

    /// Handler-level counterpart to applyAuth: appends each requirement's
    /// metadata objects directly onto the HandlerDefinition. ResourceBuilder
    /// .AddHandlerDefinition later scopes them to just that handler's HTTP
    /// method -- this function does not need to know about scoping at all.
    val applyAuthToHandler: config: AuthConfig -> def: HandlerDefinition -> HandlerDefinition
