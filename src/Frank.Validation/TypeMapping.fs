namespace Frank.Validation

open System

/// Functions for mapping F# CLR types to XSD datatypes used in SHACL property shapes.
module TypeMapping =

    /// Map an F# CLR type to its XSD datatype. Returns None for types
    /// that require sh:node references (records, collections) rather than sh:datatype.
    let mapType (typ: Type) : XsdDatatype option =
        match typ with
        | t when t = typeof<string> -> Some XsdString
        | t when t = typeof<int> || t = typeof<int32> -> Some XsdInteger
        | t when t = typeof<int64> -> Some XsdLong
        | t when t = typeof<float> || t = typeof<double> -> Some XsdDouble
        | t when t = typeof<decimal> -> Some XsdDecimal
        | t when t = typeof<bool> -> Some XsdBoolean
        | t when t = typeof<DateTimeOffset> -> Some XsdDateTimeStamp
        | t when t = typeof<DateTime> -> Some XsdDateTime
        | t when t = typeof<DateOnly> -> Some XsdDate
        | t when t = typeof<TimeOnly> -> Some XsdTime
        | t when t = typeof<TimeSpan> -> Some XsdDuration
        | t when t = typeof<Uri> -> Some XsdAnyUri
        | t when t = typeof<byte[]> -> Some XsdBase64Binary
        | t when t = typeof<Guid> -> Some XsdString // + pattern constraint added by derivation
        | _ -> None

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
