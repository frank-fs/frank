namespace Frank.Provenance

open System
open Frank.Rdf

/// Named constructor functions for the closed PROV-O vocabulary this package uses. Callers never
/// write a raw PROV IRI string -- every function here wraps a `ProvClass`/`ProvRelation` case.
/// Builds directly on `Frank.Rdf.Description`; not a parallel triple model.
module Prov =
    /// A Description whose subject is typed prov:Activity.
    val activity: id: Node -> Description
    /// A Description whose subject is typed prov:Entity.
    val entity: id: Node -> Description
    /// A Description whose subject is typed prov:Agent.
    val agent: id: Node -> Description

    /// Adds prov:wasGeneratedBy, pointing at the given Activity node.
    val wasGeneratedBy: activity: Node -> Description -> Description
    /// Adds prov:wasAssociatedWith, pointing at the given Agent node.
    val wasAssociatedWith: agent: Node -> Description -> Description
    /// Adds prov:used, pointing at the given Entity node.
    val used: entity: Node -> Description -> Description
    /// Adds prov:startedAtTime as a DateTimeOffset-typed literal.
    val startedAtTime: t: DateTimeOffset -> Description -> Description
    /// Adds prov:endedAtTime as a DateTimeOffset-typed literal.
    val endedAtTime: t: DateTimeOffset -> Description -> Description
    /// Adds prov:wasDerivedFrom, pointing at the given Entity node.
    val wasDerivedFrom: entity: Node -> Description -> Description
    /// Adds prov:specializationOf, pointing at the given Entity node.
    val specializationOf: entity: Node -> Description -> Description
