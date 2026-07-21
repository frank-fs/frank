module Frank.LinkedData.Ontology

open System
open VDS.RDF
open Frank.Semantic

let private rdf = "http://www.w3.org/1999/02/22-rdf-syntax-ns#"
let private rdfs = "http://www.w3.org/2000/01/rdf-schema#"
let private owl = "http://www.w3.org/2002/07/owl#"

/// Resolve `u` to an absolute Uri.
/// - Already absolute (an external vocab IRI, or an owned IRI already resolved): returned
///   unchanged — an external vocab Uri is never rebased against `baseUri`, even when supplied.
/// - Relative (the app's own declared-only prefix, emitted host-relative by LinkedDataEmitter):
///   rebased against `baseUri` when supplied (#396 round 5 — resolves the real deployed origin
///   at call time, not a codegen-time placeholder). The absolute-vs-relative rule itself is
///   Frank.UriResolution.resolveAgainst — the ONE place both this module and
///   Frank.Discovery.DiscoveryMiddleware.resolveHref apply it (#398 /simplify item 1).
/// - Relative with no `baseUri` supplied: a codegen misclassification (or a caller with no live
///   origin to rebase against) — fail loud here, before the throwing .AbsoluteUri accessor,
///   naming the offending field and its owning class when there is one (#396). `classIri` is
///   None for call sites with no single owning class, e.g. toJsonLdContext's ContextBases.
let private resolveAbsolute
    (baseUri: Uri option)
    (paramName: string)
    (fieldLabel: string)
    (classIri: Uri option)
    (u: Uri)
    : Uri =
    match baseUri with
    | Some b -> Frank.UriResolution.resolveAgainst b u
    | None when u.IsAbsoluteUri -> u
    | None ->
        let owner =
            match classIri with
            | Some c -> $"OntologyDecl class '{c}'"
            | None -> "OntologyDecl"

        invalidArg
            paramName
            $"{owner} declares a relative {fieldLabel} Uri '{u.OriginalString}'; {fieldLabel} must be an absolute, dereferenceable URI, or a baseUri must be supplied to rebase it — Ontology.toGraph/toJsonLdContext received no baseUri."

/// Assert `u` is absolute — unlike resolveAbsolute, NEVER rebases against a baseUri, regardless
/// of whether one is supplied. ContextBases is built exclusively from `using` (external vocab)
/// prefixes (LinkedDataEmitter.contextBases), which must always already be absolute,
/// dereferenceable URIs — never the app's own relative ones (those live on ClassDecl.Iri and
/// friends, which resolveAbsolute legitimately rebases). A relative ContextBases entry is always
/// a bug: fail loud here rather than let it silently rebase into a garbage-but-valid-looking URI
/// whenever a caller happens to supply a baseUri for unrelated reasons (#396 round 7).
let private assertAbsolute (paramName: string) (fieldLabel: string) (u: Uri) : Uri =
    if u.IsAbsoluteUri then
        u
    else
        invalidArg
            paramName
            $"OntologyDecl declares a relative {fieldLabel} Uri '{u.OriginalString}'; {fieldLabel} must be an absolute, dereferenceable URI — ContextBases entries are never rebased against a baseUri."

