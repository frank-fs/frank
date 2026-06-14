namespace Frank.Semantic

open System
open System.Text.Json

/// Authored semantic alignments declared via the vocabulary CE.
/// Type-keyed maps use FullName for F# Map key compatibility.
type VocabularyRegistry =
    { Prefixes: Map<string, Uri>
      Using: Set<string>
      EquivalentClasses: Map<string, Uri>
      SeeAlso: Map<string, Uri list>
      FieldSeeAlso: Map<string * string, Uri list>
      ProvClasses: Map<string, ProvOClass>
      ConstraintPatterns: Map<string * string, string> }

module VocabularyRegistry =

    let empty: VocabularyRegistry =
        { Prefixes = Map.empty
          Using = Set.empty
          EquivalentClasses = Map.empty
          SeeAlso = Map.empty
          FieldSeeAlso = Map.empty
          ProvClasses = Map.empty
          ConstraintPatterns = Map.empty }

    let private typeKey (t: Type) =
        match t.FullName with
        | null -> t.AssemblyQualifiedName
        | k -> k

    let private fieldKey (t: Type) (fieldName: string) = (typeKey t, fieldName)

    /// Resolve a prefixed IRI string (e.g. "schema:Order") to a Uri using known prefixes.
    /// Raises InvalidOperationException if the prefix is not declared.
    let resolveIri (prefixes: Map<string, Uri>) (iri: string) : Uri =
        match iri.IndexOf(':') with
        | -1 -> Uri(iri)
        | idx ->
            let prefix = iri.[.. idx - 1]
            let local = iri.[idx + 1 ..]

            match Map.tryFind prefix prefixes with
            | None ->
                invalidOp $"Unknown prefix '{prefix}' in IRI '{iri}'. Declare it with: prefix \"{prefix}\" \"<uri>\""
            | Some baseUri -> Uri(baseUri.AbsoluteUri + local)

    /// Merge two maps, raising on conflicting values; identical-value duplicates absorbed.
    let private mergeMap<'K, 'V when 'K: comparison and 'V: equality>
        (field: string)
        (a: Map<'K, 'V>)
        (b: Map<'K, 'V>)
        : Map<'K, 'V> =
        Map.fold
            (fun acc key value ->
                match Map.tryFind key acc with
                | None -> Map.add key value acc
                | Some existing when existing = value -> acc
                | Some _ ->
                    invalidOp
                        $"Conflicting values for key in '{field}' during include. Use identical values to absorb duplicates.")
            a
            b

    /// Merge two prefix maps, raising on duplicate key with different value.
    let private mergePrefixes (a: Map<string, Uri>) (b: Map<string, Uri>) : Map<string, Uri> =
        Map.fold
            (fun acc key value ->
                match Map.tryFind key acc with
                | None -> Map.add key value acc
                | Some existing when existing = value -> acc
                | Some _ -> invalidOp $"Conflicting prefix '{key}' during include. Each prefix must be declared once.")
            a
            b

    /// Merge two Using sets, raising on duplicate entry.
    let private mergeUsing (a: Set<string>) (b: Set<string>) : Set<string> =
        Set.fold
            (fun acc entry ->
                if Set.contains entry acc then
                    invalidOp $"Conflicting 'using' entry '{entry}' during include. Each prefix must be used once."
                else
                    Set.add entry acc)
            a
            b

    /// Deep-union two registries. Raises on conflicting keys per field semantics.
    let include' (base': VocabularyRegistry) (other: VocabularyRegistry) : VocabularyRegistry =
        { Prefixes = mergePrefixes base'.Prefixes other.Prefixes
          Using = mergeUsing base'.Using other.Using
          EquivalentClasses = mergeMap "EquivalentClasses" base'.EquivalentClasses other.EquivalentClasses
          SeeAlso = mergeMap "SeeAlso" base'.SeeAlso other.SeeAlso
          FieldSeeAlso = mergeMap "FieldSeeAlso" base'.FieldSeeAlso other.FieldSeeAlso
          ProvClasses = mergeMap "ProvClasses" base'.ProvClasses other.ProvClasses
          ConstraintPatterns = mergeMap "ConstraintPatterns" base'.ConstraintPatterns other.ConstraintPatterns }

    /// Look up EquivalentClass by Type.
    let tryFindEquivalentClass (t: Type) (r: VocabularyRegistry) =
        Map.tryFind (typeKey t) r.EquivalentClasses

    /// Look up SeeAlso by Type.
    let tryFindSeeAlso (t: Type) (r: VocabularyRegistry) = Map.tryFind (typeKey t) r.SeeAlso

    /// Look up FieldSeeAlso by Type and field name.
    let tryFindFieldSeeAlso (t: Type) (fieldName: string) (r: VocabularyRegistry) =
        Map.tryFind (fieldKey t fieldName) r.FieldSeeAlso

    /// Look up ProvClass by Type.
    let tryFindProvClass (t: Type) (r: VocabularyRegistry) = Map.tryFind (typeKey t) r.ProvClasses

    /// Look up ConstraintPattern by Type and field name.
    let tryFindConstraintPattern (t: Type) (fieldName: string) (r: VocabularyRegistry) =
        Map.tryFind (fieldKey t fieldName) r.ConstraintPatterns

    /// Expose type key for use in builder without leaking internals.
    let internal keyOf = typeKey
    let internal fieldKeyOf = fieldKey

    // ── ALC-safe serialization ─────────────────────────────────────────────────
    // Strings cross AssemblyLoadContext boundaries; typed values do not.
    // These functions let evalRegistry serialize inside FSI's ALC and deserialize
    // in the caller's ALC, avoiding cross-ALC cast failures.

    /// Separator for tuple keys in the JSON representation. U+001F (Unit Separator)
    /// cannot appear in .NET type FullNames or field names.
    [<Literal>]
    let private TupleKeySep = ''

    let private encodeTupleKey (a: string) (b: string) = a + string TupleKeySep + b

    /// Strip the FSI session prefix from a type key produced by `typeof<T>.FullName`
    /// inside an FsiEvaluationSession. FSI compiles each fragment under "FSI_NNNN."
    /// and wraps module types as nested classes, turning '.' separators into '+'.
    /// Stripping the prefix and replacing '+' with '.' recovers the static CLR name
    /// that matches lock-file fsharpType entries.
    /// "FSI_0001.TicTacToe.Model+Move" -> "TicTacToe.Model.Move"
    /// "Frank.Semantic.Tests.VocabularyRegistryTests+Order" -> unchanged (no-op)
    let private normalizeFsiKey (key: string) : string =
        if not (key.StartsWith("FSI_", StringComparison.Ordinal)) then
            key
        else
            let dotIdx = key.IndexOf('.')
            let stripped = if dotIdx > 0 then key.[dotIdx + 1 ..] else key
            stripped.Replace('+', '.')

    let private decodeTupleKey (s: string) : Result<string * string, string> =
        let idx = s.IndexOf(TupleKeySep)

        if idx < 0 then
            Error $"tuple key missing separator U+001F in '{s}'"
        else
            Ok(s.[.. idx - 1], s.[idx + 1 ..])

    let private provOClassToString (c: ProvOClass) =
        match c with
        | Entity -> "Entity"
        | Activity -> "Activity"
        | Agent -> "Agent"

    let private provOClassFromString (s: string) : Result<ProvOClass, string> =
        match s with
        | "Entity" -> Ok Entity
        | "Activity" -> Ok Activity
        | "Agent" -> Ok Agent
        | other -> Error $"unknown ProvOClass case '{other}'"

    /// Serialize a VocabularyRegistry to a deterministic JSON string.
    /// Type-name keys are normalized: FSI "FSI_NNNN." prefixes are stripped and
    /// '+' nested-type separators are replaced with '.' so deserialized keys match
    /// the static CLR names used in lock files.
    /// Strings cross ALC boundaries; use this before casting across FSI contexts.
    let serialize (r: VocabularyRegistry) : string =
        use ms = new System.IO.MemoryStream()
        use w = new Utf8JsonWriter(ms)

        w.WriteStartObject()

        w.WriteStartObject("Prefixes")

        for KeyValue(k, v) in r.Prefixes do
            w.WriteString(k, v.AbsoluteUri)

        w.WriteEndObject()

        w.WriteStartArray("Using")

        for s in r.Using do
            w.WriteStringValue(s)

        w.WriteEndArray()

        w.WriteStartObject("EquivalentClasses")

        for KeyValue(k, v) in r.EquivalentClasses do
            w.WriteString(normalizeFsiKey k, v.AbsoluteUri)

        w.WriteEndObject()

        w.WriteStartObject("SeeAlso")

        for KeyValue(k, vs) in r.SeeAlso do
            w.WriteStartArray(normalizeFsiKey k)

            for u in vs do
                w.WriteStringValue(u.AbsoluteUri)

            w.WriteEndArray()

        w.WriteEndObject()

        w.WriteStartObject("FieldSeeAlso")

        for KeyValue((t, f), vs) in r.FieldSeeAlso do
            w.WriteStartArray(encodeTupleKey (normalizeFsiKey t) f)

            for u in vs do
                w.WriteStringValue(u.AbsoluteUri)

            w.WriteEndArray()

        w.WriteEndObject()

        w.WriteStartObject("ProvClasses")

        for KeyValue(k, v) in r.ProvClasses do
            w.WriteString(normalizeFsiKey k, provOClassToString v)

        w.WriteEndObject()

        w.WriteStartObject("ConstraintPatterns")

        for KeyValue((t, f), v) in r.ConstraintPatterns do
            w.WriteString(encodeTupleKey (normalizeFsiKey t) f, v)

        w.WriteEndObject()

        w.WriteEndObject()
        w.Flush()
        System.Text.Encoding.UTF8.GetString(ms.ToArray())

    /// Deserialize a VocabularyRegistry from the JSON produced by `serialize`.
    /// Returns Error with a diagnostic if the JSON is malformed or any field is missing.
    let deserialize (json: string) : Result<VocabularyRegistry, string> =
        if String.IsNullOrWhiteSpace json then
            invalidArg (nameof json) "json must not be empty"

        try
            use doc = JsonDocument.Parse(json)
            let root = doc.RootElement

            let getProp (name: string) =
                match root.TryGetProperty(name) with
                | false, _ -> Error $"missing property '{name}'"
                | true, p -> Ok p

            let parsePrefixes () =
                match getProp "Prefixes" with
                | Error e -> Error e
                | Ok el ->
                    el.EnumerateObject()
                    |> Seq.fold
                        (fun acc entry ->
                            match acc with
                            | Error e -> Error e
                            | Ok m -> Ok(Map.add entry.Name (Uri(entry.Value.GetString())) m))
                        (Ok Map.empty)

            let parseUsing () =
                match getProp "Using" with
                | Error e -> Error e
                | Ok el ->
                    el.EnumerateArray()
                    |> Seq.fold
                        (fun acc item ->
                            match acc with
                            | Error e -> Error e
                            | Ok s -> Ok(Set.add (item.GetString()) s))
                        (Ok Set.empty)

            let parseEquivalentClasses () =
                match getProp "EquivalentClasses" with
                | Error e -> Error e
                | Ok el ->
                    el.EnumerateObject()
                    |> Seq.fold
                        (fun acc entry ->
                            match acc with
                            | Error e -> Error e
                            | Ok m -> Ok(Map.add entry.Name (Uri(entry.Value.GetString())) m))
                        (Ok Map.empty)

            let parseSeeAlso () =
                match getProp "SeeAlso" with
                | Error e -> Error e
                | Ok el ->
                    el.EnumerateObject()
                    |> Seq.fold
                        (fun acc entry ->
                            match acc with
                            | Error e -> Error e
                            | Ok m ->
                                let uris =
                                    entry.Value.EnumerateArray()
                                    |> Seq.map (fun item -> Uri(item.GetString()))
                                    |> Seq.toList

                                Ok(Map.add entry.Name uris m))
                        (Ok Map.empty)

            let parseFieldSeeAlso () =
                match getProp "FieldSeeAlso" with
                | Error e -> Error e
                | Ok el ->
                    el.EnumerateObject()
                    |> Seq.fold
                        (fun acc entry ->
                            match acc with
                            | Error e -> Error e
                            | Ok m ->
                                match decodeTupleKey entry.Name with
                                | Error e -> Error e
                                | Ok tk ->
                                    let uris =
                                        entry.Value.EnumerateArray()
                                        |> Seq.map (fun item -> Uri(item.GetString()))
                                        |> Seq.toList

                                    Ok(Map.add tk uris m))
                        (Ok Map.empty)

            let parseProvClasses () =
                match getProp "ProvClasses" with
                | Error e -> Error e
                | Ok el ->
                    el.EnumerateObject()
                    |> Seq.fold
                        (fun acc entry ->
                            match acc with
                            | Error e -> Error e
                            | Ok m ->
                                match provOClassFromString (entry.Value.GetString()) with
                                | Error e -> Error e
                                | Ok cls -> Ok(Map.add entry.Name cls m))
                        (Ok Map.empty)

            let parseConstraintPatterns () =
                match getProp "ConstraintPatterns" with
                | Error e -> Error e
                | Ok el ->
                    el.EnumerateObject()
                    |> Seq.fold
                        (fun acc entry ->
                            match acc with
                            | Error e -> Error e
                            | Ok m ->
                                match decodeTupleKey entry.Name with
                                | Error e -> Error e
                                | Ok tk -> Ok(Map.add tk (entry.Value.GetString()) m))
                        (Ok Map.empty)

            match
                parsePrefixes (),
                parseUsing (),
                parseEquivalentClasses (),
                parseSeeAlso (),
                parseFieldSeeAlso (),
                parseProvClasses (),
                parseConstraintPatterns ()
            with
            | Ok p, Ok u, Ok ec, Ok sa, Ok fsa, Ok pc, Ok cp ->
                Ok
                    { Prefixes = p
                      Using = u
                      EquivalentClasses = ec
                      SeeAlso = sa
                      FieldSeeAlso = fsa
                      ProvClasses = pc
                      ConstraintPatterns = cp }
            | Error e, _, _, _, _, _, _ -> Error $"Prefixes: {e}"
            | _, Error e, _, _, _, _, _ -> Error $"Using: {e}"
            | _, _, Error e, _, _, _, _ -> Error $"EquivalentClasses: {e}"
            | _, _, _, Error e, _, _, _ -> Error $"SeeAlso: {e}"
            | _, _, _, _, Error e, _, _ -> Error $"FieldSeeAlso: {e}"
            | _, _, _, _, _, Error e, _ -> Error $"ProvClasses: {e}"
            | _, _, _, _, _, _, Error e -> Error $"ConstraintPatterns: {e}"
        with ex ->
            Error $"JSON parse failed: {ex.Message}"
