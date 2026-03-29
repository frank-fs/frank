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

/// Algebraic operations for GuardResult.
/// Monoid under conjunction via compose. alternative provides disjunction
/// (semigroup — no identity element). compose and alternative do NOT form
/// a distributive lattice.
[<RequireQualifiedAccess>]
module GuardResult =

    /// Identity element: always Allowed.
    let identity: GuardResult = Allowed

    /// Conjunction (AND): if the first blocks, short-circuit; otherwise return the second.
    let compose (a: GuardResult) (b: GuardResult) : GuardResult =
        match a with
        | Blocked _ -> a
        | Allowed -> b

    /// Disjunction (OR): if the first allows, short-circuit; otherwise return the second.
    let alternative (a: GuardResult) (b: GuardResult) : GuardResult =
        match a with
        | Allowed -> a
        | Blocked _ -> b

/// Context for access-control guards (pre-handler). No event available.
[<NoEquality; NoComparison>]
type AccessControlContext<'State, 'Context> =
    { User: ClaimsPrincipal
      CurrentState: 'State
      Context: 'Context
      Roles: Set<string> }

    member this.HasRole(roleName: string) = this.Roles.Contains(roleName)

/// Context for event-validation guards (post-handler). Event is the actual value set by the handler.
[<NoEquality; NoComparison>]
type EventValidationContext<'State, 'Event, 'Context> =
    { User: ClaimsPrincipal
      CurrentState: 'State
      Event: 'Event
      Context: 'Context
      Roles: Set<string> }

    member this.HasRole(roleName: string) = this.Roles.Contains(roleName)

/// Named role with identity-matching predicate.
/// Per-resource, not global. The predicate is the source of truth for runtime evaluation.
[<NoEquality; NoComparison>]
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

/// Algebraic operations for TransitionResult (bifunctor map, Kleisli-style bind over state*context product).
[<RequireQualifiedAccess>]
module TransitionResult =

    /// Lift a state and context into a successful TransitionResult.
    let pure' (state: 'State) (context: 'Context) : TransitionResult<'State, 'Context> =
        TransitionResult.Transitioned(state, context)

    /// Bifunctor map over ('State, 'Context): apply functions to state and context
    /// if Transitioned; pass through Blocked and Invalid unchanged.
    let map
        (fState: 'State1 -> 'State2)
        (fContext: 'Context1 -> 'Context2)
        (result: TransitionResult<'State1, 'Context1>)
        : TransitionResult<'State2, 'Context2> =
        match result with
        | TransitionResult.Transitioned(state, context) -> TransitionResult.Transitioned(fState state, fContext context)
        | TransitionResult.Blocked reason -> TransitionResult.Blocked reason
        | TransitionResult.Invalid message -> TransitionResult.Invalid message

    /// Kleisli-style bind over ('State * 'Context) product, presented in curried form:
    /// apply function to state and context if Transitioned; short-circuit on Blocked and Invalid.
    let bind
        (f: 'State -> 'Context -> TransitionResult<'State2, 'Context2>)
        (result: TransitionResult<'State, 'Context>)
        : TransitionResult<'State2, 'Context2> =
        match result with
        | TransitionResult.Transitioned(state, context) -> f state context
        | TransitionResult.Blocked reason -> TransitionResult.Blocked reason
        | TransitionResult.Invalid message -> TransitionResult.Invalid message

/// Compile-time definition of a state machine.
type StateMachine<'State, 'Event, 'Context when 'State: equality and 'State: comparison> =
    { Initial: 'State
      InitialContext: 'Context
      Transition: 'State -> 'Event -> 'Context -> TransitionResult<'State, 'Context>
      Guards: Guard<'State, 'Event, 'Context> list
      StateMetadata: Map<'State, StateInfo> }
