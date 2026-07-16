namespace Frank.Semantic

open System
open VDS.RDF

type PropertyDecl = { Iri: Uri; Domain: Uri }

type ClassDecl =
    { Iri: Uri
      EquivalentClass: Uri option
      SeeAlso: Uri list
      Properties: PropertyDecl list }

type OntologyDecl =
    { Classes: ClassDecl list
      ContextBases: Uri list }

module Triples =
    val uriNode: g: IGraph -> iri: string -> INode
    val qnameNode: g: IGraph -> qname: string -> INode

    val assert3: g: IGraph -> s: INode -> p: INode -> o: INode -> unit