let private addClass (g: IGraph) (baseUri: Uri option) (c: ClassDecl) : unit =
    if
        not (
            g.NamespaceMap.HasNamespace "rdf"
            && g.NamespaceMap.HasNamespace "rdfs"
            && g.NamespaceMap.HasNamespace "owl"
        )
    then
        invalidOp "addClass requires rdf/rdfs/owl namespaces registered on the graph"

    let classAbs = resolveAbsolute baseUri "classIri" "Iri" (Some c.Iri) c.Iri
    let subj = Triples.uriNode g classAbs.AbsoluteUri
    Triples.assert3 g subj (Triples.qnameNode g "rdf:type") (Triples.qnameNode g "owl:Class")

    match c.EquivalentClass with
    | Some e ->
        let eAbs =
            resolveAbsolute baseUri "equivalentClass" "owl:equivalentClass" (Some c.Iri) e

        // #417: EquivalentClass resolving to the same IRI as the class's own Iri is a
        // tautology (`X owl:equivalentClass X`), not an assertion — e.g. when
        // ConventionEngine.applyExplicitClass has already overridden ClassIri to the
        // explicit equivalentClass target, nothing distinct is left to assert.
        if eAbs.AbsoluteUri <> classAbs.AbsoluteUri then
            Triples.assert3 g subj (Triples.qnameNode g "owl:equivalentClass") (Triples.uriNode g eAbs.AbsoluteUri)
    | None -> ()

    for s in c.SeeAlso do
        let sAbs = resolveAbsolute baseUri "seeAlso" "rdfs:seeAlso" (Some c.Iri) s
        Triples.assert3 g subj (Triples.qnameNode g "rdfs:seeAlso") (Triples.uriNode g sAbs.AbsoluteUri)

    for p in c.Properties do
        let pAbs =
            resolveAbsolute baseUri "propertyIri" "PropertyDecl.Iri" (Some c.Iri) p.Iri

        let dAbs =
            resolveAbsolute baseUri "domain" "PropertyDecl.Domain" (Some c.Iri) p.Domain

        let pNode = Triples.uriNode g pAbs.AbsoluteUri
        Triples.assert3 g pNode (Triples.qnameNode g "rdf:type") (Triples.qnameNode g "rdf:Property")
        Triples.assert3 g pNode (Triples.qnameNode g "rdfs:domain") (Triples.uriNode g dAbs.AbsoluteUri)

/// `baseUri`: when Some, rebases any relative (owned, not-yet-resolved) Uri in `ontology`
/// against it — the real deployed origin at call time (#396 round 5). External vocab Uris
/// (already absolute) are always passed through unchanged, regardless of `baseUri`. When None,
/// a relative Uri fails loud instead of rebasing (see resolveAbsolute).
let toGraph (baseUri: Uri option) (ontology: OntologyDecl) : IGraph =
    let g = new Graph() :> IGraph
    g.NamespaceMap.AddNamespace("rdf", UriFactory.Create rdf)
    g.NamespaceMap.AddNamespace("rdfs", UriFactory.Create rdfs)
    g.NamespaceMap.AddNamespace("owl", UriFactory.Create owl)

    for c in ontology.Classes do
        addClass g baseUri c

    g

/// `rdf`/`rdfs`/`owl` are always listed first — toGraph unconditionally registers all three
/// namespaces on the graph regardless of `ontology.Classes` (see toGraph above), so
/// toJsonLdContext must expose matching external-document coverage for every triple addClass can
/// emit (rdf:type, owl:Class, owl:equivalentClass, rdfs:seeAlso, rdf:Property, rdfs:domain) or a
/// real JSON-LD consumer cannot compact them (#396 round 6). Unlike toGraph, `baseUri` is
/// accepted only for signature parity — it is NEVER used to rebase `ontology.ContextBases`
/// entries, even when Some. Every ContextBases entry is instead asserted absolute up front
/// (assertAbsolute), because ContextBases is built exclusively from `using` (genuinely external
/// vocab) prefixes, which must always already be absolute — a relative entry is always a bug and
/// fails loud with ArgumentException regardless of `baseUri` (#396 round 7).
let toJsonLdContext (baseUri: Uri option) (ontology: OntologyDecl) : string =
    let contextBaseItems =
        ontology.ContextBases
        |> List.map (fun u ->
            let uAbs = assertAbsolute "contextBases" "ContextBases" u
            uAbs.AbsoluteUri.TrimEnd('/'))

    let items =
        ([ rdf; rdfs; owl ] @ contextBaseItems)
        |> List.map (fun s -> "\"" + s + "\"")
        |> String.concat ","

    "{\"@context\":[" + items + "]}"
