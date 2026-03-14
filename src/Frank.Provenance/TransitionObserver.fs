namespace Frank.Provenance

open System
open System.Security.Claims
open Microsoft.Extensions.Logging

/// An event representing a state transition in a statechart-managed resource.
type TransitionEvent =
    {
        /// Unique identifier for the statechart instance.
        InstanceId: string
        /// The URI of the resource undergoing the transition.
        ResourceUri: string
        /// The state before the transition.
        PreviousState: string
        /// The state after the transition.
        NewState: string
        /// The event that triggered the transition.
        Event: string
        /// When the transition occurred.
        Timestamp: DateTimeOffset
        /// The user who triggered the transition, if any.
        User: ClaimsPrincipal option
        /// The HTTP method that triggered the transition.
        HttpMethod: string
        /// HTTP headers from the triggering request.
        Headers: Map<string, string>
    }

/// Private module for extracting a ProvenanceAgent from request context.
[<AutoOpen>]
module private AgentExtraction =

    let private tryGetClaim (claimType: string) (principal: ClaimsPrincipal) =
        match principal.FindFirst(claimType) with
        | null -> None
        | claim ->
            match claim.Value with
            | null
            | "" -> None
            | v -> Some v

    let private systemAgent () =
        { ProvenanceAgent.Id = "urn:frank:agent:system"
          AgentType = AgentType.SoftwareAgent("system") }

    let extractAgent (user: ClaimsPrincipal option) (headers: Map<string, string>) =
        match user with
        | None -> systemAgent ()
        | Some principal when principal.Identity = null || not principal.Identity.IsAuthenticated -> systemAgent ()
        | Some principal ->
            match headers |> Map.tryFind "X-Agent-Type" with
            | Some agentType when agentType.Equals("llm", StringComparison.OrdinalIgnoreCase) ->
                let identifier =
                    principal
                    |> tryGetClaim ClaimTypes.NameIdentifier
                    |> Option.defaultValue "unknown"

                let model = headers |> Map.tryFind "X-Agent-Model"

                { ProvenanceAgent.Id = $"urn:frank:agent:llm:{identifier}"
                  AgentType = AgentType.LlmAgent(identifier, model) }
            | _ ->
                let name = principal |> tryGetClaim ClaimTypes.Name |> Option.defaultValue "unknown"

                let identifier =
                    principal
                    |> tryGetClaim ClaimTypes.NameIdentifier
                    |> Option.defaultValue "unknown"

                { ProvenanceAgent.Id = $"urn:frank:agent:person:{identifier}"
                  AgentType = AgentType.Person(name, identifier) }

/// An IObserver that receives TransitionEvents and creates PROV-O provenance records.
type TransitionObserver(store: IProvenanceStore, logger: ILogger<TransitionObserver>) =

    let createRecord (event: TransitionEvent) =
        let agent = extractAgent event.User event.Headers

        let activityId = $"urn:frank:activity:{Guid.NewGuid()}"
        let usedEntityId = $"urn:frank:entity:{Guid.NewGuid()}"
        let generatedEntityId = $"urn:frank:entity:{Guid.NewGuid()}"
        let recordId = $"urn:frank:record:{Guid.NewGuid()}"

        let activity =
            { ProvenanceActivity.Id = activityId
              HttpMethod = event.HttpMethod
              ResourceUri = event.ResourceUri
              EventName = event.Event
              PreviousState = event.PreviousState
              NewState = event.NewState
              StartedAt = event.Timestamp
              EndedAt = event.Timestamp }

        let usedEntity =
            { ProvenanceEntity.Id = usedEntityId
              ResourceUri = event.ResourceUri
              StateName = event.PreviousState
              CapturedAt = event.Timestamp }

        let generatedEntity =
            { ProvenanceEntity.Id = generatedEntityId
              ResourceUri = event.ResourceUri
              StateName = event.NewState
              CapturedAt = event.Timestamp }

        { ProvenanceRecord.Id = recordId
          ResourceUri = event.ResourceUri
          RecordedAt = event.Timestamp
          Activity = activity
          Agent = agent
          GeneratedEntity = generatedEntity
          UsedEntity = usedEntity }

    interface IObserver<TransitionEvent> with

        member _.OnNext(event) =
            logger.LogDebug(
                "Received transition event: {InstanceId} {PreviousState} -> {NewState} via {Event}",
                event.InstanceId,
                event.PreviousState,
                event.NewState,
                event.Event
            )

            try
                let record = createRecord event
                store.Append(record)

                logger.LogInformation(
                    "Provenance record {RecordId} created for {ResourceUri}: {PreviousState} -> {NewState}",
                    record.Id,
                    record.ResourceUri,
                    event.PreviousState,
                    event.NewState
                )
            with
            | :? ObjectDisposedException as ex ->
                logger.LogWarning(
                    ex,
                    "Provenance store disposed while processing transition for {ResourceUri}",
                    event.ResourceUri
                )
            | ex ->
                logger.LogError(
                    ex,
                    "Failed to create provenance record for transition on {ResourceUri}",
                    event.ResourceUri
                )

        member _.OnError(error) =
            logger.LogWarning(error, "Transition event stream reported an error")

        member _.OnCompleted() =
            logger.LogInformation("Transition event stream completed")
