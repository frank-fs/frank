namespace Frank.Validation

open System
open Frank.Rdf

/// One SHACL validation-report result, typed. See ResultPath's doc comment for a disclosed
/// simplification versus a fully round-tripped PropertyPath.
type Violation =
    {
        /// The node the violation was raised against. Typed `Frank.Rdf.Value`, not `Frank.Rdf.Node`,
        /// because a SHACL focus node is NOT always an IRI or blank node: `sh:targetObjectsOf`
        /// (TargetSpec.ObjectsOf) targets the OBJECTS of a predicate, which are routinely literals.
        /// A `Node`-typed field could only represent those by fabricating a garbage IRI out of the
        /// literal's lexical form, which then failed to serialize at all.
        ///
        /// KNOWN LOSSINESS: `Frank.Rdf.Literal` has String/Int/Bool/DateTime/LangString cases only,
        /// so a focus-node literal typed xsd:decimal, xsd:double, xsd:float, xsd:short, ... comes
        /// back as `Literal.String` carrying its lexical form, losing the datatype IRI. Widening
        /// `Frank.Rdf.Literal` is the only real fix and belongs to that package, not this one.
        FocusNode: Value
        /// Some uri for a simple-predicate path; None when the violated property's path is complex
        /// (sh:alternativePath/sh:inversePath/...) -- not round-tripped back to PropertyPath in v1.
        ResultPath: Uri option
        Severity: Severity
        Message: string
        ConstraintComponent: Uri
        /// The shape that raised this violation. Node shapes now always mint a fresh blank-node
        /// subject (see Shacl.fs's RecordShape/EnumShape emission -- a shape's subject is no longer
        /// its sh:targetClass IRI, so that two independently-authored shapes over the same class don't
        /// collide onto one subject). This means SourceShape is an opaque blank node identifier with
        /// no direct route back to the authored ShapeDecl or a human-readable name -- it identifies
        /// *that* a shape fired, not *which* shape in your source code it was.
        SourceShape: Node
    }

[<RequireQualifiedAccess>]
type ValidationOutcome =
    | Conforms
    | Violates of Violation list
