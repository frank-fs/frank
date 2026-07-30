namespace Frank.Auth

type AuthConfig = { Requirements: AuthRequirement list }

module AuthConfig =
    val empty : AuthConfig

    /// An AuthConfig carrying exactly one requirement. Shorthand for
    /// `empty |> addRequirement requirement`, used by every single-requirement
    /// ResourceBuilder/HandlerBuilder auth operation. Named to match the
    /// F# core library convention (List.singleton, Set.singleton, ...).
    val singleton : requirement:AuthRequirement -> AuthConfig

    val addRequirement : requirement:AuthRequirement -> config:AuthConfig -> AuthConfig

    val isEmpty : config:AuthConfig -> bool
