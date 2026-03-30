namespace Frank.Affordances

open System
open System.Collections.Generic
open System.IO
open System.Text
open System.Text.Json
open Frank.Resources.Model
open Frank.Statecharts.Dual

/// Pre-computed entry for a single (route, state, role) dual profile.
/// Contains both the ALPS JSON content and the pre-formatted Link header value.
type DualProfileEntry =
    {
        /// ALPS JSON string with duality annotations for serving content.
        AlpsJson: string
        /// Pre-formatted RFC 8288 Link header value (e.g., '<https://example.com/alps/orders-seller-Submitted-dual>; rel="profile"').
        /// Built at startup so the middleware writes a cached string with zero per-request allocation.
        LinkHeaderValue: string
    }

/// Pre-computed dual profile lookup: route template -> state -> role -> DualProfileEntry.
/// Outer key: route template (e.g., "/orders/{orderId}")
/// Middle key: state name (e.g., "Submitted")
/// Inner key: role name (case-insensitive via OrdinalIgnoreCase comparer)
/// Value: DualProfileEntry with ALPS JSON and pre-computed Link header value
type DualProfileLookup = Dictionary<string, Dictionary<string, Dictionary<string, DualProfileEntry>>>

/// Parsing for RFC 7240 Prefer header values.
module PreferHeader =

    /// Check whether a Prefer header value contains the "return=dual" preference.
    /// Parsing follows RFC 7240: preferences are comma-separated tokens.
    /// Case-insensitive matching per RFC 7240 Section 2.
    let hasReturnDual (preferValue: string) : bool =
        if String.IsNullOrEmpty(preferValue) then
            false
        else
            preferValue.Split(',', StringSplitOptions.RemoveEmptyEntries ||| StringSplitOptions.TrimEntries)
            |> Array.exists (fun token -> token.Equals("return=dual", StringComparison.OrdinalIgnoreCase))

/// Generate a minimal ALPS JSON document with duality annotations for a (role, state) pair.
/// This produces a self-contained ALPS profile annotated with clientObligation,
/// advancesProtocol, dualOf, and cutPoint extensions from the DeriveResult.
module DualAlpsGenerator =

    /// Map ClientObligation to its string representation for ALPS extension values.
    let private obligationToString (obligation: ClientObligation) : string =
        match obligation with
        | MustSelect -> "must-select"
        | MayPoll -> "may-poll"
        | SessionComplete -> "session-complete"

    /// Generate a dual-annotated ALPS JSON for a specific (role, state) pair.
    /// The document contains the role's available descriptors in the given state,
    /// each annotated with their client obligation and protocol advancement status.
    let generate
        (annotations: DualAnnotation list)
        (resourceSlug: string)
        (roleName: string)
        (stateName: string)
        (baseUri: string)
        : string =
        use stream = new MemoryStream()
        use writer = new Utf8JsonWriter(stream, JsonWriterOptions(Indented = true))

        writer.WriteStartObject()
        writer.WritePropertyName("alps")
        writer.WriteStartObject()

        writer.WriteString("version", "1.0")

        // Document-level doc
        writer.WritePropertyName("doc")
        writer.WriteStartObject()
        writer.WriteString("format", "text")

        writer.WriteString("value", sprintf "Client dual for role %s in state %s of %s" roleName stateName resourceSlug)

        writer.WriteEndObject()

        // Document-level extensions: projectedRole and protocolState
        writer.WritePropertyName("ext")
        writer.WriteStartArray()

        writer.WriteStartObject()
        writer.WriteString("id", "https://frank-fs.github.io/alps-ext/projectedRole")
        writer.WriteString("value", roleName)
        writer.WriteEndObject()

        writer.WriteStartObject()
        writer.WriteString("id", "https://frank-fs.github.io/alps-ext/protocolState")
        writer.WriteString("value", stateName)
        writer.WriteEndObject()

        writer.WriteEndArray()

        // Descriptor array: one descriptor per DualAnnotation
        if not (List.isEmpty annotations) then
            writer.WritePropertyName("descriptor")
            writer.WriteStartArray()

            for ann in annotations do
                writer.WriteStartObject()
                writer.WriteString("id", ann.Descriptor)
                writer.WriteString("type", "semantic")

                // Extensions for duality annotations
                writer.WritePropertyName("ext")
                writer.WriteStartArray()

                // clientObligation
                writer.WriteStartObject()
                writer.WriteString("id", "https://frank-fs.github.io/alps-ext/clientObligation")
                writer.WriteString("value", obligationToString ann.Obligation)
                writer.WriteEndObject()

                // advancesProtocol
                writer.WriteStartObject()
                writer.WriteString("id", "https://frank-fs.github.io/alps-ext/advancesProtocol")
                writer.WriteString("value", (if ann.AdvancesProtocol then "true" else "false"))
                writer.WriteEndObject()

                // dualOf (optional)
                ann.DualOf
                |> Option.iter (fun dualOf ->
                    writer.WriteStartObject()
                    writer.WriteString("id", "https://frank-fs.github.io/alps-ext/dualOf")
                    writer.WriteString("value", dualOf)
                    writer.WriteEndObject())

                // cutPoint (optional)
                ann.CutPoint
                |> Option.iter (fun cutPoint ->
                    writer.WriteStartObject()
                    writer.WriteString("id", "https://frank-fs.github.io/alps-ext/cutPoint")

                    let cutPointStr =
                        match cutPoint with
                        | Opaque s -> s
                        | Enriched cp -> sprintf "%s@%s" cp.TargetUriTemplate cp.AuthorityBoundary

                    writer.WriteString("value", cutPointStr)
                    writer.WriteEndObject())

                // choiceGroupId (optional, for external choice semantics)
                ann.ChoiceGroupId
                |> Option.iter (fun groupId ->
                    writer.WriteStartObject()
                    writer.WriteString("id", "https://frank-fs.github.io/alps-ext/choiceGroup")
                    writer.WriteString("value", string groupId)
                    writer.WriteEndObject())

                writer.WriteEndArray()
                writer.WriteEndObject()

            writer.WriteEndArray()

        // Document-level link: self reference
        writer.WritePropertyName("link")
        writer.WriteStartArray()

        writer.WriteStartObject()
        writer.WriteString("rel", "self")

        let encodedRole = Uri.EscapeDataString(roleName.ToLowerInvariant())
        let encodedState = Uri.EscapeDataString(stateName)

        writer.WriteString(
            "href",
            sprintf "%s/%s-%s-%s-dual" baseUri resourceSlug encodedRole encodedState
        )

        writer.WriteEndObject()
        writer.WriteEndArray()

        writer.WriteEndObject()
        writer.WriteEndObject()
        writer.Flush()

        Encoding.UTF8.GetString(stream.ToArray())

