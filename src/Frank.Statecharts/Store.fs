namespace Frank.Statecharts

open System
open System.Threading.Tasks

/// Abstraction for state machine instance persistence.
type IStateMachineStore<'State, 'Context when 'State: equality> =
    /// Retrieve the current state and context for an instance.
    /// Returns None if the instance doesn't exist yet.
    abstract GetState: instanceId: string -> Task<('State * 'Context) option>

    /// Persist a state change for an instance.
    abstract SetState: instanceId: string -> state: 'State -> context: 'Context -> Task<unit>

    /// Subscribe to state changes for an instance.
    /// Returns an IDisposable that unsubscribes when disposed.
    /// BehaviorSubject semantics: new subscribers immediately receive current state.
    abstract Subscribe: instanceId: string -> observer: IObserver<'State * 'Context> -> IDisposable
