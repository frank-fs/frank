namespace Frank.Provenance

open System
open Frank.Rdf

module Prov =
    let activity (id: Node) : Description = describe id { typ (ProvClass.toIri ProvClass.Activity) }
    let entity (id: Node) : Description = describe id { typ (ProvClass.toIri ProvClass.Entity) }
    let agent (id: Node) : Description = describe id { typ (ProvClass.toIri ProvClass.Agent) }

    let private addProperty (predicate: string) (value: Value) (d: Description) : Description =
        { d with
            Statements = d.Statements @ [ predicate, value ] }

    let wasGeneratedBy (activity: Node) (d: Description) : Description =
        d |> addProperty (ProvRelation.toIri ProvRelation.WasGeneratedBy) (Value.Node activity)

    let wasAssociatedWith (agent: Node) (d: Description) : Description =
        d |> addProperty (ProvRelation.toIri ProvRelation.WasAssociatedWith) (Value.Node agent)

    let used (entity: Node) (d: Description) : Description =
        d |> addProperty (ProvRelation.toIri ProvRelation.Used) (Value.Node entity)

    let startedAtTime (t: DateTimeOffset) (d: Description) : Description =
        d
        |> addProperty (ProvRelation.toIri ProvRelation.StartedAtTime) (Value.Literal(Literal.DateTime t))

    let endedAtTime (t: DateTimeOffset) (d: Description) : Description =
        d
        |> addProperty (ProvRelation.toIri ProvRelation.EndedAtTime) (Value.Literal(Literal.DateTime t))

    let wasDerivedFrom (entity: Node) (d: Description) : Description =
        d |> addProperty (ProvRelation.toIri ProvRelation.WasDerivedFrom) (Value.Node entity)

    let specializationOf (entity: Node) (d: Description) : Description =
        d |> addProperty (ProvRelation.toIri ProvRelation.SpecializationOf) (Value.Node entity)
