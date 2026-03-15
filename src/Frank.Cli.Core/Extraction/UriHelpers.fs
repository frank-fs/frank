namespace Frank.Cli.Core.Extraction

open System
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Analysis

/// Shared URI construction helpers for extraction modules.
/// Constitution VIII: No Duplicated Logic.
module UriHelpers =

    /// Create class URI: {baseUri}/types/{typeName}
    let classUri (baseUri: string) (typeName: string) = Uri(baseUri + "/types/" + typeName)

    /// Create property URI: {baseUri}/properties/{typeName}/{fieldName}
    let propertyUri (baseUri: string) (typeName: string) (fieldName: string) =
        Uri(baseUri + "/properties/" + typeName + "/" + fieldName)

    /// Remove route decorations (/, {, }) to create a slug
    let routeToSlug (route: string) =
        route.TrimStart('/').Replace("/", "_").Replace("{", "").Replace("}", "")

    /// Create resource URI: {baseUri}/resources/{cleanRoute}
    let resourceUri (baseUri: string) (route: string) =
        let cleanRoute = route.TrimStart('/')
        Uri(baseUri + "/resources/" + cleanRoute)

    /// Map F# FieldKind to XSD range URI and isObjectProperty flag
    let rec fieldKindToRange (baseUri: string) (kind: FieldKind) : Uri * bool =
        match kind with
        | Primitive xsdType ->
            let rangeUri =
                match xsdType with
                | "xsd:string" -> Uri Xsd.String
                | "xsd:integer" -> Uri Xsd.Integer
                | "xsd:long" -> Uri Xsd.Integer
                | "xsd:double"
                | "xsd:float" -> Uri Xsd.Double
                | "xsd:boolean" -> Uri Xsd.Boolean
                | "xsd:dateTime" -> Uri Xsd.DateTime
                | _ -> Uri Xsd.String

            rangeUri, false
        | Guid -> Uri Xsd.String, false
        | Optional inner -> fieldKindToRange baseUri inner
        | Collection element -> fieldKindToRange baseUri element
        | Reference typeName -> classUri baseUri typeName, true
