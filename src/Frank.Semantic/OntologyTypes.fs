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
    let uriNode (g: IGraph) (iri: string) : INode = g.CreateUriNode(UriFactory.Create iri)
    let qnameNode (g: IGraph) (qname: string) : INode = g.CreateUriNode(g.ResolveQName qname)

    let assert3 (g: IGraph) (s: INode) (p: INode) (o: INode) : unit = g.Assert(Triple(s, p, o)) |> ignore
