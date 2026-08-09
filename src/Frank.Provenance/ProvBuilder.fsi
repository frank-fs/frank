namespace Frank.Provenance

open System
open Frank.Rdf

[<AutoOpen>]
module ProvBuilderModule =
    /// Builds a `Description` via computation expression, as an alternative to plain `|>` combinators
    /// over `Prov`'s functions -- both produce identical `Description` values. Mirrors `Frank.Rdf`'s
    /// `DescribeBuilder`/`describe` and `Frank.Alps`'s `DescriptorBuilder`/`descriptor`: one accumulator,
    /// no `Combine`/`Delay`, `Run` returns a plain value.
    [<Sealed>]
    type ProvBuilder =
        new: initial: Description -> ProvBuilder
        member Yield: 'a -> Description
        member Zero: unit -> Description
        member inline Run: d: Description -> Description

        [<CustomOperation("wasGeneratedBy")>]
        member inline WasGeneratedBy: d: Description * activity: Node -> Description

        [<CustomOperation("wasAssociatedWith")>]
        member inline WasAssociatedWith: d: Description * agent: Node -> Description

        [<CustomOperation("used")>]
        member inline Used: d: Description * entity: Node -> Description

        [<CustomOperation("startedAtTime")>]
        member inline StartedAtTime: d: Description * t: DateTimeOffset -> Description

        [<CustomOperation("endedAtTime")>]
        member inline EndedAtTime: d: Description * t: DateTimeOffset -> Description

        [<CustomOperation("wasDerivedFrom")>]
        member inline WasDerivedFrom: d: Description * entity: Node -> Description

        [<CustomOperation("specializationOf")>]
        member inline SpecializationOf: d: Description * entity: Node -> Description

    /// Enters an `activity id { }` block: `activity a { wasAssociatedWith ag; startedAtTime t0; endedAtTime t1 }`.
    val activity: id: Node -> ProvBuilder
    /// Enters an `entity id { }` block: `entity e { wasGeneratedBy a }`.
    val entity: id: Node -> ProvBuilder
    /// Enters an `agent id { }` block: `agent ag { }`.
    val agent: id: Node -> ProvBuilder
