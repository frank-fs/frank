module Frank.Validation.Shapes

open VDS.RDF.Shacl
open Frank.Semantic

/// THE single place SHACL triples are built. Total over ShapeDecl, correct by construction.
val toShapesGraph: shapes: ShapeDecl list -> ShapesGraph
