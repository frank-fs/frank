namespace Frank.Statecharts

open Frank.Builder

[<AutoOpen>]
module ResourceBuilderExtensions =
    type ResourceBuilder with

        /// Attach a state machine metadata marker to a standard resource.
        /// Use this for simple metadata-only annotation when the full
        /// statefulResource CE is not needed.
        [<CustomOperation("stateMachine")>]
        member _.StateMachine(spec: ResourceSpec, metadata: StateMachineMetadata) : ResourceSpec =
            ResourceBuilder.AddMetadata(spec, fun builder -> builder.Metadata.Add(metadata))
