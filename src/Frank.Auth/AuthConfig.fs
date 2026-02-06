namespace Frank.Auth

type AuthConfig = { Requirements: AuthRequirement list }

module AuthConfig =
    let empty : AuthConfig = { Requirements = [] }

    let addRequirement (requirement: AuthRequirement) (config: AuthConfig) : AuthConfig =
        { config with Requirements = config.Requirements @ [ requirement ] }

    let isEmpty (config: AuthConfig) : bool =
        config.Requirements |> List.isEmpty
