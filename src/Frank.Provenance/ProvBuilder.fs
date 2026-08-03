namespace Frank.Provenance

open System
open Frank.Rdf

[<AutoOpen>]
module ProvBuilderModule =
    [<Sealed>]
    type ProvBuilder(initial: Description) =
        member _.Yield(_) : Description = initial
        member _.Zero() : Description = initial
        member _.Run(d: Description) : Description = d

        [<CustomOperation("wasGeneratedBy")>]
        member _.WasGeneratedBy(d: Description, activity: Node) : Description = d |> Prov.wasGeneratedBy activity

        [<CustomOperation("wasAssociatedWith")>]
        member _.WasAssociatedWith(d: Description, agent: Node) : Description = d |> Prov.wasAssociatedWith agent

        [<CustomOperation("used")>]
        member _.Used(d: Description, entity: Node) : Description = d |> Prov.used entity

        [<CustomOperation("startedAtTime")>]
        member _.StartedAtTime(d: Description, t: DateTimeOffset) : Description = d |> Prov.startedAtTime t

        [<CustomOperation("endedAtTime")>]
        member _.EndedAtTime(d: Description, t: DateTimeOffset) : Description = d |> Prov.endedAtTime t

        [<CustomOperation("wasDerivedFrom")>]
        member _.WasDerivedFrom(d: Description, entity: Node) : Description = d |> Prov.wasDerivedFrom entity

        [<CustomOperation("specializationOf")>]
        member _.SpecializationOf(d: Description, entity: Node) : Description = d |> Prov.specializationOf entity

    let activity (id: Node) = ProvBuilder(Prov.activity id)
    let entity (id: Node) = ProvBuilder(Prov.entity id)
    let agent (id: Node) = ProvBuilder(Prov.agent id)
