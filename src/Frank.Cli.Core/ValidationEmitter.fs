module Frank.Cli.Core.ValidationEmitter

open System
open Frank.Semantic
open Frank.Semantic.LockFile

// ── XSD datatype mapping ──────────────────────────────────────────────────────

/// Map the enriched (already-unwrapped) TypeName string to an XsdDatatype.
/// Returns None for domain types — they get sh:path + cardinality but no sh:datatype.
let private xsdOf (typeName: string) : XsdDatatype option =
    match typeName with
    | "int" -> Some XsdInteger
    | "int64" -> Some XsdLong
    | "decimal" -> Some XsdDecimal
    | "float"
    | "float32"
    | "double" -> Some XsdDouble
    | "bool" -> Some XsdBoolean
    | "string" -> Some XsdString
    | "System.DateTime"
    | "DateTime" -> Some XsdDateTime
    | _ -> None

// ── PropertyShape projection ──────────────────────────────────────────────────

let private projectProperty (rf: ResolvedField) : Result<PropertyShape, string> =
    match rf.Iri with
    | None -> Error $"field '{rf.Name}' has no IRI; cannot project to PropertyShape"
    | Some path ->
        if String.IsNullOrEmpty rf.TypeName then
            Error $"field '{rf.Name}' has IRI but TypeName is empty (never enriched); fail-closed"
        else
            Ok
                { Path = path
                  Datatype = xsdOf rf.TypeName
                  MinCount = if rf.IsOptional then 0 else 1
                  MaxCount = if rf.IsCollection then None else Some 1
                  Pattern = rf.ConstraintPattern }

let private projectProperties (fields: ResolvedField list) : Result<PropertyShape list, string> =
    let withIri = fields |> List.filter (fun f -> f.Iri.IsSome)

    let folder acc f =
        match acc with
        | Error e -> Error e
        | Ok ps ->
            match projectProperty f with
            | Error e -> Error e
            | Ok p -> Ok(p :: ps)

    match List.fold folder (Ok []) withIri with
    | Error e -> Error e
    | Ok ps -> Ok(List.rev ps)

// ── ShapeDecl projection ──────────────────────────────────────────────────────

let private isAllNullary (r: ResolvedResource) : bool =
    r.Cases |> List.forall (fun c -> c.IsNullary)

let private projectResource (r: ResolvedResource) : Result<ShapeDecl option, string> =
    match r.ClassIri with
    | None -> Ok None
    | Some classIri ->
        match r.Cases with
        | _ :: _ when isAllNullary r ->
            let iris = r.Cases |> List.map (fun c -> c.Iri)

            match NonEmptyList.ofList iris with
            | None -> Ok None
            | Some nel -> Ok(Some(EnumShape(classIri, nel)))
        | _ :: _ -> Ok None
        | [] ->
            match projectProperties r.Fields with
            | Error e -> Error e
            | Ok props -> Ok(Some(RecordShape(classIri, props)))

let private traverseResult (f: 'a -> Result<'b option, 'e>) (xs: 'a list) : Result<'b list, 'e> =
    let folder acc x =
        match acc with
        | Error e -> Error e
        | Ok ys ->
            match f x with
            | Error e -> Error e
            | Ok None -> Ok ys
            | Ok(Some y) -> Ok(y :: ys)

    match List.fold folder (Ok []) xs with
    | Error e -> Error e
    | Ok ys -> Ok(List.rev ys)

/// Project an enriched ResolvedModel to a ShapeDecl list.
let internal projectShapes (model: ResolvedModel) : Result<ShapeDecl list, string> =
    traverseResult projectResource model.Resources

// ── AstRender helpers ─────────────────────────────────────────────────────────

let private datatypeExpr (d: XsdDatatype option) =
    match d with
    | None -> AstRender.rawExpr "None"
    | Some x -> AstRender.someExpr (AstRender.rawExpr (string x))

let private maxCountExpr (mc: int option) =
    match mc with
    | None -> AstRender.rawExpr "None"
    | Some n -> AstRender.someExpr (AstRender.intExpr n)

let private patternExpr (pat: string option) =
    match pat with
    | None -> AstRender.rawExpr "None"
    | Some s -> AstRender.someExpr (AstRender.strExpr s)

let private sysUriExpr (u: Uri) =
    AstRender.appExpr "System.Uri" (AstRender.strExpr u.AbsoluteUri)

let private propExpr (p: PropertyShape) =
    AstRender.recordExpr
        [ "Path", sysUriExpr p.Path
          "Datatype", datatypeExpr p.Datatype
          "MinCount", AstRender.intExpr p.MinCount
          "MaxCount", maxCountExpr p.MaxCount
          "Pattern", patternExpr p.Pattern ]

let private shapeExpr (shape: ShapeDecl) =
    match shape with
    | RecordShape(classIri, props) ->
        AstRender.tupleAppExpr "RecordShape" [ sysUriExpr classIri; AstRender.listExpr (props |> List.map propExpr) ]
    | EnumShape(classIri, nel) ->
        AstRender.tupleAppExpr
            "EnumShape"
            [ sysUriExpr classIri
              AstRender.recordExpr
                  [ "Head", sysUriExpr nel.Head
                    "Tail", AstRender.listExpr (nel.Tail |> List.map sysUriExpr) ] ]

let private renderShapes (moduleName: string) (shapes: ShapeDecl list) : string =
    let decls =
        [ AstRender.valueDecl "shapes" "ShapeDecl list" (AstRender.listExpr (shapes |> List.map shapeExpr))
          AstRender.valueDecl
              "shapesGraph"
              "VDS.RDF.Shacl.ShapesGraph"
              (AstRender.appExpr "Shapes.toShapesGraph" (AstRender.rawExpr "shapes")) ]

    AstRender.formatModule moduleName None [ "Frank.Semantic"; "Frank.Validation" ] decls

// ── Public API ────────────────────────────────────────────────────────────────

/// Emit a GeneratedValidation F# module from a vocabulary registry, lock file, and type map.
///
/// moduleName  — the F# module name to emit (e.g. "TicTacToe.GeneratedValidation")
/// registry    — the VocabularyRegistry supplying prefix→URI mappings
/// lock        — the resolved lock file
/// typesByName — FCS-extracted TypeInfo map keyed by FullName
///
/// Returns Ok with the F# source string, or Error if any shaped field has an empty TypeName.
let emit
    (moduleName: string)
    (registry: VocabularyRegistry)
    (lock: LockFile)
    (typesByName: Map<string, TypeInfo>)
    : Result<string, string> =
    if String.IsNullOrWhiteSpace moduleName then
        invalidArg (nameof moduleName) "moduleName must not be empty"

    ResolvedModel.build registry lock
    |> Result.bind (ResolvedModel.enrichTypes typesByName)
    |> Result.bind projectShapes
    |> Result.map (renderShapes moduleName)
