namespace Frank.Validation

open System

module ShapeSpecFunctions =
    let ofPath (path: PropertyPath) : PropertyShapeSpec =
        { Path = path
          Constraints = []
          Severity = None
          Message = None }

    let addConstraint (constr: PropertyConstraint) (spec: PropertyShapeSpec) : PropertyShapeSpec =
        { spec with
            Constraints = spec.Constraints @ [ constr ] }

    let recordShape (targets: TargetSpec list) (properties: PropertyShapeSpec list) : ShapeDecl =
        ShapeDecl.RecordShape
            { Targets = targets
              Properties = properties
              Closed = false
              IgnoredProperties = []
              Severity = None
              Message = None }

    let enumShape (targetClass: Uri) (head: Uri) (tail: Uri list) : ShapeDecl =
        ShapeDecl.EnumShape(targetClass, { Head = head; Tail = tail })

    let targetClass (uri: Uri) : TargetSpec list = [ TargetSpec.Class uri ]
