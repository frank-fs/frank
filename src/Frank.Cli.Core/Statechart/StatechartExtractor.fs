namespace Frank.Cli.Core.Statechart

open Frank.Statecharts

/// Structured representation of a single stateful resource extracted from source.
type ExtractedStatechart =
    { RouteTemplate: string
      StateNames: string list
      InitialStateKey: string
      GuardNames: string list
      StateMetadata: Map<string, StateInfo> }

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
