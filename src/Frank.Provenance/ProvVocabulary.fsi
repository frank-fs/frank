namespace Frank.Provenance

[<RequireQualifiedAccess>]
module ProvVocabulary =

    val Namespace: string

    module Class =
        val Activity: string
        val Entity: string
        val Agent: string

    module Property =
        val WasGeneratedBy: string
        val WasAssociatedWith: string
        val Used: string
        val StartedAtTime: string
        val EndedAtTime: string
        val WasDerivedFrom: string
        val SpecializationOf: string

    module Rdf =
        val Type: string

    module Xsd =
        val DateTime: string
        val Integer: string

    module Http =
        val Namespace: string
        val MethodName: string
        val StatusCodeValue: string
