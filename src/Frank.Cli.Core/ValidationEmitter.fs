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

// ── IRI ownership check ───────────────────────────────────────────────────────

/// Returns true if the given absolute IRI starts with the namespace URI
/// of any prefix that is in the `Using` set (i.e., is an external vocabulary).
let private isExternalIri (using: Set<string>) (prefixes: Map<string, Uri>) (iri: Uri) : bool =
    using
    |> Set.exists (fun prefix ->
        match Map.tryFind prefix prefixes with
        | None -> false
        | Some ns -> iri.AbsoluteUri.StartsWith(ns.AbsoluteUri, StringComparison.Ordinal))

// ── PropertyShape projection ──────────────────────────────────────────────────

let private projectProperty (path: Uri) (rf: ResolvedField) : Result<PropertyShape, string> =
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
    let withIri =
        fields |> List.choose (fun f -> f.Iri |> Option.map (fun iri -> iri, f))

    let folder acc (path, rf) =
        match acc with
        | Error e -> Error e
        | Ok ps ->
            match projectProperty path rf with
            | Error e -> Error e
            | Ok p -> Ok(p :: ps)

    match List.fold folder (Ok []) withIri with
    | Error e -> Error e
    | Ok ps -> Ok(List.rev ps)

// ── ShapeDecl projection ──────────────────────────────────────────────────────

let private isAllNullary (r: ResolvedResource) : bool =
    r.Cases |> List.forall (fun c -> c.IsNullary)

let private projectClassShape
    (using: Set<string>)
    (prefixes: Map<string, Uri>)
    (classIri: Uri)
    (r: ResolvedResource)
    : Result<ShapeDecl option, string> =
    match r.Cases with
    | _ :: _ when isAllNullary r ->
        let iris = r.Cases |> List.map (fun c -> c.Iri)

        let nel =
            { Head = List.head iris
              Tail = List.tail iris }

        Ok(Some(EnumShape(classIri, nel)))
    | _ :: _ -> Ok None
    | [] ->
        let externalFields =
            r.Fields
            |> List.filter (fun f ->
                match f.Iri with
                | None -> false
                | Some iri -> isExternalIri using prefixes iri)

        match projectProperties externalFields with
        | Error e -> Error e
        | Ok props -> Ok(Some(RecordShape(classIri, props)))

let private projectResource
    (using: Set<string>)
    (prefixes: Map<string, Uri>)
    (r: ResolvedResource)
    : Result<ShapeDecl option, string> =
    match r.ClassIri with
    | None -> Ok None
    | Some classIri -> projectClassShape using prefixes classIri r

let private projectHostRelativeProps
    (using: Set<string>)
    (prefixes: Map<string, Uri>)
    (r: ResolvedResource)
    : (Uri * string * string option) list =
    match r.ClassIri with
    | None -> []
    | Some classIri ->
        r.Fields
        |> List.choose (fun f ->
            match f.Iri with
            | None -> None
            | Some iri when not (isExternalIri using prefixes iri) ->
                let relPath = iri.AbsolutePath + iri.Fragment
                Some(classIri, relPath, f.ConstraintPattern)
            | _ -> None)

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

/// Project an enriched ResolvedModel to static ShapeDecl list (external-vocab fields only)
/// and host-relative property tuples (app-owned fields).
let internal projectShapes
    (model: ResolvedModel)
    : Result<ShapeDecl list * (Uri * string * string option) list, string> =
    let using = model.Using
    let prefixes = model.Prefixes

    match traverseResult (projectResource using prefixes) model.Resources with
    | Error e -> Error e
    | Ok shapes ->
        let hrProps =
            model.Resources |> List.collect (projectHostRelativeProps using prefixes)

        Ok(shapes, hrProps)

// ── AstRender helpers ─────────────────────────────────────────────────────────

let private datatypeExpr (d: XsdDatatype option) =
    AstRender.optionExpr (fun x -> AstRender.rawExpr (string x)) d

let private maxCountExpr (mc: int option) =
    AstRender.optionExpr AstRender.intExpr mc

let private patternExpr (pat: string option) =
    AstRender.optionExpr AstRender.strExpr pat

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

let private hostRelPropExpr (classUri: Uri) (relPath: string) (pattern: string option) =
    AstRender.parenExpr (AstRender.tupleExpr [ sysUriExpr classUri; AstRender.strExpr relPath; patternExpr pattern ])

let private renderShapes
    (moduleName: string)
    (knownNamespaces: string list)
    (shapes: ShapeDecl list)
    (hrProps: (Uri * string * string option) list)
    : string =
    let hrPropExprs =
        hrProps |> List.map (fun (cls, rel, pat) -> hostRelPropExpr cls rel pat)

    let decls =
        [ AstRender.valueDecl "shapes" "ShapeDecl list" (AstRender.listExpr (shapes |> List.map shapeExpr))
          AstRender.valueDecl
              "shapesGraph"
              "VDS.RDF.Shacl.ShapesGraph"
              (AstRender.appExpr "Shapes.toShapesGraph" (AstRender.rawExpr "shapes"))
          AstRender.valueDecl
              "knownNamespaces"
              "string[]"
              (AstRender.arrayExpr (knownNamespaces |> List.map AstRender.strExpr))
          AstRender.valueDecl
              "hostRelativeProperties"
              "(System.Uri * string * string option) list"
              (AstRender.listExpr hrPropExprs) ]

    AstRender.formatModule
        moduleName
        (Some AstRender.autoGeneratedHeader)
        [ "Frank.Semantic"; "Frank.Validation" ]
        decls

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
    let knownNamespaces = EmitterShared.computeKnownNamespaces registry

    AstRender.validateModuleName moduleName
    |> Result.bind (fun () -> ResolvedModel.build registry lock)
    |> Result.bind (ResolvedModel.enrichTypes typesByName)
    |> Result.bind projectShapes
    |> Result.map (fun (shapes, hrProps) -> renderShapes moduleName knownNamespaces shapes hrProps)
