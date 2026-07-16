namespace Frank.Provenance

open Frank.Builder

[<AutoOpen>]
module ProvenanceExtensions =

    type WebHostBuilder with

        [<CustomOperation("useProvenanceWith")>]
        member UseProvenanceWith: spec: WebHostSpec * config: ProvenanceConfig -> WebHostSpec

        [<CustomOperation("useProvenance")>]
        member UseProvenance: spec: WebHostSpec -> WebHostSpec
