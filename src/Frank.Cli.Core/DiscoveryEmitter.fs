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

type private ResolvedDescriptor = { Id: string; Href: string }

/// Resolve the type-level descriptor for one ResolvedResource. Returns None if ClassIri is absent.
let private typeDescriptor (r: ResolvedResource) : ResolvedDescriptor option =
    r.ClassIri
    |> Option.map (fun uri ->
        let href = uri.AbsoluteUri

        { Id = localName href; Href = href })

/// Resolve field descriptors for one resource; skip fields with no Iri.
let private fieldDescriptors (fields: ResolvedField list) : ResolvedDescriptor list =
    fields
    |> List.choose (fun f ->
        f.Iri
        |> Option.map (fun uri ->
            let href = uri.AbsoluteUri

            { Id = localName href; Href = href }))

/// Collect all descriptors from all resources in dependency order: each type then its fields.
let private collectDescriptors (resources: ResolvedResource list) : ResolvedDescriptor list =
    resources
    |> List.collect (fun r ->
        let typeDs = typeDescriptor r |> Option.toList
        let fieldDs = fieldDescriptors r.Fields
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

// ── Source rendering ──────────────────────────────────────────────────────────

/// Escape a string for use as an F# string literal.
let private escapeString (s: string) : string =
    s.Replace("\\", "\\\\").Replace("\"", "\\\"")

/// Render one AlpsDescriptor record literal as a single line.
let private renderDescriptor (d: ResolvedDescriptor) : string =
    let href = "Some \"" + escapeString d.Href + "\""

    "{ Id = \""
    + escapeString d.Id
    + "\"; Type = \"semantic\"; Doc = None; Href = "
    + href
    + " }"

/// Render the AlpsDescriptors list literal (indented 8 spaces).
let private renderDescriptorList (descriptors: ResolvedDescriptor list) : string =
    match descriptors with
    | [] -> "        []"
    | d :: rest ->
        let first = "        [ " + renderDescriptor d
        let others = rest |> List.map (fun x -> "          " + renderDescriptor x)
        let all = first :: others
        (all |> String.concat "\n") + " ]"

/// Render the DescribedByLinks list literal (indented 8 spaces).
let private renderLinkList (links: string list) : string =
    match links with
    | [] -> "        []"
    | l :: rest ->
        let first = "        [ \"" + escapeString l + "\""
        let others = rest |> List.map (fun s -> "          \"" + escapeString s + "\"")
        let all = first :: others
        (all |> String.concat "\n") + " ]"

/// Assemble the full F# module source string.
let private assembleModule
    (moduleName: string)
    (profileUri: string)
    (descriptors: ResolvedDescriptor list)
    (links: string list)
    : string =
    let descriptorLines = renderDescriptorList descriptors
    let linkLines = renderLinkList links
    let escapedProfile = escapeString profileUri

    String.concat
        "\n"
        [ $"module {moduleName}"
          ""
          "open Frank.Discovery"
          ""
          "let discoveryConfig: DiscoveryConfig ="
          "    { ProfileUri = \"" + escapedProfile + "\""
          "      HomeRoute = \"/\""
          "      AlpsDescriptors ="
          descriptorLines
          "      DescribedByLinks ="
          linkLines + " }"
          "" ]

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
    if String.IsNullOrWhiteSpace moduleName then
        invalidArg (nameof moduleName) "moduleName must not be empty"

    if String.IsNullOrWhiteSpace profileUri then
        invalidArg (nameof profileUri) "profileUri must not be empty"

    match ResolvedModel.build registry lock with
    | Error e -> Error e
    | Ok model ->
        let descriptors = collectDescriptors model.Resources
        let links = collectDescribedByLinks model.Resources
        Ok(assembleModule moduleName profileUri descriptors links)
