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

    /// Projects a ShapeDecl list onto a Doc: one sh:NodeShape/sh:PropertyShape pair per shape,
    /// blank nodes for anonymous property shapes and path expressions.
    val toDoc: shapes: ShapeDecl list -> Doc
