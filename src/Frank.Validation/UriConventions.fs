namespace Frank.Validation

open System

/// Pure URI construction helpers for SHACL shapes and property paths.
/// Extracted from ShapeDerivation so that ShapeLoader and ShapeGraphBuilder
/// can use them without pulling in the reflection-heavy derivation logic.
module UriConventions =

    /// Build a NodeShape URI from an assembly name and a type full name.
    /// Pattern: urn:frank:shape:{assembly}:{encoded-type}
    let buildNodeShapeUri (assemblyName: string) (typeFullName: string) : Uri =
        let encoded = Uri.EscapeDataString(typeFullName)
        Uri(sprintf "urn:frank:shape:%s:%s" assemblyName encoded)

    /// Build a NodeShape URI directly from a System.Type.
    /// Generic parameters are expanded (e.g., PagedResult_MyApp.Customer).
    /// Option<T> and collection types are treated as non-generic for URI purposes.
    let buildNodeShapeUriFromType (typ: Type) : Uri =
        let assemblyName =
            if typ.Assembly <> null && typ.Assembly.GetName() <> null then
                typ.Assembly.GetName().Name
            else
                "Unknown"

        let isOptionType (t: Type) =
            t.IsGenericType && t.GetGenericTypeDefinition() = typedefof<option<_>>

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
                || gtd = typedefof<System.Collections.Generic.IEnumerable<_>>
            else
                false

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
            elif typ.FullName <> null then
                typ.FullName
            elif typ.Name <> null then
                typ.Name
            else
                "Unknown"

        buildNodeShapeUri assemblyName typeName

    /// Build a property path URI for a field name.
    /// Pattern: urn:frank:property:{fieldName}
    let buildPropertyPathUri (fieldName: string) : string =
        sprintf "urn:frank:property:%s" fieldName
