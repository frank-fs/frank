module Frank.LinkedData.Ontology

open System
open VDS.RDF
open Frank.Semantic

let private rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"
let private rdfs = "http://www.w3.org/2000/01/rdf-schema#"
let private owl = "http://www.w3.org/2002/07/owl#"

/// Precondition: `u` must be an absolute, dereferenceable URI. Ontology.toGraph never rebases a
/// relative Uri against a runtime host, so a relative Uri reaching this point is a codegen
/// misclassification — fail loud here, before the throwing .AbsoluteUri accessor, naming the
/// offending field and its owning class (#396).
let private assertAbsolute (paramName: string) (fieldLabel: string) (classIri: Uri) (u: Uri) : unit =
    if not u.IsAbsoluteUri then
        invalidArg
            paramName
            $"OntologyDecl class '{classIri}' declares a relative {fieldLabel} Uri '{u.OriginalString}'; {fieldLabel} must be an absolute, dereferenceable URI — Ontology.toGraph never rebases a relative Uri against a runtime host."

let private addClass (g: IGraph) (c: ClassDecl) : unit =
    if
        not (
            g.NamespaceMap.HasNamespace "rdf"
            && g.NamespaceMap.HasNamespace "rdfs"
            && g.NamespaceMap.HasNamespace "owl"
        )
    then
        invalidOp "addClass requires rdf/rdfs/owl namespaces registered on the graph"

    assertAbsolute "classIri" "Iri" c.Iri c.Iri
    let subj = Triples.uriNode g c.Iri.AbsoluteUri
    Triples.assert3 g subj (Triples.qnameNode g "rdf:type") (Triples.qnameNode g "owl:Class")

    match c.EquivalentClass with
    | Some e -> Triples.assert3 g subj (Triples.qnameNode g "owl:equivalentClass") (Triples.uriNode g e.AbsoluteUri)
    | None -> ()

    for s in c.SeeAlso do
        assertAbsolute "seeAlso" "rdfs:seeAlso" c.Iri s
        Triples.assert3 g subj (Triples.qnameNode g "rdfs:seeAlso") (Triples.uriNode g s.AbsoluteUri)

    for p in c.Properties do
        assertAbsolute "propertyIri" "PropertyDecl.Iri" c.Iri p.Iri
        let pNode = Triples.uriNode g p.Iri.AbsoluteUri
        Triples.assert3 g pNode (Triples.qnameNode g "rdf:type") (Triples.qnameNode g "rdf:Property")
        Triples.assert3 g pNode (Triples.qnameNode g "rdfs:domain") (Triples.uriNode g p.Domain.AbsoluteUri)

let toGraph (ontology: OntologyDecl) : IGraph =
    let g = new Graph() :> IGraph
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create rdf)
    g.NamespaceMap.AddNamespace("rdfs", UriFactory.Create rdfs)
    g.NamespaceMap.AddNamespace("owl", UriFactory.Create owl)

    for c in ontology.Classes do
        addClass g c

    g

let toJsonLdContext (ontology: OntologyDecl) : string =
    let items =
        ontology.ContextBases
        |> List.map (fun u -> "\"" + u.AbsoluteUri.TrimEnd('/') + "\"")
        |> String.concat ","

    "{\"@context\":[" + items + "]}"
