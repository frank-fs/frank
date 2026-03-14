module Frank.Validation.Tests.TypeMappingTests

open System
open Expecto
open Frank.Validation

[<Tests>]
let mapTypeTests =
    testList
        "TypeMapping.mapType"
        [ testCase "string maps to XsdString"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<string>) (Some XsdString) ""

          testCase "int maps to XsdInteger"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<int>) (Some XsdInteger) ""

          testCase "int32 maps to XsdInteger"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<int32>) (Some XsdInteger) ""

          testCase "int64 maps to XsdLong"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<int64>) (Some XsdLong) ""

          testCase "float maps to XsdDouble"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<float>) (Some XsdDouble) ""

          testCase "double maps to XsdDouble"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<double>) (Some XsdDouble) ""

          testCase "decimal maps to XsdDecimal"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<decimal>) (Some XsdDecimal) ""

          testCase "bool maps to XsdBoolean"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<bool>) (Some XsdBoolean) ""

          testCase "DateTimeOffset maps to XsdDateTimeStamp"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<DateTimeOffset>) (Some XsdDateTimeStamp) ""

          testCase "DateTime maps to XsdDateTime"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<DateTime>) (Some XsdDateTime) ""

          testCase "DateOnly maps to XsdDate"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<DateOnly>) (Some XsdDate) ""

          testCase "TimeOnly maps to XsdTime"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<TimeOnly>) (Some XsdTime) ""

          testCase "TimeSpan maps to XsdDuration"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<TimeSpan>) (Some XsdDuration) ""

          testCase "Uri maps to XsdAnyUri"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<Uri>) (Some XsdAnyUri) ""

          testCase "byte array maps to XsdBase64Binary"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<byte[]>) (Some XsdBase64Binary) ""

          testCase "Guid maps to XsdString"
          <| fun _ -> Expect.equal (TypeMapping.mapType typeof<Guid>) (Some XsdString) ""

          testCase "unsupported type returns None"
          <| fun _ -> Expect.isNone (TypeMapping.mapType typeof<obj>) "obj should not map"

          testCase "custom class returns None"
          <| fun _ ->
              Expect.isNone (TypeMapping.mapType typeof<System.Text.StringBuilder>) "StringBuilder should not map" ]

[<Tests>]
let xsdUriTests =
    testList
        "TypeMapping.xsdUri"
        [ testCase "XsdString URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdString) (Uri "http://www.w3.org/2001/XMLSchema#string") ""

          testCase "XsdInteger URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdInteger) (Uri "http://www.w3.org/2001/XMLSchema#integer") ""

          testCase "XsdLong URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdLong) (Uri "http://www.w3.org/2001/XMLSchema#long") ""

          testCase "XsdDouble URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdDouble) (Uri "http://www.w3.org/2001/XMLSchema#double") ""

          testCase "XsdDecimal URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdDecimal) (Uri "http://www.w3.org/2001/XMLSchema#decimal") ""

          testCase "XsdBoolean URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdBoolean) (Uri "http://www.w3.org/2001/XMLSchema#boolean") ""

          testCase "XsdDateTimeStamp URI"
          <| fun _ ->
              Expect.equal
                  (TypeMapping.xsdUri XsdDateTimeStamp)
                  (Uri "http://www.w3.org/2001/XMLSchema#dateTimeStamp")
                  ""

          testCase "XsdDateTime URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdDateTime) (Uri "http://www.w3.org/2001/XMLSchema#dateTime") ""

          testCase "XsdDate URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdDate) (Uri "http://www.w3.org/2001/XMLSchema#date") ""

          testCase "XsdTime URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdTime) (Uri "http://www.w3.org/2001/XMLSchema#time") ""

          testCase "XsdDuration URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdDuration) (Uri "http://www.w3.org/2001/XMLSchema#duration") ""

          testCase "XsdAnyUri URI"
          <| fun _ -> Expect.equal (TypeMapping.xsdUri XsdAnyUri) (Uri "http://www.w3.org/2001/XMLSchema#anyURI") ""

          testCase "XsdBase64Binary URI"
          <| fun _ ->
              Expect.equal (TypeMapping.xsdUri XsdBase64Binary) (Uri "http://www.w3.org/2001/XMLSchema#base64Binary") ""

          testCase "Custom URI passthrough"
          <| fun _ ->
              let customUri = Uri("http://example.org/custom")
              Expect.equal (TypeMapping.xsdUri (Custom customUri)) customUri "" ]
