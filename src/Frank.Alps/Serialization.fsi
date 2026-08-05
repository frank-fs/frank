namespace Frank.Alps

[<AutoOpen>]
module SerializationExt =
    /// Canonical ext ids under https://frank-fs.github.io/alps-ext/, from PR #165/#214 -- unchanged from
    /// the rolled-back v7.3.0 line's shipped generator output, continued here for wire-format continuity.
    [<Literal>]
    val ProtocolStateExtId: string = "https://frank-fs.github.io/alps-ext/protocolState"

    [<Literal>]
    val AvailableInStatesExtId: string = "https://frank-fs.github.io/alps-ext/availableInStates"

module Serialization =
    /// Serializes a profile (the same `Descriptor list` passed to `useAlps`, or any subset for the
    /// per-resource excerpt) as draft-07 JSON: `{ "alps": { "version": "1.0", "descriptor": [...] } }`.
    ///
    /// Cross-descriptor references resolve in two cases:
    /// - Local references: if the referenced descriptor is present in `profile`, emits a same-document
    ///   `#id` fragment (the scope of the served document).
    /// - Cross-document fallback: if the referenced descriptor is absent from `profile` (filtered out
    ///   in an excerpt or role-pruned view), emits `rootUri#id` to link the reader to the full document
    ///   at `rootUri`.
    /// External references (URIs) are always serialized verbatim, unchanged.
    val toJson: rootUri: System.Uri -> profile: Descriptor list -> string
