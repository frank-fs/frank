module Frank.Cli.Core.EmitterShared

open System
open Frank.Semantic
open Frank.Semantic.LockFile

let isExternalIri (using: Set<string>) (prefixes: Map<string, Uri>) (iri: Uri) : bool =
    using
    |> Set.exists (fun prefix ->
        match Map.tryFind prefix prefixes with
        | None -> false
        | Some ns -> iri.AbsoluteUri.StartsWith(ns.AbsoluteUri, StringComparison.Ordinal))

let computeKnownNamespaces (registry: VocabularyRegistry) : string list =
    let inScope =
        if Set.isEmpty registry.Using then
            registry.Prefixes |> Map.toSeq |> Seq.map snd
        else
            registry.Using
            |> Set.toSeq
            |> Seq.choose (fun p -> Map.tryFind p registry.Prefixes)

    inScope |> Seq.map (fun u -> u.AbsoluteUri) |> Seq.distinct |> Seq.toList

/// Compute which prefixes are declared-only (in DeclaredPrefixes but not in Vocabularies) AND
/// actually back the app's own resource identity — i.e. their authority matches at least one
/// resource's ClassIri or field Iri in the resolved model (#396). A prefix that is declared-only
/// but never used to identify a mapped resource (e.g. referenced only via seeAlso/equivalentClass,
/// pointing at a genuinely external vocabulary such as Wikidata) is never classified as owned:
/// VocabClassifier.isOwnedByAuthority is the single authority check — this only decides which
/// candidate base URIs to test it against, derived from the produced ResolvedModel.
/// Their base URIs are returned as a set; matching IRIs will be emitted as relative paths.
let internal declaredOnlyBases (lock: LockFile) (model: ResolvedModel) : Set<string> =
    let candidates =
        lock.DeclaredPrefixes
        |> Map.filter (fun k _ -> not (Map.containsKey k lock.Vocabularies))
        |> Map.toSeq
        |> Seq.map snd
        |> Set.ofSeq

    let identityUris =
        model.Resources
        |> List.collect (fun r -> (r.ClassIri |> Option.toList) @ (r.Fields |> List.choose (fun f -> f.Iri)))

    candidates
    |> Set.filter (fun candidateBase ->
        identityUris
        |> List.exists (fun u -> VocabClassifier.isOwnedByAuthority candidateBase u.AbsoluteUri))

/// For a declared-only IRI, extract the host-relative path+fragment.
/// For external vocab IRIs, return the absolute URI unchanged.
let internal hrefFor (bases: Set<string>) (absoluteUri: string) : string =
    let matchingBase =
        bases |> Set.toSeq |> Seq.tryFind (fun b -> absoluteUri.StartsWith(b))

    match matchingBase with
    | None -> absoluteUri
    | Some _ ->
        let uri = Uri(absoluteUri)
        uri.PathAndQuery + uri.Fragment
