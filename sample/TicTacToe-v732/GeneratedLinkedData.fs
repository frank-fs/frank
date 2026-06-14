// FRANK-STUB(AT-S5): hand-authored stand-in for MSBuild-generated GeneratedLinkedData — replace with frank-semantic codegen.
module TicTacToe.GeneratedLinkedData

open VDS.RDF
open VDS.RDF.Parsing

let jsonLdContext: string = """{"@context":["https://schema.org"]}"""

let graph: IGraph =
    let g = new Graph()
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#"))
    g.NamespaceMap.AddNamespace("rdfs", UriFactory.Create("http://www.w3.org/2000/01/rdf-schema#"))
    g.NamespaceMap.AddNamespace("owl", UriFactory.Create("http://www.w3.org/2002/07/owl#"))

    g.Assert(
        Triple(
            g.CreateUriNode(UriFactory.Create("https://schema.org/Game")),
            g.CreateUriNode(g.ResolveQName("rdf:type")),
            g.CreateUriNode(g.ResolveQName("owl:Class"))
        )
    )

    g.Assert(
        Triple(
            g.CreateUriNode(UriFactory.Create("https://schema.org/MoveAction")),
            g.CreateUriNode(g.ResolveQName("rdfs:seeAlso")),
            g.CreateUriNode(UriFactory.Create("https://www.wikidata.org/wiki/Q11907"))
        )
    )

    g
