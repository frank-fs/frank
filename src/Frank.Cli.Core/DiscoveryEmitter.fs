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
    {
        Id: string
        Href: string option
        Rt: string option
        /// Full, un-relativized class IRI (Some for top-level class descriptors only).
        /// Runtime correlation key for HTTP-method reconciliation (#397) — never emitted
        /// as-is; carried through to AlpsDescriptor.ClassIri.
        ClassIri: string option
        /// Full CLR type name this class maps from (Some for top-level class descriptors
        /// only). Runtime correlation key (#397) — carried through to
        /// AlpsDescriptor.RequestClrTypeName.
        RequestClrTypeName: string option
        Children: ResolvedDescriptor list
    }

let private hrefOption (href: string) : string option =
    if String.IsNullOrEmpty href then None else Some href

/// Build a leaf field descriptor; returns None when the field has no IRI.
let private fieldDescriptor (bases: Set<string>) (f: ResolvedField) : ResolvedDescriptor option =
    f.Iri
    |> Option.map (fun uri ->
        let absolute = uri.AbsoluteUri

        { Id = localName absolute
          Href = hrefOption (EmitterShared.hrefFor bases absolute)
          Rt = None
          ClassIri = None
          RequestClrTypeName = None
          Children = [] })

/// Build a leaf case descriptor for a union case.
let private caseDescriptor (bases: Set<string>) (c: ResolvedCase) : ResolvedDescriptor =
    let absolute = c.Iri.AbsoluteUri

    { Id = c.CaseName
      Href = hrefOption (EmitterShared.hrefFor bases absolute)
      Rt = None
      ClassIri = None
      RequestClrTypeName = None
      Children = [] }

/// Build child descriptors for a resource: cases for DUs, field IRIs for records.
let private buildChildren (bases: Set<string>) (r: ResolvedResource) : ResolvedDescriptor list =
    if not (List.isEmpty r.Cases) then
        r.Cases |> List.map (caseDescriptor bases)
    else
        r.Fields |> List.choose (fieldDescriptor bases)

/// Collect all class-level descriptors. Each class descriptor carries its children:
/// - Union types: confirmed case descriptors (AC1 #17 — outcome terms discoverable).
/// - Record types: field descriptors with IRIs (AC1 #4 nesting).
/// Rt is the href of the declared return-type IRI, set explicitly in the lock file — a
/// genuine ALPS return-type link, never a proxy for HTTP-safety classification (#400 Fix
/// 2: "does this class declare an rt target" and "is this an unsafe/idempotent transition"
/// are orthogonal ALPS concerns; conflating them made a resource's declared linkage the
/// only lever available to control its codegen-time Type guess). The Type default itself
/// comes from alpsTypeDefault, computed independently of Rt; DiscoveryMiddleware reconciles
/// the real Type against the resource's actual registered HTTP method at serve time (#397),
/// using ClassIri/RequestClrTypeName as correlation keys.
let private collectDescriptors (bases: Set<string>) (resources: ResolvedResource list) : ResolvedDescriptor list =
    resources
    |> List.choose (fun r ->
        r.ClassIri
        |> Option.map (fun uri ->
            let absolute = uri.AbsoluteUri

            let rt =
                r.Rt |> Option.map (fun rtUri -> EmitterShared.hrefFor bases rtUri.AbsoluteUri)

            { Id = localName absolute
              Href = hrefOption (EmitterShared.hrefFor bases absolute)
              Rt = rt
              ClassIri = Some absolute
              RequestClrTypeName = Some r.FSharpType
              Children = buildChildren bases r }))

/// Collect unique `rel="type"` link values for resources that have a ClassIri, paired
/// with their full, un-relativized class IRI — DiscoveryMiddleware's correlation key for
/// scoping the served link to only the resource actually matched at serve time (#398),
/// instead of broadcasting every app resource's link on every OPTIONS response.
/// Declared-only prefix class IRIs are emitted as host-relative link targets.
let private collectDescribedByLinks (bases: Set<string>) (resources: ResolvedResource list) : (string * string) list =
    let folder (seen: Set<string>, acc: (string * string) list) (r: ResolvedResource) =
        match r.ClassIri with
        | None -> seen, acc
        | Some uri ->
            let fullIri = uri.AbsoluteUri

            if Set.contains fullIri seen then
                seen, acc
            else
                let linkIri = EmitterShared.hrefFor bases fullIri
                let link = $"<{linkIri}>; rel=\"type\""
                Set.add fullIri seen, (fullIri, link) :: acc

    let _, revLinks = List.fold folder (Set.empty, []) resources
    List.rev revLinks

// ── Pure projection ───────────────────────────────────────────────────────────

/// Recursively collect one field (per descriptor, top-level and nested children) across
/// a descriptor tree — the shared tree-walk shape behind collectAllIds/collectHrefs/collectRtValues.
let rec private collectField
    (extract: ResolvedDescriptor -> string list)
    (descriptors: ResolvedDescriptor list)
    : string list =
    descriptors
    |> List.collect (fun d -> extract d @ collectField extract d.Children)

/// Recursively collect all descriptor IDs (top-level and nested children).
let private collectAllIds: ResolvedDescriptor list -> string list =
    collectField (fun d -> [ d.Id ])

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

/// Recursively collect every descriptor's own Href (top-level and nested children).
let private collectHrefs: ResolvedDescriptor list -> string list =
    collectField (fun d -> d.Href |> Option.toList)

