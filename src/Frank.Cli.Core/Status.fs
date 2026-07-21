module Frank.Cli.Core.Status

open System
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabClassifier

let private vocabSection (now: DateTimeOffset) (lf: LockFile) : string option =
    let referencedNs = lf.DeclaredPrefixes |> Map.keys |> Seq.toList

    if List.isEmpty referencedNs then
        None
    else
        let states = classifyReferencedVocab lf now referencedNs

        let lines =
            List.zip referencedNs states
            |> List.map (fun (prefix, state) -> $"  {prefix}: {vocabStateToString state}")
            |> String.concat "\n"

        Some $"Vocabularies:\n{lines}"

// Returns (prefix, iri) pairs for every Undereferenceable prefix in DeclaredPrefixes.
let private undereferencedPrefixes (now: DateTimeOffset) (lf: LockFile) : (string * string) list =
    let referencedNs = lf.DeclaredPrefixes |> Map.keys |> Seq.toList

    if List.isEmpty referencedNs then
        []
    else
        let states = classifyReferencedVocab lf now referencedNs

        List.zip referencedNs states
        |> List.choose (fun (prefix, state) ->
            if state = VocabState.Undereferenceable then
                let iri = Map.tryFind prefix lf.DeclaredPrefixes |> Option.defaultValue prefix
                Some(prefix, iri)
            else
                None)

let private warningSection (now: DateTimeOffset) (lf: LockFile) : string option =
    match undereferencedPrefixes now lf with
    | [] -> None
    | pairs ->
        let lines =
            pairs
            |> List.map (fun (prefix, iri) -> $"  {prefix} ({iri}): {Accept.vocabWarningHint iri}")
            |> String.concat "\n"

        Some $"Warnings:\n{lines}"

// ── Mapping scan helpers (type/field enrichment only) ─────────────────────────

/// True when `iri` is a reference under the given namespace: either a full IRI
/// starting with `nsIri` at a namespace boundary (#/), or a CURIE whose prefix matches `prefix`.
let private isReferenceUnder (prefix: string) (nsIri: string) (iri: string) : bool =
    let absMatch =
        iri.StartsWith(nsIri, StringComparison.Ordinal)
        && (nsIri.EndsWith("#", StringComparison.Ordinal)
            || nsIri.EndsWith("/", StringComparison.Ordinal)
            || (iri.Length > nsIri.Length
                && (iri.[nsIri.Length] = '#' || iri.[nsIri.Length] = '/')))

    absMatch
    || (not (iri.Contains("://"))
        && iri.StartsWith(prefix + ":", StringComparison.Ordinal))

/// Return the name of the first field in `fields` whose IRI references (prefix, nsIri).
let private findFieldInPayload (prefix: string) (nsIri: string) (fields: FieldMapping list) : string option =
    fields
    |> List.tryPick (fun f ->
        f.Iri
        |> Option.bind (fun iri ->
            if isReferenceUnder prefix nsIri iri then
                Some f.Name
            else
                None))

/// Return the name of the first field/case referencing (prefix, nsIri) in `shape`.
let private findFieldInShape (prefix: string) (nsIri: string) (shape: MappingShape) : string option =
    match shape with
    | MappingShape.Record fs -> findFieldInPayload prefix nsIri fs
    | MappingShape.Union cs ->
        cs
        |> List.tryPick (fun c ->
            match
                c.Iri
                |> Option.bind (fun iri ->
                    if isReferenceUnder prefix nsIri iri then
                        Some c.Name
                    else
                        None)
            with
            | Some n -> Some n
            | None -> findFieldInPayload prefix nsIri c.Payload)

/// Scan `mappings` for the first reference to (prefix, nsIri).
/// Returns (type, field): field-level reference wins over type-level; None when absent.
let private findMappingRef (prefix: string) (nsIri: string) (mappings: Mapping list) : string option * string option =
    let fieldRef =
        mappings
        |> List.tryPick (fun m -> findFieldInShape prefix nsIri m.Shape |> Option.map (fun f -> m.FSharpType, f))

    match fieldRef with
    | Some(t, f) -> Some t, Some f
    | None ->
        let typeRef =
            mappings
            |> List.tryPick (fun m ->
                m.Iri
                |> Option.bind (fun iri ->
                    if isReferenceUnder prefix nsIri iri then
                        Some m.FSharpType
                    else
                        None))

        typeRef, None

/// Returns structured warning records for each Undereferenceable prefix.
/// Used by the CLI for --strict exit-code decisions and --format json output.
let getWarnings (now: DateTimeOffset) (lf: LockFile) : Accept.VocabWarning list =
    undereferencedPrefixes now lf
    |> List.map (fun (prefix, iri) ->
        let typeName, fieldName = findMappingRef prefix iri lf.Mappings

        let location =
            match typeName with
            | None -> None
            | Some t ->
                Some(
                    { Accept.VocabWarningLocation.Type = t
                      Accept.VocabWarningLocation.Field = fieldName }
                )

        { Accept.VocabWarning.Prefix = prefix
          State = VocabState.Undereferenceable
          Iri = iri
          Location = location
          Hint = Accept.vocabWarningHint iri })

/// Format lock status including mapping counts and vocabulary states derived from
/// classifyReferencedVocab — the single shared classifier.
/// `now` is injected for deterministic SLA reasoning.
let format (now: DateTimeOffset) (lf: LockFile) : string =
    let c = countByStatus lf.Mappings

    let mappingSection =
        $"Confirmed:  {c.Confirmed}\nProposed:   {c.Proposed}\nUnresolved: {c.Unresolved}\nExcluded:   {c.Excluded}"

    let parts =
        [ Some mappingSection; vocabSection now lf; warningSection now lf ]
        |> List.choose id

    String.concat "\n\n" parts

let private formatGroupBlock (g: PackageGroup) : string =
    let statusLine =
        $"Confirmed:  {g.Counts.Confirmed}\nProposed:   {g.Counts.Proposed}\nUnresolved: {g.Counts.Unresolved}\nExcluded:   {g.Counts.Excluded}"

    if g.Vocabs = [] then
        $"{g.Namespace}\n{statusLine}"
    else
        let vocabList =
            g.Vocabs |> List.map (fun (k, n) -> $"{k} ({n})") |> String.concat ", "

        $"{g.Namespace}\n{statusLine}\nvocabs: {vocabList}"

let formatByPackage (now: DateTimeOffset) (lf: LockFile) : string =
    let knownPrefixes =
        Set.union (lf.Vocabularies |> Map.keys |> Set.ofSeq) (lf.DeclaredPrefixes |> Map.keys |> Set.ofSeq)

    let groups = countByPackage knownPrefixes lf.Mappings
    let groupsStr = groups |> List.map formatGroupBlock |> String.concat "\n\n"

    let parts =
        [ Some groupsStr; vocabSection now lf; warningSection now lf ] |> List.choose id

    String.concat "\n\n" parts
