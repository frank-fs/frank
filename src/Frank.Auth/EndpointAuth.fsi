namespace Frank.Auth

open Frank.Builder

module EndpointAuth =
    val applyAuth : config:AuthConfig -> spec:ResourceSpec -> ResourceSpec
