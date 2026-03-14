module Frank.Provenance.Tests.VocabularyTests

open Expecto
open Frank.Provenance

[<Tests>]
let vocabularyTests =
    testList
        "ProvVocabulary"
        [ testList
              "PROV-O namespace"
              [ test "Namespace is correct" {
                    Expect.equal
                        ProvVocabulary.Namespace
                        "http://www.w3.org/ns/prov#"
                        "PROV namespace should be correct"
                } ]

          testList
              "Class URIs start with PROV namespace"
              [ test "Entity" {
                    Expect.isTrue
                        (ProvVocabulary.Class.Entity.StartsWith(ProvVocabulary.Namespace))
                        "Entity should start with PROV namespace"
                }

                test "Activity" {
                    Expect.isTrue
                        (ProvVocabulary.Class.Activity.StartsWith(ProvVocabulary.Namespace))
                        "Activity should start with PROV namespace"
                }

                test "Agent" {
                    Expect.isTrue
                        (ProvVocabulary.Class.Agent.StartsWith(ProvVocabulary.Namespace))
                        "Agent should start with PROV namespace"
                }

                test "SoftwareAgent" {
                    Expect.isTrue
                        (ProvVocabulary.Class.SoftwareAgent.StartsWith(ProvVocabulary.Namespace))
                        "SoftwareAgent should start with PROV namespace"
                }

                test "Person" {
                    Expect.isTrue
                        (ProvVocabulary.Class.Person.StartsWith(ProvVocabulary.Namespace))
                        "Person should start with PROV namespace"
                } ]

          testList
              "Property URIs start with PROV namespace"
              [ test "WasGeneratedBy" {
                    Expect.isTrue
                        (ProvVocabulary.Property.WasGeneratedBy.StartsWith(ProvVocabulary.Namespace))
                        "WasGeneratedBy should start with PROV namespace"
                }

                test "WasDerivedFrom" {
                    Expect.isTrue
                        (ProvVocabulary.Property.WasDerivedFrom.StartsWith(ProvVocabulary.Namespace))
                        "WasDerivedFrom should start with PROV namespace"
                }

                test "WasAttributedTo" {
                    Expect.isTrue
                        (ProvVocabulary.Property.WasAttributedTo.StartsWith(ProvVocabulary.Namespace))
                        "WasAttributedTo should start with PROV namespace"
                }

                test "WasAssociatedWith" {
                    Expect.isTrue
                        (ProvVocabulary.Property.WasAssociatedWith.StartsWith(ProvVocabulary.Namespace))
                        "WasAssociatedWith should start with PROV namespace"
                }

                test "Used" {
                    Expect.isTrue
                        (ProvVocabulary.Property.Used.StartsWith(ProvVocabulary.Namespace))
                        "Used should start with PROV namespace"
                }

                test "StartedAtTime" {
                    Expect.isTrue
                        (ProvVocabulary.Property.StartedAtTime.StartsWith(ProvVocabulary.Namespace))
                        "StartedAtTime should start with PROV namespace"
                }

                test "EndedAtTime" {
                    Expect.isTrue
                        (ProvVocabulary.Property.EndedAtTime.StartsWith(ProvVocabulary.Namespace))
                        "EndedAtTime should start with PROV namespace"
                }

                test "GeneratedAtTime" {
                    Expect.isTrue
                        (ProvVocabulary.Property.GeneratedAtTime.StartsWith(ProvVocabulary.Namespace))
                        "GeneratedAtTime should start with PROV namespace"
                } ]

          testList
              "Frank extension URIs start with Frank namespace"
              [ test "LlmAgent" {
                    Expect.isTrue
                        (ProvVocabulary.Frank.LlmAgent.StartsWith(ProvVocabulary.FrankNamespace))
                        "LlmAgent should start with Frank namespace"
                }

                test "httpMethod" {
                    Expect.isTrue
                        (ProvVocabulary.Frank.httpMethod.StartsWith(ProvVocabulary.FrankNamespace))
                        "httpMethod should start with Frank namespace"
                }

                test "eventName" {
                    Expect.isTrue
                        (ProvVocabulary.Frank.eventName.StartsWith(ProvVocabulary.FrankNamespace))
                        "eventName should start with Frank namespace"
                }

                test "stateName" {
                    Expect.isTrue
                        (ProvVocabulary.Frank.stateName.StartsWith(ProvVocabulary.FrankNamespace))
                        "stateName should start with Frank namespace"
                }

                test "agentModel" {
                    Expect.isTrue
                        (ProvVocabulary.Frank.agentModel.StartsWith(ProvVocabulary.FrankNamespace))
                        "agentModel should start with Frank namespace"
                } ]

          testList
              "Rdf vocabulary"
              [ test "Type URI is correct" {
                    Expect.equal
                        ProvVocabulary.Rdf.Type
                        "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"
                        "rdf:type should be correct"
                } ]

          testList
              "Xsd vocabulary"
              [ test "DateTime starts with XSD namespace" {
                    Expect.isTrue
                        (ProvVocabulary.Xsd.DateTime.StartsWith(ProvVocabulary.XsdNamespace))
                        "xsd:dateTime should start with XSD namespace"
                } ]

          testList
              "Rdfs vocabulary"
              [ test "label URI is correct" {
                    Expect.equal
                        ProvVocabulary.Rdfs.label
                        "http://www.w3.org/2000/01/rdf-schema#label"
                        "rdfs:label should be correct"
                } ] ]
