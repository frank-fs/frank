namespace Frank.Auth

type AuthConfig = { Requirements: AuthRequirement list }

module AuthConfig =
    val empty : AuthConfig

    val addRequirement : requirement:AuthRequirement -> config:AuthConfig -> AuthConfig

    val isEmpty : config:AuthConfig -> bool
