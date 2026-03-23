namespace Frank.Cli.Core.Statechart

open Frank.Statecharts
open Frank.Resources.Model

/// Helpers for constructing ExtractedStatechart values.
module StatechartExtractor =

    /// Build an ExtractedStatechart from extracted data.
    let toExtractedStatechart
        (routeTemplate: string)
        (stateNames: string list)
        (initialStateKey: string)
        (guardNames: string list)
        (stateMetadata: Map<string, StateInfo>)
        (roles: RoleInfo list)
        =
        { RouteTemplate = routeTemplate
          StateNames = stateNames
          InitialStateKey = initialStateKey
          GuardNames = guardNames
          StateMetadata = stateMetadata
          Roles = roles
          Transitions = [] }
