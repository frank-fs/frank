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

type internal ResolvedDescriptor =
    { Id: string
      Href: string option
      IsAction: bool
      Rt: string option
      Children: ResolvedDescriptor list }

let private hrefOption (href: string) : string option =
    if String.IsNullOrEmpty href then None else Some href

/// Build a leaf field descriptor; returns None when the field has no IRI.
let private fieldDescriptor (bases: Set<string>) (f: ResolvedField) : ResolvedDescriptor option =
    f.Iri
    |> Option.map (fun uri ->
        let absolute = uri.AbsoluteUri

        { Id = localName absolute
          Href = hrefOption (EmitterShared.hrefFor bases absolute)
          IsAction = false
          Rt = None
          Children = [] })

/// Build a leaf case descriptor for a union case.
let private caseDescriptor (bases: Set<string>) (c: ResolvedCase) : ResolvedDescriptor =
    let absolute = c.Iri.AbsoluteUri

    { Id = c.CaseName
      Href = hrefOption (EmitterShared.hrefFor bases absolute)
      IsAction = false
      Rt = None
      Children = [] }

/// Collect all class-level descriptors. Each class descriptor carries its children:
/// - Union types: confirmed case descriptors (AC1 #17 — outcome terms discoverable).
/// - Record types: field descriptors with IRIs (AC1 #4 nesting).
/// A resource with a declared Rt is an unsafe transition (isAction = r.Rt.IsSome).
/// Rt is the href of the declared return-type IRI, set explicitly in the lock file.
let private collectDescriptors (bases: Set<string>) (resources: ResolvedResource list) : ResolvedDescriptor list =
    resources
    |> List.choose (fun r ->
        r.ClassIri
        |> Option.map (fun uri ->
            let absolute = uri.AbsoluteUri
            let isAction = r.Rt.IsSome

            let children =
                if not (List.isEmpty r.Cases) then
                    r.Cases |> List.map (caseDescriptor bases)
                else
                    r.Fields |> List.choose (fieldDescriptor bases)

            let rt =
                r.Rt |> Option.map (fun rtUri -> EmitterShared.hrefFor bases rtUri.AbsoluteUri)

            { Id = localName absolute
              Href = hrefOption (EmitterShared.hrefFor bases absolute)
              IsAction = isAction
              Rt = rt
              Children = children }))

/// Collect unique `rel="type"` link values for resources that have a ClassIri.
/// Declared-only prefix class IRIs are emitted as host-relative link targets.
let private collectDescribedByLinks (bases: Set<string>) (resources: ResolvedResource list) : string list =
    let folder (seen: Set<string>, acc: string list) (r: ResolvedResource) =
        match r.ClassIri with
        | None -> seen, acc
        | Some uri ->
            let fullIri = uri.AbsoluteUri

            if Set.contains fullIri seen then
                seen, acc
            else
                let linkIri = EmitterShared.hrefFor bases fullIri
                let link = $"<{linkIri}>; rel=\"type\""
                Set.add fullIri seen, link :: acc

    let _, revLinks = List.fold folder (Set.empty, []) resources
    List.rev revLinks

// ── Pure projection ───────────────────────────────────────────────────────────

/// Recursively collect all descriptor IDs (top-level and nested children).
let rec private collectAllIds (descriptors: ResolvedDescriptor list) : string list =
    descriptors |> List.collect (fun d -> d.Id :: collectAllIds d.Children)

/// Assert that all descriptor IDs in the projected list are unique (ALPS §3.1).
/// Throws invalidOp naming the first duplicate — this is a codegen-time invariant.
let private assertUniqueIds (descriptors: ResolvedDescriptor list) : unit =
    let allIds = collectAllIds descriptors

    let dups =
        allIds
        |> List.groupBy id
        |> List.choose (fun (k, vs) -> if vs.Length > 1 then Some k else None)

    if not dups.IsEmpty then
        let dupsStr = String.concat ", " dups

        invalidOp (
            sprintf
                "DiscoveryEmitter: duplicate ALPS descriptor ids: %s. ALPS §3.1 requires globally unique ids within a profile."
                dupsStr
        )

/// Pure projection: model → (descriptors, describedBy links). Testable typed output.
/// declaredOnlyBases: set of base URI strings whose IRIs should be emitted as host-relative paths.
let internal projectDiscovery
    (declaredOnlyBases: Set<string>)
    (model: ResolvedModel)
    : ResolvedDescriptor list * string list =
    collectDescriptors declaredOnlyBases model.Resources, collectDescribedByLinks declaredOnlyBases model.Resources

// ── Source rendering via AstRender (no string concat) ────────────────────────

