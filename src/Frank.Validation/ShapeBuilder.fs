namespace Frank.Validation

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.Reflection

/// Functions for deriving SHACL NodeShapes from F# record types and discriminated unions
/// via .NET reflection. URI construction delegates to UriConventions.
module ShapeBuilder =

    /// UUID regex pattern for Guid fields (RFC 4122).
    [<Literal>]
    let private UuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"

    /// Default maximum derivation depth for recursive types.
    [<Literal>]
    let DefaultMaxDepth = 5

    /// Thread-safe cache of derived shapes, keyed by System.Type.
    let private shapeCache = ConcurrentDictionary<Type, ShaclShape>()

    /// Clear the shape cache. Useful for testing.
    let clearCache () = shapeCache.Clear()

    // ──────────────────────────────────────────────
    // Type key helper
    // ──────────────────────────────────────────────

    let private typeKey (t: Type) : string =
        if t.FullName <> null then
            t.FullName
        elif t.Name <> null then
            sprintf "%s_%d" t.Name (t.GetHashCode())
        else
            sprintf "unknown_%d" (t.GetHashCode())

    // ──────────────────────────────────────────────
    // Type classification helpers
    // ──────────────────────────────────────────────

    /// Check if a type is option<T>.
    let isOptionType (t: Type) =
        t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

    /// Unwrap option<T> to the inner type T. Returns None if not an option type.
    let unwrapOptionType (t: Type) =
        if isOptionType t then
            Some(t.GetGenericArguments().[0])
        else
            None

    /// Check if a type is a collection (list, array, seq, ResizeArray) but not string.
    let isCollectionType (t: Type) =
        if t = typeof<string> then
            false
        elif t.IsArray then
            true
        elif t.IsGenericType then
            let gtd = t.GetGenericTypeDefinition()

            gtd = typedefof<list<_>>
            || gtd = typedefof<seq<_>>
            || gtd = typedefof<ResizeArray<_>>
            || gtd = typedefof<IEnumerable<_>>
        else
            false

    /// Get the element type of a collection type.
    let getCollectionElementType (t: Type) =
        if t.IsArray then t.GetElementType()
        elif t.IsGenericType then t.GetGenericArguments().[0]
        else t

    /// Framework and infrastructure types that should NOT have shapes derived.
    let private excludedTypeNames =
        set
            [ "Microsoft.AspNetCore.Http.HttpContext"
              "Microsoft.AspNetCore.Http.HttpRequest"
              "Microsoft.AspNetCore.Http.HttpResponse"
              "System.Threading.CancellationToken"
              "System.IO.Stream"
              "System.IO.Pipelines.PipeReader" ]

    /// Check if a type is a derivable domain type (F# record or DU),
    /// excluding framework/infrastructure types.
    let isDerivableType (typ: Type) =
        if typ = null then
            false
        elif typ.FullName <> null && excludedTypeNames.Contains(typ.FullName) then
            false
        elif FSharpType.IsRecord(typ, true) then
            true
        elif FSharpType.IsUnion(typ, true) then
            true
        else
            false

    // ──────────────────────────────────────────────
    // Type-to-XSD mapping (private; only needed during derivation)
    // ──────────────────────────────────────────────

    /// Map an F# CLR type to its XSD datatype. Returns None for types
    /// that require sh:node references (records, collections).
    let private mapType (typ: Type) : XsdDatatype option =
        match typ with
        | t when t = typeof<string> -> Some XsdString
        | t when t = typeof<int> || t = typeof<int32> -> Some XsdInteger
        | t when t = typeof<int64> -> Some XsdLong
        | t when t = typeof<float> || t = typeof<double> -> Some XsdDouble
        | t when t = typeof<decimal> -> Some XsdDecimal
        | t when t = typeof<bool> -> Some XsdBoolean
        | t when t = typeof<DateTimeOffset> -> Some XsdDateTimeStamp
        | t when t = typeof<DateTime> -> Some XsdDateTime
        | t when t = typeof<DateOnly> -> Some XsdDate
        | t when t = typeof<TimeOnly> -> Some XsdTime
        | t when t = typeof<TimeSpan> -> Some XsdDuration
        | t when t = typeof<Uri> -> Some XsdAnyUri
        | t when t = typeof<byte[]> -> Some XsdBase64Binary
        | t when t = typeof<Guid> -> Some XsdString // + pattern constraint added by derivation
        | _ -> None

    // ──────────────────────────────────────────────
    // Shape derivation
    // ──────────────────────────────────────────────

    /// Discriminated union to represent the result of DU constraint analysis.
    [<Struct>]
    type DuConstraintResult =
        | InValues of values: string list
        | OrShapes of shapes: Uri list

    /// Derive a PropertyShape from a single record field.
    let rec deriveProperty (maxDepth: int) (stack: Set<string>) (field: PropertyInfo) : PropertyShape =
        let fieldType = field.PropertyType

        let isOption, unwrappedType =
            match unwrapOptionType fieldType with
            | Some inner -> true, inner
            | None -> false, fieldType

        let isCollection, elementType =
            if isCollectionType unwrappedType then
                true, getCollectionElementType unwrappedType
            else
                false, unwrappedType

        let minCount = if isOption then 0 else 1
        let maxCount = if isCollection then None else Some 1

        let xsdDatatype = mapType elementType

        let pattern =
            if elementType = typeof<Guid> then Some UuidPattern
            else None

        let nodeRef, inValues, orShapes =
            match xsdDatatype with
            | Some _ -> None, None, None
            | None ->
                if not (isDerivableType elementType) then
                    None, None, None
                elif FSharpType.IsUnion(elementType, true) then
                    let duResult = deriveDuConstraint maxDepth stack elementType

                    match duResult with
                    | InValues values -> None, Some values, None
                    | OrShapes uris -> None, None, Some uris
                elif FSharpType.IsRecord(elementType, true) then
                    let nestedShape = deriveShape maxDepth stack elementType
                    Some nestedShape.NodeShapeUri, None, None
                else
                    None, None, None

        let finalDatatype =
            match inValues with
            | Some _ -> Some XsdString
            | None -> xsdDatatype

        { Path = field.Name
          Datatype = finalDatatype
          MinCount = minCount
          MaxCount = maxCount
          NodeReference = nodeRef
          InValues = inValues
          OrShapes = orShapes
          Pattern = pattern
          MinInclusive = None
          MaxInclusive = None
          Description = None }

    /// Derive DU constraints: sh:in for simple DUs, sh:or for payload DUs.
    and deriveDuConstraint (maxDepth: int) (stack: Set<string>) (duType: Type) : DuConstraintResult =
        let cases = FSharpType.GetUnionCases(duType, true)
        let allSimple = cases |> Array.forall (fun c -> c.GetFields().Length = 0)

        if allSimple then
            InValues(cases |> Array.map (fun c -> c.Name) |> Array.toList)
        else
            let caseUris =
                cases
                |> Array.map (fun c ->
                    let caseShape = deriveCaseShape maxDepth stack duType c
                    caseShape.NodeShapeUri)
                |> Array.toList

            OrShapes caseUris

    /// Derive a NodeShape for a single DU case (for payload DUs).
    and deriveCaseShape
        (maxDepth: int)
        (stack: Set<string>)
        (parentDuType: Type)
        (caseInfo: UnionCaseInfo)
        : ShaclShape =
        let caseFields = caseInfo.GetFields()

        let assemblyName =
            if parentDuType.Assembly <> null && parentDuType.Assembly.GetName() <> null then
                parentDuType.Assembly.GetName().Name
            else
                "Unknown"

        let parentName =
            if parentDuType.FullName <> null then parentDuType.FullName
            else parentDuType.Name

        let caseUri =
            let encoded = Uri.EscapeDataString(sprintf "%s.%s" parentName caseInfo.Name)
            Uri(sprintf "urn:frank:shape:%s:%s" assemblyName encoded)

        let properties =
            caseFields |> Array.map (deriveProperty maxDepth stack) |> Array.toList

        { TargetType = Some parentDuType
          NodeShapeUri = caseUri
          Properties = properties
          Closed = true
          Description = Some(sprintf "DU case: %s" caseInfo.Name) }

    /// Derive a ShaclShape from an F# type. Handles records, DUs, option types,
    /// collections, nested types, and recursive types (with cycle detection).
    and deriveShape (maxDepth: int) (stack: Set<string>) (typ: Type) : ShaclShape =
        let key = typeKey typ

        match shapeCache.TryGetValue(typ) with
        | true, cached -> cached
        | _ ->

            if stack.Contains key then
                { TargetType = Some typ
                  NodeShapeUri = UriConventions.buildNodeShapeUriFromType typ
                  Properties = []
                  Closed = false
                  Description = Some "Recursive reference (cycle detected)" }
            elif stack.Count >= maxDepth then
                { TargetType = Some typ
                  NodeShapeUri = UriConventions.buildNodeShapeUriFromType typ
                  Properties = []
                  Closed = false
                  Description = Some(sprintf "Depth limit reached (%d)" maxDepth) }
            else
                let stack' = stack |> Set.add key
                let uri = UriConventions.buildNodeShapeUriFromType typ

                let shape =
                    if FSharpType.IsRecord(typ, true) then
                        let fields = FSharpType.GetRecordFields(typ, true)

                        let properties =
                            fields |> Array.map (deriveProperty maxDepth stack') |> Array.toList

                        { TargetType = Some typ
                          NodeShapeUri = uri
                          Properties = properties
                          Closed = true
                          Description = None }
                    elif FSharpType.IsUnion(typ, true) then
                        let duResult = deriveDuConstraint maxDepth stack' typ

                        match duResult with
                        | InValues _values ->
                            { TargetType = Some typ
                              NodeShapeUri = uri
                              Properties = []
                              Closed = true
                              Description = Some(sprintf "Enum DU: %s" typ.Name) }
                        | OrShapes _uris ->
                            { TargetType = Some typ
                              NodeShapeUri = uri
                              Properties = []
                              Closed = false
                              Description = Some(sprintf "Payload DU: %s" typ.Name) }
                    else
                        { TargetType = Some typ
                          NodeShapeUri = uri
                          Properties = []
                          Closed = false
                          Description = Some "Non-derivable type" }

                shapeCache.TryAdd(typ, shape) |> ignore
                shape

    /// Derive a ShaclShape with the default max depth.
    let deriveShapeDefault (typ: Type) : ShaclShape =
        deriveShape DefaultMaxDepth Set.empty typ
