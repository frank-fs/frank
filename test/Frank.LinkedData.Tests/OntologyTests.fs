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

let private enrichedOntology: OntologyDecl =
    { Classes =
        [ { Iri = Uri "https://example.org/Thing"
            EquivalentClass = Some(Uri "https://schema.org/Thing")
            SeeAlso = [ Uri "https://example.org/ref" ]
            Properties =
              [ { Iri = Uri "https://example.org/name"
                  Domain = Uri "https://example.org/Thing" } ] } ]
      ContextBases = [] }

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
          }
          test "toGraph emits owl:equivalentClass when EquivalentClass is Some" {
              let g = Ontology.toGraph enrichedOntology

              let equivalentClass =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2002/07/owl#equivalentClass")

              let schemaObj = g.CreateUriNode(UriFactory.Create "https://schema.org/Thing")

              Expect.isNonEmpty
                  (g.GetTriplesWithPredicateObject(equivalentClass, schemaObj) |> Seq.toList)
                  "owl:equivalentClass triple present"
          }
          test "toGraph emits rdfs:seeAlso for each SeeAlso entry" {
              let g = Ontology.toGraph enrichedOntology

              let seeAlso =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/2000/01/rdf-schema#seeAlso")

              let refObj = g.CreateUriNode(UriFactory.Create "https://example.org/ref")

              Expect.isNonEmpty
                  (g.GetTriplesWithPredicateObject(seeAlso, refObj) |> Seq.toList)
                  "rdfs:seeAlso triple present"
          }
          test "toGraph emits rdf:type rdf:Property for each property node" {
              let g = Ontology.toGraph enrichedOntology

              let rdfType =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

              let rdfProperty =
                  g.CreateUriNode(UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#Property")

              Expect.isNonEmpty
                  (g.GetTriplesWithPredicateObject(rdfType, rdfProperty) |> Seq.toList)
                  "rdf:type rdf:Property triple present"
          } ]
