namespace Frank.Provenance

[<RequireQualifiedAccess>]
module ProvVocabulary =

    let Namespace = "http://www.w3.org/ns/prov#"

    module Class =
        let Activity = Namespace + "Activity"
        let Entity = Namespace + "Entity"
        let Agent = Namespace + "Agent"

    module Property =
        let WasGeneratedBy = Namespace + "wasGeneratedBy"
        let WasAssociatedWith = Namespace + "wasAssociatedWith"
        let Used = Namespace + "used"
        let StartedAtTime = Namespace + "startedAtTime"
        let EndedAtTime = Namespace + "endedAtTime"

    module Rdf =
        let Type = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"

    module Xsd =
        let DateTime = "http://www.w3.org/2001/XMLSchema#dateTime"
        let Integer = "http://www.w3.org/2001/XMLSchema#integer"

    module Http =
        let Namespace = "http://www.w3.org/2011/http#"
        let MethodName = Namespace + "methodName"
        let StatusCodeValue = Namespace + "statusCodeValue"
