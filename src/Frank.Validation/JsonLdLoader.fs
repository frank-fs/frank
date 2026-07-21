module Frank.Validation.JsonLdLoader

open System
open System.Collections.Generic
open Newtonsoft.Json.Linq
open VDS.RDF.JsonLd
open Frank.Semantic

/// rdf/rdfs/owl are always in play on every served @context (Ontology.toGraph/
/// toJsonLdContext unconditionally register all three regardless of the app's own
/// declared vocabulary, Frank.LinkedData/Ontology.fs:110-133) — never app-declared vocab,
/// so computeKnownNamespaces (built solely from registry.Using/registry.Prefixes) never
/// includes them. Hardcoded here rather than requiring every call site to special-case
/// them (#414 cause a).
let private wellKnownNamespaces =
    [ "http://www.w3.org/1999/02/22-rdf-syntax-ns#"
      "http://www.w3.org/2000/01/rdf-schema#"
      "http://www.w3.org/2002/07/owl#" ]

/// Build the synthesized {"@context":{"@vocab": ns}} document for a resolved request —
/// DocumentUrl stays the REQUESTED uri (where the caller asked to load from), while the
/// synthesized @vocab is always the matched known NAMESPACE, never the document URL itself
/// (the one shared builder both the exact-match and authority-fallback branches below use).
let private buildDocument (requestedUri: Uri) (ns: string) : RemoteDocument =
    let doc = RemoteDocument()
    doc.DocumentUrl <- requestedUri
    doc.Document <- JObject.Parse(sprintf """{"@context":{"@vocab":"%s"}}""" ns)
    doc

/// Offline JSON-LD context loader. For a context IRI that matches a known vocabulary
/// namespace (exactly, or by trailing-slash variant), returns a synthesized
/// {"@context":{"@vocab": ns}} document so bare terms expand by concatenation to the SAME
/// IRIs Frank's shapes use. Falls back to AUTHORITY matching (VocabClassifier.
/// isOwnedByAuthority — the SAME mechanism LinkedDataMiddleware itself already uses to
/// decide local-vs-external prefix inlining, #394) for a context-DOCUMENT-URL that shares a
/// known namespace's authority but isn't the bare namespace string itself — e.g. a served
/// @context can legitimately cite "https://schema.org/version/latest/schemaorg-current-https.jsonld"
/// (a real, versioned schema.org context document) while only "https://schema.org/" is a
/// registered namespace (#414 cause b); every namespace this app knows still has its OWN
/// document served, since the resolved @vocab is always the known namespace, never the
/// document URL itself. Fails closed (throws) for any OTHER-authority context IRI — a
/// validator must never let missing context look like conforming data.
let synthesizing (namespaces: string seq) : JsonLdDocumentLoader =
    let allNamespaces = Seq.append wellKnownNamespaces namespaces |> Seq.toList
    let index = Dictionary<string, string>(StringComparer.Ordinal)

    for ns in allNamespaces do
        index.[ns] <- ns
        index.[ns.TrimEnd('/')] <- ns

    // Precomputed once per synthesizing call (not per request): normalizing each candidate
    // namespace's authority up front lets the per-request fallback below normalize only the
    // requested URI, via VocabClassifier.authorityInSet, instead of re-normalizing every
    // candidate namespace on every unresolved request (see authorityInSet's own doc comment).
    // First-listed namespace wins when several share a normalized authority (e.g. rdf/rdfs/owl
    // all normalize to the same w3.org authority) — matches the original List.tryFind's
    // first-match-in-list-order semantics.
    let namespacesByAuthority =
        allNamespaces
        |> List.choose (fun ns -> VocabClassifier.normalizeAuthority ns |> Option.map (fun a -> a, ns))
        |> List.fold (fun m (a, ns) -> if Map.containsKey a m then m else Map.add a ns m) Map.empty

    let knownAuthorities =
        namespacesByAuthority |> Map.toSeq |> Seq.map fst |> Set.ofSeq

    let load (uri: Uri) (_opts: JsonLdLoaderOptions) : RemoteDocument =
        let key = uri.AbsoluteUri

        match index.TryGetValue(key) with
        | true, ns -> buildDocument uri ns
        | false, _ ->
            if VocabClassifier.authorityInSet knownAuthorities key then
                let ns =
                    VocabClassifier.normalizeAuthority key
                    |> Option.bind (fun a -> Map.tryFind a namespacesByAuthority)
                    |> Option.get

                buildDocument uri ns
            else
                failwithf
                    "Frank.Validation: no known vocabulary namespace for JSON-LD @context '%s'; \
                     declare its prefix in the vocabulary CE"
                    key

    JsonLdDocumentLoader(load)
