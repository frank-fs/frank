namespace Frank.Validation

open Frank.Rdf

/// The interpreter: projects the hand-authored ShapeDecl model onto Frank.Rdf's Doc/Node/Value --
/// the single SHACL graph-builder, no parallel triple model.
module Shacl =
    /// Builds a well-formed rdf:list: head node + rdf:first/rdf:rest/rdf:nil triples, one blank node
    /// per element. An empty list's head is rdf:nil itself -- no blank nodes minted.
    val internal rdfList: items: Value list -> Node * (Node * string * Value) list

    /// Projects a PropertyPath onto its sh:path representation: a bare IRI for Predicate, or a blank
    /// node carrying the matching sh:inversePath/sh:alternativePath/sh:zeroOrMorePath/sh:oneOrMorePath/
    /// sh:zeroOrOnePath/rdf:list-of-paths structure for the other six cases.
    val internal pathNode: path: PropertyPath -> Node * (Node * string * Value) list

    /// Parses a SPARQL constraint's query text exactly as it is emitted into the shapes graph (the
    /// constraint's declared prefixes rendered as PREFIX lines, then the author's query), using
    /// dotNetRDF's own SPARQL parser. Internal: callers get this enforced for them by toShapesGraph.
    val internal parseSparqlConstraint: sc: SparqlConstraint -> Result<VDS.RDF.Query.SparqlQuery, string>

    /// Projects a ShapeDecl list onto a Doc: one sh:NodeShape/sh:PropertyShape pair per shape,
    /// blank nodes for anonymous property shapes and path expressions. A total projection -- it never
    /// raises, including for a sh:sparql query that doesn't parse (toShapesGraph is where that is
    /// rejected).
    val toDoc: shapes: ShapeDecl list -> Doc

    /// toDoc >> Doc.toGraph >> ShapesGraph -- what Validation.fs's `validate` consumes.
    ///
    /// Raises InvalidOperationException if any reachable sh:sparql constraint's query fails to parse
    /// as SPARQL. That is deliberate and matches the design doc's error-handling table: a malformed
    /// author-supplied query is a shape bug, surfaced once at shape-authoring time, never deferred to
    /// fail every request to the resource it guards.
    val toShapesGraph: shapes: ShapeDecl list -> VDS.RDF.Shacl.ShapesGraph

    /// A typed wrapper over VDS.RDF.Shacl.Validation.Report -- never exposes the raw dotNetRDF
    /// Result type to callers.
    val validate: shapesGraph: VDS.RDF.Shacl.ShapesGraph -> dataGraph: VDS.RDF.IGraph -> ValidationOutcome

    /// Projects a Violation list back onto a Doc as a real sh:ValidationReport -- the inverse
    /// direction of toDoc/validate, used by the 422 application/ld+json response path.
    val reportToDoc: violations: Violation list -> Doc
