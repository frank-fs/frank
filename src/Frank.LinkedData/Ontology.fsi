module Frank.LinkedData.Ontology

open VDS.RDF
open Frank.Semantic

val toGraph: ontology: OntologyDecl -> IGraph

val toJsonLdContext: ontology: OntologyDecl -> string
