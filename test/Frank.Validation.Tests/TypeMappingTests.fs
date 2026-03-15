module Frank.Validation.Tests.TypeMappingTests

open System
open Expecto
open Frank.Validation

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
