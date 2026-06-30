module Frank.Cli.Core.EmitterShared

open System
open Frank.Semantic

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
