namespace Frank.Validation

open System

/// Functions for mapping XSD datatypes to URIs used in SHACL property shapes.
module TypeMapping =

    /// Get the XSD URI string for a datatype.
    let xsdUri (dt: XsdDatatype) : Uri =
        let xsd = "http://www.w3.org/2001/XMLSchema#"

        match dt with
        | XsdString -> Uri(xsd + "string")
        | XsdInteger -> Uri(xsd + "integer")
        | XsdLong -> Uri(xsd + "long")
        | XsdDouble -> Uri(xsd + "double")
        | XsdDecimal -> Uri(xsd + "decimal")
        | XsdBoolean -> Uri(xsd + "boolean")
        | XsdDateTimeStamp -> Uri(xsd + "dateTimeStamp")
        | XsdDateTime -> Uri(xsd + "dateTime")
        | XsdDate -> Uri(xsd + "date")
        | XsdTime -> Uri(xsd + "time")
        | XsdDuration -> Uri(xsd + "duration")
        | XsdAnyUri -> Uri(xsd + "anyURI")
        | XsdBase64Binary -> Uri(xsd + "base64Binary")
        | Custom uri -> uri
