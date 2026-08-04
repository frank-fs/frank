namespace Frank.Validation

open System
open Frank.Rdf
open Frank.Validation.ShapeSpecFunctions

[<AutoOpen>]
module ShapeBuilderModule =
    [<Sealed>]
    type PropertyShapeBuilder(initial: PropertyShapeSpec) =
        member _.Yield(_) : PropertyShapeSpec = initial
        member _.Zero() : PropertyShapeSpec = initial
        member _.Run(p: PropertyShapeSpec) : PropertyShapeSpec = p

        [<CustomOperation("datatype")>]
        member _.Datatype(p, dt: XsdDatatype) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Datatype dt)

        [<CustomOperation("ofClass")>]
        member _.OfClass(p, c: Uri) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Class c)

        [<CustomOperation("nodeKind")>]
        member _.NodeKindOp(p, nk: NodeKind) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.NodeKind nk)

        [<CustomOperation("minCount")>]
        member _.MinCount(p, n: int) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MinCount n)

        [<CustomOperation("maxCount")>]
        member _.MaxCount(p, n: int) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MaxCount n)

        [<CustomOperation("minLength")>]
        member _.MinLength(p, n: int) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MinLength n)

        [<CustomOperation("maxLength")>]
        member _.MaxLength(p, n: int) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MaxLength n)

        [<CustomOperation("minExclusive")>]
        member _.MinExclusive(p, v: Literal) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MinExclusive v)

        [<CustomOperation("minInclusive")>]
        member _.MinInclusive(p, v: Literal) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MinInclusive v)

        [<CustomOperation("maxExclusive")>]
        member _.MaxExclusive(p, v: Literal) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MaxExclusive v)

        [<CustomOperation("maxInclusive")>]
        member _.MaxInclusive(p, v: Literal) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MaxInclusive v)

        [<CustomOperation("pattern")>]
        member _.Pattern(p, pat: string) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Pattern(pat, None))

        [<CustomOperation("patternWithFlags")>]
        member _.PatternWithFlags(p, pat: string, flags: string) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Pattern(pat, Some flags))

        [<CustomOperation("languageIn")>]
        member _.LanguageIn(p, tags: NonEmptyList<string>) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.LanguageIn tags)

        [<CustomOperation("uniqueLang")>]
        member _.UniqueLang(p, b: bool) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.UniqueLang b)

        [<CustomOperation("equalsPath")>]
        member _.EqualsPath(p, u: Uri) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Equals u)

        [<CustomOperation("disjoint")>]
        member _.Disjoint(p, u: Uri) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Disjoint u)

        [<CustomOperation("lessThan")>]
        member _.LessThan(p, u: Uri) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.LessThan u)

        [<CustomOperation("lessThanOrEquals")>]
        member _.LessThanOrEquals(p, u: Uri) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.LessThanOrEquals u)

        [<CustomOperation("node")>]
        member _.NodeOp(p, s: ShapeDecl) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Node s)

        [<CustomOperation("qualifiedValueShape")>]
        member _.QualifiedValueShape
            (p, s: ShapeDecl, minC: int option, maxC: int option, disjoint: bool)
            : PropertyShapeSpec =
            p
            |> addConstraint (PropertyConstraint.QualifiedValueShape(s, minC, maxC, disjoint))

        [<CustomOperation("hasValue")>]
        member _.HasValue(p, v: Value) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.HasValue v)

        [<CustomOperation("allowedValues")>]
        member _.AllowedValues(p, vs: NonEmptyList<Value>) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.AllowedValues vs)

        [<CustomOperation("sparqlConstraint")>]
        member _.SparqlConstraintOp(p, sc: SparqlConstraint) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Sparql sc)

        [<CustomOperation("severity")>]
        member _.SeverityOp(p, sev: Severity) : PropertyShapeSpec = { p with Severity = Some sev }

        [<CustomOperation("message")>]
        member _.MessageOp(p, msg: string) : PropertyShapeSpec = { p with Message = Some msg }

    let property (path: PropertyPath) = PropertyShapeBuilder(ofPath path)

    [<Sealed>]
    type ShapeBuilder(initial: ShapeDecl) =
        member _.Yield(_) : ShapeDecl = initial
        member _.Zero() : ShapeDecl = initial
        member _.Run(d: ShapeDecl) : ShapeDecl = d

        [<CustomOperation("properties")>]
        member _.Properties(d, props: PropertyShapeSpec list) : ShapeDecl =
            match d with
            | RecordShape n ->
                RecordShape
                    { n with
                        Properties = n.Properties @ props }
            | other -> other

        [<CustomOperation("closed")>]
        member _.Closed(d, ignoredProperties: Uri list) : ShapeDecl =
            match d with
            | RecordShape n ->
                RecordShape
                    { n with
                        Closed = true
                        IgnoredProperties = ignoredProperties }
            | other -> other

        [<CustomOperation("severity")>]
        member _.SeverityOp(d, sev: Severity) : ShapeDecl =
            match d with
            | RecordShape n -> RecordShape { n with Severity = Some sev }
            | other -> other

        [<CustomOperation("message")>]
        member _.MessageOp(d, msg: string) : ShapeDecl =
            match d with
            | RecordShape n -> RecordShape { n with Message = Some msg }
            | other -> other

    let shape (targets: TargetSpec list) = ShapeBuilder(recordShape targets [])
