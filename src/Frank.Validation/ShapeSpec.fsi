namespace Frank.Validation

open System

/// Plain curried functions -- the real authoring model. Kept to the ones that construct a genuinely
/// new value or combine data non-trivially; simple field mutation doesn't get a named counterpart
/// here (see ShapeBuilder.fsi for why -- it's inlined directly in the CE instead).
module ShapeSpecFunctions =
    val ofPath: path: PropertyPath -> PropertyShapeSpec

    /// The one general-purpose accumulator every per-constraint CE operation is sugar over. Because
    /// PropertyConstraint is already a closed, named DU, this IS the plain-function API for adding a
    /// constraint -- `p |> addConstraint (PropertyConstraint.Datatype XsdDatatype.Integer)`.
    val addConstraint: constr: PropertyConstraint -> spec: PropertyShapeSpec -> PropertyShapeSpec

    val recordShape: targets: TargetSpec list -> properties: PropertyShapeSpec list -> ShapeDecl

    val enumShape: targetClass: Uri -> head: Uri -> tail: Uri list -> ShapeDecl

    /// Convenience for the common single-class-target case.
    val targetClass: uri: Uri -> TargetSpec list
