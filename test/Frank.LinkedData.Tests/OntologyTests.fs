module Frank.LinkedData.Tests.OntologyTests

open System
open Expecto
open Frank.Semantic
open Frank.LinkedData
open VDS.RDF

let private sampleOntology: OntologyDecl =
    { Classes =
        [ { Iri = Uri "https://schema.org/Game"
            EquivalentClass = None
            SeeAlso = []
            Properties =
              [ { Iri = Uri "https://schema.org/position"
                  Domain = Uri "https://schema.org/Game" } ] } ]
      ContextBases = [ Uri "https://schema.org/" ] }

[<Tests>]
let tests =
    testList
        "Ontology interpreter"
        [ test "toGraph emits owl:Class for each class" {
              let g = Ontology.toGraph sampleOntology

              let rdfType =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

              let owlClass =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2002/07/owl#Class")

              Expect.isNonEmpty (g.GetTriplesWithPredicateObject(rdfType, owlClass) |> Seq.toList) "owl:Class present"
          }
          test "toGraph emits rdfs:domain for each property" {
              let g = Ontology.toGraph sampleOntology

              let domain =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2000/01/rdf-schema#domain")

              let cls = g.CreateUriNode(UriFactory.Create "https://schema.org/Game")
              Expect.isNonEmpty (g.GetTriplesWithPredicateObject(domain, cls) |> Seq.toList) "domain → class present"
          }
          test "toJsonLdContext lists external bases (trailing slash trimmed)" {
              let ctx = Ontology.toJsonLdContext sampleOntology
              Expect.stringContains ctx "\"https://schema.org\"" "base IRI present, slash trimmed"
              Expect.stringContains ctx "@context" "is a @context document"
          } ]
