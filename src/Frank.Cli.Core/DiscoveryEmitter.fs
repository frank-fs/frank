module Frank.Cli.Core.DiscoveryEmitter

open System
open Frank.Semantic
open Frank.Semantic.LockFile

// ── IRI local-name helper ─────────────────────────────────────────────────────

/// Extract the local name from a full IRI (part after last '#' or '/').
let private localName (iri: string) : string =
    let hashIdx = iri.LastIndexOf('#')
    let slashIdx = iri.LastIndexOf('/')
    let idx = max hashIdx slashIdx

    if idx >= 0 && idx < iri.Length - 1 then
        iri.[idx + 1 ..]
    else
        iri

// ── Descriptor builders ───────────────────────────────────────────────────────

type internal ResolvedDescriptor = { Id: string; Href: string option }

let private hrefOption (href: string) : string option =
    if String.IsNullOrEmpty href then None else Some href

/// Compute which prefixes are declared-only (in DeclaredPrefixes but not in Vocabularies).
/// Their base URIs are returned as a set; matching IRIs will be emitted as relative paths.
let private declaredOnlyBases (lock: LockFile) : Set<string> =
    lock.DeclaredPrefixes
    |> Map.filter (fun k _ -> not (Map.containsKey k lock.Vocabularies))
    |> Map.toSeq
    |> Seq.map snd
    |> Set.ofSeq

/// For a declared-only IRI, extract the host-relative path+fragment.
/// For external vocab IRIs, return the absolute URI unchanged.
let private hrefFor (bases: Set<string>) (absoluteUri: string) : string =
    let matchingBase =
        bases |> Set.toSeq |> Seq.tryFind (fun b -> absoluteUri.StartsWith(b))

    match matchingBase with
    | None -> absoluteUri
    | Some _ ->
        let uri = Uri(absoluteUri)
        uri.PathAndQuery + uri.Fragment

/// Resolve the type-level descriptor for one ResolvedResource. Returns None if ClassIri is absent.
let private typeDescriptor (bases: Set<string>) (r: ResolvedResource) : ResolvedDescriptor option =
    r.ClassIri
    |> Option.map (fun uri ->
        let absolute = uri.AbsoluteUri
        let href = hrefFor bases absolute

        { Id = localName absolute
          Href = hrefOption href })

/// Resolve field descriptors for one resource; skip fields with no Iri.
let private fieldDescriptors (bases: Set<string>) (fields: ResolvedField list) : ResolvedDescriptor list =
    fields
    |> List.choose (fun f ->
        f.Iri
        |> Option.map (fun uri ->
            let absolute = uri.AbsoluteUri
            let href = hrefFor bases absolute

            { Id = localName absolute
              Href = hrefOption href }))

/// Collect all descriptors from all resources in dependency order: each type then its fields.
let private collectDescriptors (bases: Set<string>) (resources: ResolvedResource list) : ResolvedDescriptor list =
    resources
    |> List.collect (fun r ->
        let typeDs = typeDescriptor bases r |> Option.toList
        let fieldDs = fieldDescriptors bases r.Fields
        typeDs @ fieldDs)

/// Collect unique `rel="type"` link values for resources that have a ClassIri.
let private collectDescribedByLinks (resources: ResolvedResource list) : string list =
    let folder (seen: Set<string>, acc: string list) (r: ResolvedResource) =
        match r.ClassIri with
        | None -> seen, acc
        | Some uri ->
            let fullIri = uri.AbsoluteUri

            if Set.contains fullIri seen then
                seen, acc
            else
                let link = $"<{fullIri}>; rel=\"type\""
                Set.add fullIri seen, link :: acc

    let _, revLinks = List.fold folder (Set.empty, []) resources
    List.rev revLinks

// ── Pure projection ───────────────────────────────────────────────────────────

/// Pure projection: model → (descriptors, describedBy links). Testable typed output.
/// declaredOnlyBases: set of base URI strings whose IRIs should be emitted as host-relative paths.
let internal projectDiscovery
    (declaredOnlyBases: Set<string>)
    (model: ResolvedModel)
    : ResolvedDescriptor list * string list =
    collectDescriptors declaredOnlyBases model.Resources, collectDescribedByLinks model.Resources

// ── Source rendering via AstRender (no string concat) ────────────────────────

let private descriptorExpr (d: ResolvedDescriptor) =
    AstRender.recordExpr
        [ "Id", AstRender.strExpr d.Id
          "Type", AstRender.strExpr "semantic"
          "Doc", AstRender.noneExpr
          "Href", AstRender.optionExpr AstRender.strExpr d.Href ]

let private configExpr (profileUri: string) (descriptors: ResolvedDescriptor list) (links: string list) =
    AstRender.recordExpr
        [ "ProfileUri", AstRender.strExpr profileUri
          "HomeRoute", AstRender.strExpr "/"
          "AlpsDescriptors", AstRender.listExpr (descriptors |> List.map descriptorExpr)
          "DescribedByLinks", AstRender.listExpr (links |> List.map AstRender.strExpr) ]

// ── Public API ────────────────────────────────────────────────────────────────

/// Emit a GeneratedDiscovery F# module from a lock file and vocabulary registry.
///
/// moduleName   — the F# module name to emit (e.g. "TicTacToe.GeneratedDiscovery")
/// profileUri   — the ALPS profile route (e.g. "/alps/tictactoe")
/// registry     — the VocabularyRegistry providing prefix→URI mappings
/// lock         — the resolved lock file
///
/// Returns Ok with the F# source string, or Error with a message if any IRI
/// references an unknown prefix.
let emit
    (moduleName: string)
    (profileUri: string)
    (registry: VocabularyRegistry)
    (lock: LockFile)
    : Result<string, string> =
    if String.IsNullOrWhiteSpace profileUri then
        invalidArg (nameof profileUri) "profileUri must not be empty"

    AstRender.validateModuleName moduleName
    |> Result.bind (fun () -> ResolvedModel.build registry lock)
    |> Result.map (fun model ->
        let bases = declaredOnlyBases lock
        let descriptors, links = projectDiscovery bases model
        let value = configExpr profileUri descriptors links

        AstRender.formatTypedValueModule
            moduleName
            (Some AstRender.autoGeneratedHeader)
            [ "Frank.Discovery" ]
            "discoveryConfig"
            "DiscoveryConfig"
            value)