/// Recursively collect every resolvable target for `rt`: each descriptor's own Href and Id.
let private collectResolvableTargets (descriptors: ResolvedDescriptor list) : Set<string> =
    Set.ofList (collectAllIds descriptors @ collectHrefs descriptors)

/// Recursively collect every emitted `rt` value (top-level and nested children).
let private collectRtValues: ResolvedDescriptor list -> string list =
    collectField (fun d -> d.Rt |> Option.toList)

/// Assert that every emitted `rt` value resolves to some descriptor's href or id in the
/// same document (parallel to assertUniqueIds — a codegen-time ALPS structural invariant).
/// Throws invalidOp naming the unresolved rt value(s) — catches an `rt` that points at a
/// class never emitted as its own descriptor (e.g. Excluded), not just a malformed IRI.
let private assertRtResolves (descriptors: ResolvedDescriptor list) : unit =
    let resolvable = collectResolvableTargets descriptors

    let unresolved =
        collectRtValues descriptors
        |> List.filter (fun rt -> not (Set.contains rt resolvable))
        |> List.distinct

    if not unresolved.IsEmpty then
        let unresolvedStr = String.concat ", " unresolved

        invalidOp (
            sprintf
                "DiscoveryEmitter: unresolved ALPS 'rt' reference(s): %s. Every emitted rt must match a descriptor's href or id in the same document."
                unresolvedStr
        )

/// Pure projection: model → (descriptors, describedBy links). Testable typed output.
/// declaredOnlyBases: set of base URI strings whose IRIs should be emitted as host-relative paths.
let internal projectDiscovery
    (declaredOnlyBases: Set<string>)
    (model: ResolvedModel)
    : ResolvedDescriptor list * (string * string) list =
    collectDescriptors declaredOnlyBases model.Resources, collectDescribedByLinks declaredOnlyBases model.Resources

// ── Source rendering via AstRender (no string concat) ────────────────────────

/// Codegen-time ALPS Type fallback for any descriptor — always "semantic", deliberately
/// independent of Rt (#400 Fix 2). Codegen cannot see an app's real `resource { get/post/
/// ... }` registrations, so it never guesses "unsafe"/"idempotent"/"safe" from Rt presence
/// or any other declared-mapping signal; DiscoveryMiddleware reconciles the genuine Type
/// against the resource's actual registered HTTP method(s) at serve time (#397). Rt is
/// carried through this function's input unread — proof, not just claim, that the two are
/// independently computable.
let internal alpsTypeDefault (_d: ResolvedDescriptor) : string = "semantic"

let rec private descriptorExpr (d: ResolvedDescriptor) =
    AstRender.recordExpr
        [ "Id", AstRender.strExpr d.Id
          "Type", AstRender.strExpr (alpsTypeDefault d)
          "Doc", AstRender.noneExpr
          "Href", AstRender.optionExpr AstRender.strExpr d.Href
          "Descriptors", AstRender.listExpr (d.Children |> List.map descriptorExpr)
          "Rt", AstRender.optionExpr AstRender.strExpr d.Rt
          "ClassIri", AstRender.optionExpr AstRender.strExpr d.ClassIri
          "RequestClrTypeName", AstRender.optionExpr AstRender.strExpr d.RequestClrTypeName ]

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

/// Collect supplemental href-var entries from the Rt target (one hop), excluding own keys.
let private supplementalEntries
    (toFieldEntry: ResolvedField -> (string * string) option)
    (byClassIri: Map<string, ResolvedResource>)
    (ownKeys: Set<string>)
    (r: ResolvedResource)
    : (string * string) list =
    r.Rt
    |> Option.bind (fun rtUri -> Map.tryFind rtUri.AbsoluteUri byClassIri)
    |> Option.map (fun rtResource ->
        rtResource.Fields
        |> List.choose toFieldEntry
        |> List.filter (fun (k, _) -> not (Set.contains k ownKeys)))
    |> Option.defaultValue []

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
            let extra = supplementalEntries toFieldEntry byClassIri ownKeys r
            classIri.AbsoluteUri, ownEntries @ extra))
    |> List.filter (fun (_, entries) -> not entries.IsEmpty)

/// { ClassIri = "..."; Link = "..." } for one DescribedByLinks entry.
let private describedByLinkExpr ((classIri, link): string * string) =
    AstRender.recordExpr [ "ClassIri", AstRender.strExpr classIri; "Link", AstRender.strExpr link ]

let private configExpr
    (profileUri: string)
    (descriptors: ResolvedDescriptor list)
    (links: (string * string) list)
    (hrefVars: (string * (string * string) list) list)
    =
    AstRender.recordExpr
        [ "ProfileUri", AstRender.strExpr profileUri
          "HomeRoute", AstRender.strExpr "/"
          "AlpsDescriptors", AstRender.listExpr (descriptors |> List.map descriptorExpr)
          "DescribedByLinks", AstRender.listExpr (links |> List.map describedByLinkExpr)
          "ResourceHrefVars", resourceHrefVarsExpr hrefVars ]

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
        let bases = EmitterShared.declaredOnlyBases lock
        let descriptors, links = projectDiscovery bases model
        assertUniqueIds descriptors
        assertRtResolves descriptors
        let hrefVars = computeHrefVars bases model
        let value = configExpr profileUri descriptors links hrefVars

        AstRender.formatTypedValueModule
            moduleName
            (Some AstRender.autoGeneratedHeader)
            [ "Frank.Discovery" ]
            "discoveryConfig"
            "DiscoveryConfig"
            value)
