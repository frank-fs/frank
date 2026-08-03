namespace Frank.Provenance

[<Struct; RequireQualifiedAccess>]
type ProvClass =
    | Activity
    | Entity
    | Agent

module ProvClass =
    [<Literal>]
    let private ProvNamespace = "http://www.w3.org/ns/prov#"

    let toIri (c: ProvClass) : string =
        match c with
        | ProvClass.Activity -> ProvNamespace + "Activity"
        | ProvClass.Entity -> ProvNamespace + "Entity"
        | ProvClass.Agent -> ProvNamespace + "Agent"

[<Struct; RequireQualifiedAccess>]
type ProvRelation =
    | WasGeneratedBy
    | WasAssociatedWith
    | Used
    | StartedAtTime
    | EndedAtTime
    | WasDerivedFrom
    | SpecializationOf

module ProvRelation =
    [<Literal>]
    let private ProvNamespace = "http://www.w3.org/ns/prov#"

    let toIri (r: ProvRelation) : string =
        match r with
        | ProvRelation.WasGeneratedBy -> ProvNamespace + "wasGeneratedBy"
        | ProvRelation.WasAssociatedWith -> ProvNamespace + "wasAssociatedWith"
        | ProvRelation.Used -> ProvNamespace + "used"
        | ProvRelation.StartedAtTime -> ProvNamespace + "startedAtTime"
        | ProvRelation.EndedAtTime -> ProvNamespace + "endedAtTime"
        | ProvRelation.WasDerivedFrom -> ProvNamespace + "wasDerivedFrom"
        | ProvRelation.SpecializationOf -> ProvNamespace + "specializationOf"
