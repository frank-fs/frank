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
        member Run: d: Description -> Description

        [<CustomOperation("wasGeneratedBy")>]
        member WasGeneratedBy: d: Description * activity: Node -> Description

        [<CustomOperation("wasAssociatedWith")>]
        member WasAssociatedWith: d: Description * agent: Node -> Description

        [<CustomOperation("used")>]
        member Used: d: Description * entity: Node -> Description

        [<CustomOperation("startedAtTime")>]
        member StartedAtTime: d: Description * t: DateTimeOffset -> Description

        [<CustomOperation("endedAtTime")>]
        member EndedAtTime: d: Description * t: DateTimeOffset -> Description

        [<CustomOperation("wasDerivedFrom")>]
        member WasDerivedFrom: d: Description * entity: Node -> Description

        [<CustomOperation("specializationOf")>]
        member SpecializationOf: d: Description * entity: Node -> Description

    /// Enters an `activity id { }` block: `activity a { wasAssociatedWith ag; startedAtTime t0; endedAtTime t1 }`.
    val activity: id: Node -> ProvBuilder
    /// Enters an `entity id { }` block: `entity e { wasGeneratedBy a }`.
    val entity: id: Node -> ProvBuilder
    /// Enters an `agent id { }` block: `agent ag { }`.
    val agent: id: Node -> ProvBuilder
