module Frank.Validation.Validator

open VDS.RDF
open VDS.RDF.Shacl
open VDS.RDF.Shacl.Validation

/// Thin pure wrapper. Delegates to ShapesGraph.Validate.
val validate: shapes: ShapesGraph -> data: IGraph -> Report
