module Frank.Cli.Core.DiscoveryEmitter

open System
open Frank.Semantic
open Frank.Semantic.LockFile

// ── IRI resolution ────────────────────────────────────────────────────────────

/// Resolve an optional prefixed IRI to its full string form.
/// Returns Error if the IRI is prefixed and the prefix is not in the registry.
/// Returns Ok None if iriOpt is None.
let private resolveOptional (prefixes: Map<string, Uri>) (iriOpt: string option) : Result<string option, string> =
    match iriOpt with
    | None -> Ok None
    | Some iri ->
        match iri.IndexOf(':') with
        | -1 -> Ok(Some iri)
        | idx ->
            let prefix = iri.[.. idx - 1]
            let local = iri.[idx + 1 ..]

            match Map.tryFind prefix prefixes with
            | None -> Error $"Unknown prefix '{prefix}' in IRI '{iri}'"
            | Some baseUri -> Ok(Some(baseUri.AbsoluteUri + local))

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

/// Resolve the type-level descriptor for one Mapping. Returns None if Iri is absent.
let private typeDescriptor (prefixes: Map<string, Uri>) (m: Mapping) : Result<ResolvedDescriptor option, string> =
    match resolveOptional prefixes m.Iri with
    | Error e -> Error $"type '{m.FSharpType}': {e}"
    | Ok None -> Ok None
    | Ok(Some fullIri) ->
        Ok(
            Some
                { Id = localName fullIri
                  Href = fullIri }
        )

/// Resolve field descriptors for one Mapping; skip fields with no Iri.
let private fieldDescriptors
    (prefixes: Map<string, Uri>)
    (typeName: string)
    (fields: FieldMapping list)
    : Result<ResolvedDescriptor list, string> =
    let rec loop (remaining: FieldMapping list) (acc: ResolvedDescriptor list) =
        match remaining with
        | [] -> Ok(List.rev acc)
        | f :: rest ->
            match resolveOptional prefixes f.Iri with
            | Error e -> Error $"field '{typeName}.{f.Name}': {e}"
            | Ok None -> loop rest acc
            | Ok(Some fullIri) ->
                let d =
                    { Id = localName fullIri
                      Href = fullIri }

                loop rest (d :: acc)

    loop fields []

/// Collect all descriptors from all mappings in dependency order: each type then its fields.
let private collectDescriptors
    (prefixes: Map<string, Uri>)
    (mappings: Mapping list)
    : Result<ResolvedDescriptor list, string> =
    let rec loop (remaining: Mapping list) (acc: ResolvedDescriptor list) =
        match remaining with
        | [] -> Ok(List.rev acc)
        | m :: rest ->
            match typeDescriptor prefixes m with
            | Error e -> Error e
            | Ok typeDOpt ->
                match fieldDescriptors prefixes m.FSharpType m.Fields with
                | Error e -> Error e
                | Ok fieldDs ->
                    let typeDs = typeDOpt |> Option.toList
                    loop rest (List.rev (fieldDs @ typeDs) @ acc)

    loop mappings []

/// Collect unique `rel="type"` link values for types that have an Iri.
/// Uses rel="type" (RFC 6903) rather than rel="describedby" so the local
/// ALPS profile retains the canonical describedby slot on OPTIONS responses.
let private collectDescribedByLinks
    (prefixes: Map<string, Uri>)
    (mappings: Mapping list)
    : Result<string list, string> =
    let rec loop (remaining: Mapping list) (seen: Set<string>) (acc: string list) =
        match remaining with
        | [] -> Ok(List.rev acc)
        | m :: rest ->
            match resolveOptional prefixes m.Iri with
            | Error e -> Error $"type link for '{m.FSharpType}': {e}"
            | Ok None -> loop rest seen acc
            | Ok(Some fullIri) ->
                if Set.contains fullIri seen then
                    loop rest seen acc
                else
                    let link = $"<{fullIri}>; rel=\"type\""
                    loop rest (Set.add fullIri seen) (link :: acc)

    loop mappings Set.empty []

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

    let prefixes = registry.Prefixes

    match collectDescriptors prefixes lock.Mappings with
    | Error e -> Error e
    | Ok descriptors ->
        match collectDescribedByLinks prefixes lock.Mappings with
        | Error e -> Error e
        | Ok links -> Ok(assembleModule moduleName profileUri descriptors links)
