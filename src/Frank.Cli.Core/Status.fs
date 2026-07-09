module Frank.Cli.Core.Status

open System
open Frank.Semantic
open Frank.Semantic.LockFile
open Frank.Semantic.VocabClassifier

let private vocabStateToString (state: VocabState) : string =
    match state with
    | VocabState.Confirmed -> "Confirmed"
    | VocabState.Proposed -> "Proposed"
    | VocabState.Undereferenceable -> "Undereferenceable"
    | VocabState.LocallyServedUnconfirmed -> "LocallyServedUnconfirmed"
    | VocabState.Stale -> "Stale"

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

/// Format lock status including mapping counts and vocabulary states derived from
/// classifyReferencedVocab — the single shared classifier.
/// `now` is injected for deterministic SLA reasoning.
let format (now: DateTimeOffset) (lf: LockFile) : string =
    let c = countByStatus lf.Mappings

    let mappingSection =
        $"Confirmed:  {c.Confirmed}\nProposed:   {c.Proposed}\nUnresolved: {c.Unresolved}\nExcluded:   {c.Excluded}"

    match vocabSection now lf with
    | None -> mappingSection
    | Some vs -> $"{mappingSection}\n\n{vs}"

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

    match vocabSection now lf with
    | None -> groupsStr
    | Some vs -> $"{groupsStr}\n\n{vs}"
