namespace Frank.Cli.Core.Unified

// Types moved to Frank.Statecharts.Unified for runtime access.
// This file re-exports the namespace so existing CLI code compiles unchanged.
open Frank.Statecharts.Unified

/// Re-export pure functions from Frank.Statecharts.Unified.UnifiedModel
/// so existing CLI code using Frank.Cli.Core.Unified.UnifiedModel still compiles.
module UnifiedModel =

    /// Derive a filename-safe slug from a route template.
    let resourceSlug = UnifiedModel.resourceSlug

    /// Empty derived fields for resources without statecharts.
    let emptyDerivedFields = UnifiedModel.emptyDerivedFields
