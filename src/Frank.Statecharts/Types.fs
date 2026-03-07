namespace Frank.Statecharts

open System.Security.Claims

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

/// Context passed to guard predicates for evaluation.
type GuardContext<'State, 'Event, 'Context> =
    { User: ClaimsPrincipal
      CurrentState: 'State
      Event: 'Event
      Context: 'Context }

/// A named guard predicate.
type Guard<'State, 'Event, 'Context> =
    { Name: string
      Predicate: GuardContext<'State, 'Event, 'Context> -> GuardResult }

/// Metadata about a single state (HTTP configuration).
type StateInfo =
    { AllowedMethods: string list
      IsFinal: bool
      Description: string option }

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
