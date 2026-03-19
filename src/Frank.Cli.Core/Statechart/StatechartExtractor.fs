namespace Frank.Cli.Core.Statechart

open Frank.Statecharts
open Frank.Statecharts.Unified

/// Helpers for constructing ExtractedStatechart values.
module StatechartExtractor =

    /// Build an ExtractedStatechart from extracted data.
    let toExtractedStatechart
        (routeTemplate: string)
        (stateNames: string list)
        (initialStateKey: string)
        (guardNames: string list)
        (stateMetadata: Map<string, StateInfo>)
        =
        { RouteTemplate = routeTemplate
          StateNames = stateNames
          InitialStateKey = initialStateKey
          GuardNames = guardNames
          StateMetadata = stateMetadata }
