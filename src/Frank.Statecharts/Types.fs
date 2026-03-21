namespace Frank.Statecharts

open System.Security.Claims
open Frank.Resources.Model

/// Why a guard blocked a transition. Maps to HTTP status codes in middleware.
[<Struct>]
type BlockReason =
    | NotAllowed
    | NotYourTurn
    | InvalidTransition
    | PreconditionFailed
    | Custom of code: int * message: string

/// Result of evaluating a guard predicate.
[<Struct>]
type GuardResult =
    | Allowed
    | Blocked of reason: BlockReason

/// Context for access-control guards (pre-handler). No event available.
type AccessControlContext<'State, 'Context> =
    { User: ClaimsPrincipal
      CurrentState: 'State
      Context: 'Context
      Roles: Set<string> }

    member this.HasRole(roleName: string) = this.Roles.Contains(roleName)

/// Context for event-validation guards (post-handler). Event is the actual value set by the handler.
type EventValidationContext<'State, 'Event, 'Context> =
    { User: ClaimsPrincipal
      CurrentState: 'State
      Event: 'Event
      Context: 'Context
      Roles: Set<string> }

    member this.HasRole(roleName: string) = this.Roles.Contains(roleName)

/// Named role with identity-matching predicate.
/// Per-resource, not global. The predicate is the source of truth for runtime evaluation.
type RoleDefinition =
    { Name: string
      ClaimsPredicate: ClaimsPrincipal -> bool }

/// A guard that controls access to state transitions.
/// The DU case determines both execution phase and type signature.
type Guard<'State, 'Event, 'Context> =
    /// Runs pre-handler. Cannot access the event (AccessControlContext has no Event field).
    | AccessControl of name: string * predicate: (AccessControlContext<'State, 'Context> -> GuardResult)
    /// Runs post-handler. Receives the actual event set by the handler.
    | EventValidation of name: string * predicate: (EventValidationContext<'State, 'Event, 'Context> -> GuardResult)

/// The outcome of a transition attempt.
[<RequireQualifiedAccess>]
type TransitionResult<'State, 'Context> =
    | Transitioned of state: 'State * context: 'Context
    | Blocked of reason: BlockReason
    | Invalid of message: string

/// Compile-time definition of a state machine.
type StateMachine<'State, 'Event, 'Context when 'State: equality and 'State: comparison> =
    { Initial: 'State
      InitialContext: 'Context
      Transition: 'State -> 'Event -> 'Context -> TransitionResult<'State, 'Context>
      Guards: Guard<'State, 'Event, 'Context> list
      StateMetadata: Map<'State, StateInfo> }
