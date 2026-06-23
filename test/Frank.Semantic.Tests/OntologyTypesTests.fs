module Frank.Semantic.Tests.OntologyTypesTests

open System
open Expecto
open Frank.Semantic
open VDS.RDF

[<Tests>]
let tests =
    testList
        "OntologyTypes + Triples"
        [ test "Triples.assert3 adds a triple resolvable by predicate/object" {
              let g = new Graph() :> IGraph
              g.NamespaceMap.AddNamespace("rdf", UriFactory.Create "http://www.w3.org/1999/02/22-rdf-syntax-ns#")
              g.NamespaceMap.AddNamespace("owl", UriFactory.Create "http://www.w3.org/2002/07/owl#")
              let s = Triples.uriNode g "https://schema.org/Game"
              let p = Triples.qnameNode g "rdf:type"
              let o = Triples.qnameNode g "owl:Class"
              Triples.assert3 g s p o
              Expect.isNonEmpty (g.GetTriplesWithPredicateObject(p, o) |> Seq.toList) "owl:Class triple present"
          }
          test "OntologyDecl is constructible with required Iri/Domain" {
              let d: OntologyDecl =
                  { Classes =
                      [ { Iri = Uri "https://schema.org/Game"
                          EquivalentClass = None
                          SeeAlso = []
                          Properties =
                            [ { Iri = Uri "https://schema.org/position"
                                Domain = Uri "https://schema.org/Game" } ] } ]
                    ContextBases = [ Uri "https://schema.org" ] }

              Expect.equal d.Classes.Head.Properties.Head.Domain (Uri "https://schema.org/Game") "domain required + set"
          } ]