/// Build pre-computed dual profile data from extracted statecharts.
module DualProfileOverlay =

    /// Build a DualProfileLookup from a single ExtractedStatechart.
    /// Derives client duals via Dual.derive and generates ALPS JSON for each (role, state) pair.
    /// Pre-computes Link header values at startup for zero per-request allocation.
    /// baseUri must be a well-formed absolute URI (e.g., "https://example.com/alps").
    let buildFromStatechart (chart: ExtractedStatechart) (resourceSlug: string) (baseUri: string) : DualProfileLookup =
        let lookup = DualProfileLookup(StringComparer.Ordinal)

        // Validate baseUri is a well-formed absolute URI
        match Uri.TryCreate(baseUri, UriKind.Absolute) with
        | false, _ -> invalidArg (nameof baseUri) $"baseUri must be a well-formed absolute URI, got: '%s{baseUri}'"
        | _ -> ()

        if chart.Roles.IsEmpty then
            lookup
        else
            let projections = Projection.projectAll chart
            let deriveResult = derive chart projections

            let stateDict =
                Dictionary<string, Dictionary<string, DualProfileEntry>>(StringComparer.Ordinal)

            for (roleName, stateName), annotations in deriveResult.Annotations |> Map.toSeq do
                if not (List.isEmpty annotations) then
                    let alpsJson =
                        DualAlpsGenerator.generate annotations resourceSlug roleName stateName baseUri

                    let roleLower = roleName.ToLowerInvariant()
                    let dualSlug = sprintf "%s-%s-%s-dual" resourceSlug roleLower stateName
                    let profileUrl = AffordanceMap.profileUrl baseUri dualSlug
                    let linkHeaderValue = AffordancePreCompute.formatLinkValue profileUrl "profile"

                    let entry =
                        { AlpsJson = alpsJson
                          LinkHeaderValue = linkHeaderValue }

                    match stateDict.TryGetValue(stateName) with
                    | true, roleDict -> roleDict.[roleName] <- entry
                    | false, _ ->
                        let roleDict =
                            Dictionary<string, DualProfileEntry>(StringComparer.OrdinalIgnoreCase)

                        roleDict.[roleName] <- entry
                        stateDict.[stateName] <- roleDict

            if stateDict.Count > 0 then
                lookup.[chart.RouteTemplate] <- stateDict

            lookup

    /// Build a DualProfileLookup from a list of UnifiedResources.
    /// Merges dual profiles from all resources that have statecharts with roles.
    let buildFromRuntimeState (resources: UnifiedResource list) (baseUri: string) : DualProfileLookup =
        let merged = DualProfileLookup(StringComparer.Ordinal)

        for resource in resources do
            match resource.Statechart with
            | Some chart when not chart.Roles.IsEmpty ->
                let perResource = buildFromStatechart chart resource.ResourceSlug baseUri

                for kv in perResource do
                    merged.[kv.Key] <- kv.Value
            | _ -> ()

        merged