let rec private descriptorExpr (d: ResolvedDescriptor) =
    AstRender.recordExpr
        [ "Id", AstRender.strExpr d.Id
          "Type", AstRender.strExpr (if d.IsAction then "unsafe" else "semantic")
          "Doc", AstRender.noneExpr
          "Href", AstRender.optionExpr AstRender.strExpr d.Href
          "Descriptors", AstRender.listExpr (d.Children |> List.map descriptorExpr)
          "Rt", AstRender.optionExpr AstRender.strExpr d.Rt ]

/// Map.ofList [("k1","v1"); ...] for a string*string list.
let private fieldVarMapExpr (entries: (string * string) list) =
    AstRender.appExpr
        "Map.ofList"
        (AstRender.listExpr (
            entries
            |> List.map (fun (k, v) -> AstRender.tupleExpr [ AstRender.strExpr k; AstRender.strExpr v ])
        ))

/// Map.ofList [("rel", Map.ofList [...]); ...] for the ResourceHrefVars field.
let private resourceHrefVarsExpr (vars: (string * (string * string) list) list) =
    AstRender.appExpr
        "Map.ofList"
        (AstRender.listExpr (
            vars
            |> List.map (fun (rel, entries) ->
                AstRender.tupleExpr [ AstRender.strExpr rel; AstRender.parenExpr (fieldVarMapExpr entries) ])
        ))

/// For each resource with a class IRI and confirmed field IRIs, build a
/// (classIri, [(varName, meaningIri)]) entry. varName is the field name lowercased;
/// meaningIri is host-relative for declared-only prefix IRIs, absolute for external vocab IRIs.
/// Template variables from a parent path segment (e.g. {id} in /games/{id}/moves) are
/// resolved by following the declared Rt linkage to the target resource and reading THAT
/// resource's matching field — NOT a global name pool. Own field entries take precedence;
/// supplemental entries come from the Rt target only (one hop).
let private computeHrefVars (bases: Set<string>) (model: ResolvedModel) : (string * (string * string) list) list =
    let toFieldEntry (f: ResolvedField) =
        f.Iri
        |> Option.map (fun iri -> f.Name.ToLowerInvariant(), EmitterShared.hrefFor bases iri.AbsoluteUri)

    let byClassIri =
        model.Resources
        |> List.choose (fun r -> r.ClassIri |> Option.map (fun uri -> uri.AbsoluteUri, r))
        |> Map.ofList

    model.Resources
    |> List.choose (fun r ->
        r.ClassIri
        |> Option.map (fun classIri ->
            let ownEntries = r.Fields |> List.choose toFieldEntry
            let ownKeys = ownEntries |> List.map fst |> Set.ofList

            let supplemental =
                r.Rt
                |> Option.bind (fun rtUri -> Map.tryFind rtUri.AbsoluteUri byClassIri)
                |> Option.map (fun rtResource ->
                    rtResource.Fields
                    |> List.choose toFieldEntry
                    |> List.filter (fun (k, _) -> not (Set.contains k ownKeys)))
                |> Option.defaultValue []

            classIri.AbsoluteUri, ownEntries @ supplemental))
    |> List.filter (fun (_, entries) -> not entries.IsEmpty)

let private configExpr
    (profileUri: string)
    (descriptors: ResolvedDescriptor list)
    (links: string list)
    (hrefVars: (string * (string * string) list) list)
    =
    AstRender.recordExpr
        [ "ProfileUri", AstRender.strExpr profileUri
          "HomeRoute", AstRender.strExpr "/"
          "AlpsDescriptors", AstRender.listExpr (descriptors |> List.map descriptorExpr)
          "DescribedByLinks", AstRender.listExpr (links |> List.map AstRender.strExpr)
          "ResourceHrefVars", resourceHrefVarsExpr hrefVars ]

// ── Public API ────────────────────────────────────────────────────────────────

/// Return VocabularyRegistry.empty for use as the Discovery registry.
/// The Prefixes field was previously populated from lock.Vocabularies, but
/// ResolvedModel.build ignores registry.Prefixes for IRI resolution (it calls
/// LockFile.buildPrefixMap directly). Populating Prefixes was dead code (rule 8).
let buildRegistry (_lock: LockFile) : VocabularyRegistry = VocabularyRegistry.empty

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
        let bases = EmitterShared.declaredOnlyBases lock
        let descriptors, links = projectDiscovery bases model
        assertUniqueIds descriptors
        let hrefVars = computeHrefVars bases model
        let value = configExpr profileUri descriptors links hrefVars

        AstRender.formatTypedValueModule
            moduleName
            (Some AstRender.autoGeneratedHeader)
            [ "Frank.Discovery" ]
            "discoveryConfig"
            "DiscoveryConfig"
            value)
