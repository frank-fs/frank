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
    val toJson: profile: Descriptor list -> string
