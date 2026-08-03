module Frank.Provenance.Tests.ProvVocabularyTests

open Expecto
open Frank.Provenance

[<Tests>]
let tests =
    testList
        "ProvVocabulary"
        [ test "ProvClass.toIri produces the correct absolute PROV-O IRI for every case" {
              Expect.equal (ProvClass.toIri ProvClass.Activity) "http://www.w3.org/ns/prov#Activity" ""
              Expect.equal (ProvClass.toIri ProvClass.Entity) "http://www.w3.org/ns/prov#Entity" ""
              Expect.equal (ProvClass.toIri ProvClass.Agent) "http://www.w3.org/ns/prov#Agent" ""
          }

          test "ProvRelation.toIri produces the correct absolute PROV-O IRI for every case" {
              Expect.equal (ProvRelation.toIri ProvRelation.WasGeneratedBy) "http://www.w3.org/ns/prov#wasGeneratedBy" ""
              Expect.equal (ProvRelation.toIri ProvRelation.WasAssociatedWith) "http://www.w3.org/ns/prov#wasAssociatedWith" ""
              Expect.equal (ProvRelation.toIri ProvRelation.Used) "http://www.w3.org/ns/prov#used" ""
              Expect.equal (ProvRelation.toIri ProvRelation.StartedAtTime) "http://www.w3.org/ns/prov#startedAtTime" ""
              Expect.equal (ProvRelation.toIri ProvRelation.EndedAtTime) "http://www.w3.org/ns/prov#endedAtTime" ""
              Expect.equal (ProvRelation.toIri ProvRelation.WasDerivedFrom) "http://www.w3.org/ns/prov#wasDerivedFrom" ""
              Expect.equal (ProvRelation.toIri ProvRelation.SpecializationOf) "http://www.w3.org/ns/prov#specializationOf" ""
          } ]
