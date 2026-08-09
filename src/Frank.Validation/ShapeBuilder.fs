namespace Frank.Validation

open System
open Frank.Rdf
open Frank.Validation.ShapeSpecFunctions

[<AutoOpen>]
module ShapeBuilderModule =
    [<Sealed>]
    type PropertyShapeBuilder(initial: PropertyShapeSpec) =
        member inline _.Yield(_) : PropertyShapeSpec = initial
        member inline _.Zero() : PropertyShapeSpec = initial
        member inline _.Run(p: PropertyShapeSpec) : PropertyShapeSpec = p

        [<CustomOperation("datatype")>]
        member inline _.Datatype(p, dt: XsdDatatype) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Datatype dt)

        [<CustomOperation("ofClass")>]
        member inline _.OfClass(p, c: Uri) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Class c)

        [<CustomOperation("nodeKind")>]
        member inline _.NodeKindOp(p, nk: NodeKind) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.NodeKind nk)

        [<CustomOperation("minCount")>]
        member inline _.MinCount(p, n: int) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MinCount n)

        [<CustomOperation("maxCount")>]
        member inline _.MaxCount(p, n: int) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MaxCount n)

        [<CustomOperation("minLength")>]
        member inline _.MinLength(p, n: int) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MinLength n)

        [<CustomOperation("maxLength")>]
        member inline _.MaxLength(p, n: int) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MaxLength n)

        [<CustomOperation("minExclusive")>]
        member inline _.MinExclusive(p, v: Literal) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MinExclusive v)

        [<CustomOperation("minInclusive")>]
        member inline _.MinInclusive(p, v: Literal) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MinInclusive v)

        [<CustomOperation("maxExclusive")>]
        member inline _.MaxExclusive(p, v: Literal) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MaxExclusive v)

        [<CustomOperation("maxInclusive")>]
        member inline _.MaxInclusive(p, v: Literal) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.MaxInclusive v)

        [<CustomOperation("pattern")>]
        member inline _.Pattern(p, pat: string) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Pattern(pat, None))

        [<CustomOperation("patternWithFlags")>]
        member inline _.PatternWithFlags(p, pat: string, flags: string) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Pattern(pat, Some flags))

        [<CustomOperation("languageIn")>]
        member inline _.LanguageIn(p, tags: NonEmptyList<string>) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.LanguageIn tags)

        [<CustomOperation("uniqueLang")>]
        member inline _.UniqueLang(p, b: bool) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.UniqueLang b)

        [<CustomOperation("equalsPath")>]
        member inline _.EqualsPath(p, u: Uri) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Equals u)

        [<CustomOperation("disjoint")>]
        member inline _.Disjoint(p, u: Uri) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Disjoint u)

        [<CustomOperation("lessThan")>]
        member inline _.LessThan(p, u: Uri) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.LessThan u)

        [<CustomOperation("lessThanOrEquals")>]
        member inline _.LessThanOrEquals(p, u: Uri) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.LessThanOrEquals u)

        [<CustomOperation("node")>]
        member inline _.NodeOp(p, s: ShapeDecl) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Node s)

        [<CustomOperation("qualifiedValueShape")>]
        member inline _.QualifiedValueShape
            (p, s: ShapeDecl, minC: int option, maxC: int option, disjoint: bool)
            : PropertyShapeSpec =
            p
            |> addConstraint (PropertyConstraint.QualifiedValueShape(s, minC, maxC, disjoint))

        [<CustomOperation("hasValue")>]
        member inline _.HasValue(p, v: Value) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.HasValue v)

        [<CustomOperation("allowedValues")>]
        member inline _.AllowedValues(p, vs: NonEmptyList<Value>) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.AllowedValues vs)

        [<CustomOperation("sparqlConstraint")>]
        member inline _.SparqlConstraintOp(p, sc: SparqlConstraint) : PropertyShapeSpec =
            p |> addConstraint (PropertyConstraint.Sparql sc)

        [<CustomOperation("severity")>]
        member inline _.SeverityOp(p, sev: Severity) : PropertyShapeSpec = { p with Severity = Some sev }

        [<CustomOperation("message")>]
        member inline _.MessageOp(p, msg: string) : PropertyShapeSpec = { p with Message = Some msg }

    let property (path: PropertyPath) = PropertyShapeBuilder(ofPath path)

    [<Sealed>]
    type ShapeBuilder(initial: ShapeDecl) =
        member inline _.Yield(_) : ShapeDecl = initial
        member inline _.Zero() : ShapeDecl = initial
        member inline _.Run(d: ShapeDecl) : ShapeDecl = d

        [<CustomOperation("properties")>]
        member inline _.Properties(d, props: PropertyShapeSpec list) : ShapeDecl =
            match d with
            | RecordShape n ->
                RecordShape
                    { n with
                        Properties = n.Properties @ props }
            | other -> other

        [<CustomOperation("closed")>]
        member inline _.Closed(d, ignoredProperties: Uri list) : ShapeDecl =
            match d with
            | RecordShape n ->
                RecordShape
                    { n with
                        Closed = true
                        IgnoredProperties = ignoredProperties }
            | other -> other

        [<CustomOperation("severity")>]
        member inline _.SeverityOp(d, sev: Severity) : ShapeDecl =
            match d with
            | RecordShape n -> RecordShape { n with Severity = Some sev }
            | other -> other

        [<CustomOperation("message")>]
        member inline _.MessageOp(d, msg: string) : ShapeDecl =
            match d with
            | RecordShape n -> RecordShape { n with Message = Some msg }
            | other -> other

    let shape (targets: TargetSpec list) = ShapeBuilder(recordShape targets [])
