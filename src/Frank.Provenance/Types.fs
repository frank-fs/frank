namespace Frank.Provenance

open System

/// The type of agent that performed or triggered an activity.
[<RequireQualifiedAccess>]
type AgentType =
    /// A human user.
    | Person of name: string * identifier: string
    /// An automated software component.
    | SoftwareAgent of identifier: string
    /// A large language model or AI agent.
    | LlmAgent of identifier: string * model: string option

/// An agent responsible for an activity, mapped to prov:Agent.
type ProvenanceAgent =
    {
        /// Unique identifier for this agent.
        Id: string
        /// The type of agent.
        AgentType: AgentType
    }

/// An entity that was used or generated, mapped to prov:Entity.
type ProvenanceEntity =
    {
        /// Unique identifier for this entity.
        Id: string
        /// The URI of the resource this entity represents.
        ResourceUri: string
        /// The state name at the time of capture.
        StateName: string
        /// The time at which this entity was captured.
        CapturedAt: DateTimeOffset
    }

/// An activity that caused a state change, mapped to prov:Activity.
type ProvenanceActivity =
    {
        /// Unique identifier for this activity.
        Id: string
        /// The HTTP method used.
        HttpMethod: string
        /// The URI of the resource affected.
        ResourceUri: string
        /// The event that triggered the state change.
        EventName: string
        /// The state before the activity.
        PreviousState: string
        /// The state after the activity.
        NewState: string
        /// When the activity started.
        StartedAt: DateTimeOffset
        /// When the activity ended.
        EndedAt: DateTimeOffset
    }

/// A complete provenance record capturing who did what, when, and to what.
type ProvenanceRecord =
    {
        /// Unique identifier for this record.
        Id: string
        /// The URI of the resource this record is about.
        ResourceUri: string
        /// When this record was created.
        RecordedAt: DateTimeOffset
        /// The activity that occurred.
        Activity: ProvenanceActivity
        /// The agent who performed the activity.
        Agent: ProvenanceAgent
        /// The entity generated (output) by this activity.
        GeneratedEntity: ProvenanceEntity
        /// The entity used (input) by this activity.
        UsedEntity: ProvenanceEntity
    }

/// A collection of provenance records for a resource.
type ProvenanceGraph =
    {
        /// The URI of the resource.
        ResourceUri: string
        /// The provenance records for this resource.
        Records: ProvenanceRecord list
    }

/// Configuration for the provenance store.
type ProvenanceStoreConfig =
    {
        /// Maximum number of records to retain.
        MaxRecords: int
        /// Number of records to evict when the maximum is reached.
        EvictionBatchSize: int
    }

module ProvenanceStoreConfig =
    let defaults =
        { MaxRecords = 10_000
          EvictionBatchSize = 100 }
