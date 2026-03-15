namespace Frank.Validation

open System
open System.Collections.Concurrent
open System.Collections.Generic
open System.Reflection
open Microsoft.FSharp.Reflection

/// Functions for deriving SHACL NodeShapes from F# record types and discriminated unions
/// via .NET reflection.
module ShapeDerivation =

    /// UUID regex pattern for Guid fields (RFC 4122).
    [<Literal>]
    let private UuidPattern =
        "^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$"

    /// Default maximum derivation depth for recursive types.
    [<Literal>]
    let DefaultMaxDepth = 5

    /// Thread-safe cache of derived shapes, keyed by System.Type.
    /// Cache assumes consistent maxDepth configuration per application lifetime.
    /// Different maxDepth values for the same type will return the first-cached result.
    let private shapeCache = ConcurrentDictionary<Type, ShaclShape>()

    /// Clear the shape cache. Useful for testing.
    let clearCache () = shapeCache.Clear()

    // ──────────────────────────────────────────────
    // Type key helper (since System.Type doesn't support F# comparison)
    // ──────────────────────────────────────────────

    /// Get a stable string key for a type, used for the derivation stack.
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
    // URI construction helpers
    // ──────────────────────────────────────────────

    /// Build a NodeShape URI for a type.
    /// Pattern: urn:frank:shape:{assembly-name}:{type-full-name}
    /// Generic parameters are expanded (e.g., PagedResult_MyApp.Customer).
    let buildNodeShapeUri (typ: Type) =
        let assemblyName =
            if typ.Assembly <> null && typ.Assembly.GetName() <> null then
                typ.Assembly.GetName().Name
            else
                "Unknown"

        let typeName =
            if typ.IsGenericType && not (isOptionType typ) && not (isCollectionType typ) then
                let baseName =
                    let idx = typ.Name.IndexOf('`')
                    if idx >= 0 then typ.Name.Substring(0, idx) else typ.Name

                let argNames =
                    typ.GetGenericArguments()
                    |> Array.map (fun t ->
                        if t.FullName <> null then t.FullName
                        elif t.Name <> null then t.Name
                        else "Unknown")
                    |> String.concat ","

                sprintf "%s_%s" baseName argNames
            else if typ.FullName <> null then
                typ.FullName
            elif typ.Name <> null then
                typ.Name
            else
                "Unknown"

        let encoded = Uri.EscapeDataString(typeName)
        Uri(sprintf "urn:frank:shape:%s:%s" assemblyName encoded)

    /// Build a property path URI for a field name.
    let buildPropertyPathUri (fieldName: string) =
        sprintf "urn:frank:property:%s" fieldName

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

        // Step 1: Check for option type - unwrap and set minCount=0
        let isOption, unwrappedType =
            match unwrapOptionType fieldType with
            | Some inner -> true, inner
            | None -> false, fieldType

        // Step 2: Check for collection type
        let isCollection, elementType =
            if isCollectionType unwrappedType then
                true, getCollectionElementType unwrappedType
            else
                false, unwrappedType

        let minCount = if isOption then 0 else 1
        let maxCount = if isCollection then None else Some 1

        // Step 3: Try XSD datatype mapping on the resolved element type
        let xsdDatatype = TypeMapping.mapType elementType

        // Step 4: Determine if Guid (needs pattern constraint)
        let pattern =
            if elementType = typeof<Guid> then
                Some UuidPattern
            else
                None

        // Step 5: Handle node references for derivable complex types
        let nodeRef, inValues, orShapes =
            match xsdDatatype with
            | Some _ ->
                // Primitive type: no node reference needed
                None, None, None
            | None ->
                if not (isDerivableType elementType) then
                    // Excluded framework/infrastructure type: treat as opaque, skip derivation
                    None, None, None
                elif FSharpType.IsUnion(elementType, true) then
                    // DU type: derive constraints
                    let duResult = deriveDuConstraint maxDepth stack elementType

                    match duResult with
                    | InValues values -> None, Some values, None
                    | OrShapes uris -> None, None, Some uris
                elif FSharpType.IsRecord(elementType, true) then
                    // Nested record: derive shape and reference it
                    let nestedShape = deriveShape maxDepth stack elementType
                    Some nestedShape.NodeShapeUri, None, None
                else
                    // Unknown/non-derivable type: treat as no constraints
                    None, None, None

        // For simple DU fields, set the datatype to XsdString for the sh:in values
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
            // Simple DU: sh:in with case names
            InValues(cases |> Array.map (fun c -> c.Name) |> Array.toList)
        else
            // Payload DU: sh:or with per-case NodeShapes
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
            if parentDuType.FullName <> null then
                parentDuType.FullName
            else
                parentDuType.Name

        let caseUri =
            let encoded = Uri.EscapeDataString(sprintf "%s.%s" parentName caseInfo.Name)
            Uri(sprintf "urn:frank:shape:%s:%s" assemblyName encoded)

        let properties =
            caseFields |> Array.map (deriveProperty maxDepth stack) |> Array.toList

        { TargetType = parentDuType
          NodeShapeUri = caseUri
          Properties = properties
          Closed = true
          Description = Some(sprintf "DU case: %s" caseInfo.Name) }

    /// Derive a ShaclShape from an F# type. Handles records, DUs, option types,
    /// collections, nested types, and recursive types (with cycle detection).
    and deriveShape (maxDepth: int) (stack: Set<string>) (typ: Type) : ShaclShape =
        let key = typeKey typ

        // Check cache first
        match shapeCache.TryGetValue(typ) with
        | true, cached -> cached
        | _ ->

            // Cycle detection: if this type is already being derived, emit reference-only shape
            if stack.Contains key then
                { TargetType = typ
                  NodeShapeUri = buildNodeShapeUri typ
                  Properties = []
                  Closed = false
                  Description = Some "Recursive reference (cycle detected)" }
            // Depth limit: safety net for deeply nested types
            elif stack.Count >= maxDepth then
                { TargetType = typ
                  NodeShapeUri = buildNodeShapeUri typ
                  Properties = []
                  Closed = false
                  Description = Some(sprintf "Depth limit reached (%d)" maxDepth) }
            else
                let stack' = stack |> Set.add key
                let uri = buildNodeShapeUri typ

                let shape =
                    if FSharpType.IsRecord(typ, true) then
                        let fields = FSharpType.GetRecordFields(typ, true)

                        let properties =
                            fields |> Array.map (deriveProperty maxDepth stack') |> Array.toList

                        { TargetType = typ
                          NodeShapeUri = uri
                          Properties = properties
                          Closed = true
                          Description = None }
                    elif FSharpType.IsUnion(typ, true) then
                        let duResult = deriveDuConstraint maxDepth stack' typ

                        match duResult with
                        | InValues _values ->
                            // Simple DU: shape representing the enum
                            { TargetType = typ
                              NodeShapeUri = uri
                              Properties = []
                              Closed = true
                              Description = Some(sprintf "Enum DU: %s" typ.Name) }
                        | OrShapes _uris ->
                            // Payload DU: OrShapes URIs from deriveDuConstraint are intentionally
                            // not stored here — ShaclShape has no field for them. Payload DUs are
                            // expected as field types where PropertyShape.OrShapes carries the data.
                            // Top-level DU shapes are valid but won't reference case shapes directly.
                            { TargetType = typ
                              NodeShapeUri = uri
                              Properties = []
                              Closed = false
                              Description = Some(sprintf "Payload DU: %s" typ.Name) }
                    else
                        // Non-derivable type: minimal shape
                        { TargetType = typ
                          NodeShapeUri = uri
                          Properties = []
                          Closed = false
                          Description = Some "Non-derivable type" }

                // Cache and return
                shapeCache.TryAdd(typ, shape) |> ignore
                shape

    /// Derive a ShaclShape with the default max depth.
    let deriveShapeDefault (typ: Type) : ShaclShape =
        deriveShape DefaultMaxDepth Set.empty typ
