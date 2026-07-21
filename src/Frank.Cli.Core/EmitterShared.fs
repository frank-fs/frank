module Frank.Cli.Core.EmitterShared

open System
open Fabulous.AST
open Fantomas.Core.SyntaxOak
open type Fabulous.AST.Ast
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
/// actually back the app's own resource identity. Ownership itself is decided by
/// VocabClassifier.ownedIdentityAuthorities — the single function that defines "what counts as
/// this app's own identity" (type IRI, field IRI, and Confirmed union-case IRI, #423); this only
/// decides which declared-only base URIs to test against that authority set. A prefix that is
/// declared-only but never used to identify a mapped resource (e.g. referenced only via
/// seeAlso/equivalentClass, pointing at a genuinely external vocabulary such as Wikidata) is never
/// classified as owned. Their base URIs are returned as a set; matching IRIs will be emitted as
/// relative paths.
let internal declaredOnlyBases (lock: LockFile) : Set<string> =
    let candidates =
        lock.DeclaredPrefixes
        |> Map.filter (fun k _ -> not (Map.containsKey k lock.Vocabularies))
        |> Map.toSeq
        |> Seq.map snd
        |> Set.ofSeq

    let ownAuthorities = VocabClassifier.ownedIdentityAuthorities lock

    candidates |> Set.filter (VocabClassifier.authorityInSet ownAuthorities)

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

/// Emit a `System.Uri` AST expression for a class/case/property IRI, relativized for
/// declared-only/owned prefixes (#396/#415) — the ONE place both LinkedDataEmitter and
/// SemanticModelEmitter build this expression (constitution #8: no duplicated logic).
/// `href = hrefFor bases u.AbsoluteUri`; when unchanged (external vocab, already absolute)
/// emits `System.Uri "<href>"`; when relativized emits
/// `System.Uri ("<href>", System.UriKind.Relative)` — the two-arg form is required because
/// the single-arg Uri(string) constructor's UriKind.RelativeOrAbsolute inference treats a
/// leading '/' as a Unix absolute file path on this platform (e.g. "/tictactoe#Game" →
/// file:///tictactoe%23Game), not a relative Uri — silently defeating rebasing at request
/// time via Frank.UriResolution.resolveAgainst (#396 round 5).
let internal uriExprFor (bases: Set<string>) (u: Uri) : WidgetBuilder<Expr> =
    let href = hrefFor bases u.AbsoluteUri

    if href = u.AbsoluteUri then
        AstRender.appExpr "System.Uri" (AstRender.strExpr href)
    else
        AstRender.appExpr
            "System.Uri"
            (AstRender.parenExpr (
                AstRender.tupleExpr [ AstRender.strExpr href; AstRender.rawExpr "System.UriKind.Relative" ]
            ))
