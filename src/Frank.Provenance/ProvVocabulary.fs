namespace Frank.Provenance

/// W3C PROV-O vocabulary constants and Frank extension vocabulary.
/// See https://www.w3.org/TR/prov-o/
[<RequireQualifiedAccess>]
module ProvVocabulary =

    /// The W3C PROV namespace URI.
    [<Literal>]
    let Namespace = "http://www.w3.org/ns/prov#"

    /// The Frank provenance extension namespace URI.
    [<Literal>]
    let FrankNamespace = "https://frank-web.dev/ns/provenance#"

    /// The RDF namespace URI.
    [<Literal>]
    let RdfNamespace = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"

    /// The RDF Schema namespace URI.
    [<Literal>]
    let RdfsNamespace = "http://www.w3.org/2000/01/rdf-schema#"

    /// The XML Schema namespace URI.
    [<Literal>]
    let XsdNamespace = "http://www.w3.org/2001/XMLSchema#"

    /// Common PROV-O class URIs.
    [<RequireQualifiedAccess>]
    module Class =

        [<Literal>]
        let Entity = Namespace + "Entity"

        [<Literal>]
        let Activity = Namespace + "Activity"

        [<Literal>]
        let Agent = Namespace + "Agent"

        [<Literal>]
        let SoftwareAgent = Namespace + "SoftwareAgent"

        [<Literal>]
        let Person = Namespace + "Person"

    /// Common PROV-O property URIs.
    [<RequireQualifiedAccess>]
    module Property =

        [<Literal>]
        let WasGeneratedBy = Namespace + "wasGeneratedBy"

        [<Literal>]
        let WasDerivedFrom = Namespace + "wasDerivedFrom"

        [<Literal>]
        let WasAttributedTo = Namespace + "wasAttributedTo"

        [<Literal>]
        let WasAssociatedWith = Namespace + "wasAssociatedWith"

        [<Literal>]
        let Used = Namespace + "used"

        [<Literal>]
        let ActedOnBehalfOf = Namespace + "actedOnBehalfOf"

        [<Literal>]
        let StartedAtTime = Namespace + "startedAtTime"

        [<Literal>]
        let EndedAtTime = Namespace + "endedAtTime"

        [<Literal>]
        let GeneratedAtTime = Namespace + "generatedAtTime"

        [<Literal>]
        let AtLocation = Namespace + "atLocation"

        [<Literal>]
        let Value = Namespace + "value"

    /// Frank extension vocabulary constants.
    [<RequireQualifiedAccess>]
    module Frank =

        [<Literal>]
        let LlmAgent = FrankNamespace + "LlmAgent"

        [<Literal>]
        let httpMethod = FrankNamespace + "httpMethod"

        [<Literal>]
        let eventName = FrankNamespace + "eventName"

        [<Literal>]
        let stateName = FrankNamespace + "stateName"

        [<Literal>]
        let agentModel = FrankNamespace + "agentModel"

    /// RDF vocabulary constants.
    [<RequireQualifiedAccess>]
    module Rdf =

        [<Literal>]
        let Type = RdfNamespace + "type"

    /// XML Schema datatype constants.
    [<RequireQualifiedAccess>]
    module Xsd =

        [<Literal>]
        let DateTime = XsdNamespace + "dateTime"

    /// RDF Schema vocabulary constants.
    [<RequireQualifiedAccess>]
    module Rdfs =

        [<Literal>]
        let label = RdfsNamespace + "label"
