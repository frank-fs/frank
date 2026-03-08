namespace Frank.Provenance

open System

/// The type of agent that performed or triggered an activity.
[<RequireQualifiedAccess>]
type AgentType =
    /// A human user.
    | Person
    /// An automated software component.
    | SoftwareAgent
    /// A large language model or AI agent.
    | LlmAgent

/// An agent responsible for an activity, mapped to prov:Agent.
type ProvenanceAgent =
    {
        /// Unique identifier for this agent.
        Id: Uri
        /// The type of agent.
        AgentType: AgentType
        /// Human-readable name for the agent.
        Name: string
        /// Optional agent that this agent acted on behalf of (delegation).
        ActedOnBehalfOf: Uri option
    }

/// An entity that was used or generated, mapped to prov:Entity.
type ProvenanceEntity =
    {
        /// Unique identifier for this entity.
        Id: Uri
        /// The time at which this entity version was generated.
        GeneratedAtTime: DateTimeOffset option
        /// The previous entity version this was derived from.
        WasDerivedFrom: Uri option
        /// The agent this entity is attributed to.
        WasAttributedTo: Uri option
        /// Optional location identifier.
        AtLocation: string option
    }

/// An activity that caused a state change, mapped to prov:Activity.
type ProvenanceActivity =
    {
        /// Unique identifier for this activity.
        Id: Uri
        /// When the activity started.
        StartedAtTime: DateTimeOffset
        /// When the activity ended.
        EndedAtTime: DateTimeOffset option
        /// The agent associated with this activity.
        WasAssociatedWith: Uri
        /// The entity that was used (input) by this activity.
        Used: Uri option
        /// Human-readable description of the activity.
        Description: string option
    }

/// A complete provenance record capturing who did what, when, and to what.
type ProvenanceRecord =
    {
        /// The activity that occurred.
        Activity: ProvenanceActivity
        /// The agent who performed the activity.
        Agent: ProvenanceAgent
        /// The entity generated (output) by this activity.
        GeneratedEntity: ProvenanceEntity
        /// The entity used (input) by this activity, if any.
        UsedEntity: ProvenanceEntity option
    }
