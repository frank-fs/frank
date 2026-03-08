namespace Frank.Provenance

/// W3C PROV-O vocabulary constants.
/// See https://www.w3.org/TR/prov-o/
[<RequireQualifiedAccess>]
module Vocabulary =

    /// The W3C PROV namespace URI.
    [<Literal>]
    let Namespace = "http://www.w3.org/ns/prov#"

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
