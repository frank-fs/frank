namespace Frank.Statecharts

open System
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

/// <summary>
/// A unified snapshot of all persisted state for a statechart instance.
/// Bundles the flat state value, context, active hierarchy configuration, and history record
/// into a single value that is atomically loaded and saved.
/// </summary>
type InstanceSnapshot<'State, 'Context> =
    { /// The current flat state value (e.g., DU case).
      State: 'State
      /// The current context value associated with the state.
      Context: 'Context
      /// The active hierarchy configuration (set of currently active state IDs).
      HierarchyConfig: ActiveStateConfiguration
      /// The history record for composite states (used by history pseudo-states).
      HistoryRecord: HistoryRecord }

/// <summary>
/// Abstraction for statechart instance persistence.
/// </summary>
/// <remarks>
/// <para>
/// All implementations MUST serialize state access through an actor (e.g., <c>MailboxProcessor</c>).
/// This ensures concurrent requests to the same instance are processed sequentially,
/// preventing lost updates without requiring optimistic concurrency tokens.
/// </para>
/// <para>
/// The actor is the sole accessor of the backing store. External code never reads or writes
/// the backing store directly -- all operations go through <c>Load</c>/<c>Save</c>.
/// </para>
/// <para>
/// For durable implementations (e.g., SQLite), persistence operations occur inside the actor loop.
/// The actor wraps the backing store, not the other way around.
/// </para>
/// </remarks>
type IStatechartsStore<'State, 'Context when 'State: equality> =
    /// <summary>
    /// Retrieve the current snapshot for an instance.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the statechart instance.</param>
    /// <returns>The current snapshot, or <c>None</c> if the instance does not exist.</returns>
    /// <remarks>
    /// This operation is serialized through the actor. Concurrent calls to <c>Load</c>
    /// are queued and processed one at a time, ensuring consistent reads.
    /// </remarks>
    abstract Load: instanceId: string -> Task<InstanceSnapshot<'State, 'Context> option>

    /// <summary>
    /// Persist a snapshot for an instance.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the statechart instance.</param>
    /// <param name="snapshot">The new snapshot value to persist.</param>
    /// <remarks>
    /// This operation is serialized through the actor. Concurrent calls to <c>Save</c>
    /// for the same instance are queued and applied sequentially, guaranteeing no lost updates.
    /// Subscribers are notified after each save within the actor loop.
    /// </remarks>
    abstract Save: instanceId: string -> snapshot: InstanceSnapshot<'State, 'Context> -> Task<unit>

    /// <summary>
    /// Subscribe to snapshot changes for an instance.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the statechart instance.</param>
    /// <param name="observer">The observer to receive snapshot change notifications.</param>
    /// <returns>An <see cref="IDisposable"/> that unsubscribes when disposed.</returns>
    /// <remarks>
    /// BehaviorSubject semantics: new subscribers immediately receive the current snapshot if it exists.
    /// Subscription management is serialized through the actor alongside state operations.
    /// </remarks>
    abstract Subscribe:
        instanceId: string -> observer: IObserver<InstanceSnapshot<'State, 'Context>> -> IDisposable

/// <summary>
/// Internal message type for the <c>MailboxProcessor</c> actor loop.
/// </summary>
/// <remarks>
/// These messages are private to the store implementation. All state operations
/// are encoded as messages and processed sequentially by the actor, ensuring
/// thread-safe access to the in-memory state map and subscriber list.
/// </remarks>
type private StoreMessage<'State, 'Context when 'State: equality> =
    | Load of instanceId: string * replyChannel: AsyncReplyChannel<InstanceSnapshot<'State, 'Context> option>
    | Save of instanceId: string * snapshot: InstanceSnapshot<'State, 'Context> * replyChannel: AsyncReplyChannel<unit>
    | Subscribe of
        instanceId: string *
        observer: IObserver<InstanceSnapshot<'State, 'Context>> *
        replyChannel: AsyncReplyChannel<IDisposable>
    | Unsubscribe of instanceId: string * observer: IObserver<InstanceSnapshot<'State, 'Context>>
    | Stop of replyChannel: AsyncReplyChannel<unit>

/// <summary>
/// In-memory <see cref="IStatechartsStore{TState, TContext}"/> backed by a <c>MailboxProcessor</c>.
/// </summary>
/// <remarks>
/// <para>
/// All state operations are serialized through the <c>MailboxProcessor</c> agent.
/// Concurrent requests are queued and processed sequentially.
/// </para>
/// <para>
/// <strong>Known limitation</strong>: The <c>MailboxProcessor</c> message queue is unbounded.
/// Under extreme load, the queue can grow without limit. In practice, the HTTP server
/// (Kestrel) provides implicit backpressure via connection limits.
/// Monitor <c>CurrentQueueLength</c> in production for queue depth visibility.
/// For bounded queue behavior, consider a custom store implementation with
/// <c>inbox.CurrentQueueLength</c> checks.
/// </para>
/// </remarks>
type MailboxProcessorStore<'State, 'Context when 'State: equality>
    (logger: ILogger<MailboxProcessorStore<'State, 'Context>>) =

    let mutable disposed = false

    let agent =
        MailboxProcessor<StoreMessage<'State, 'Context>>.Start(fun inbox ->
            let mutable instances = Map.empty<string, InstanceSnapshot<'State, 'Context>>
            let mutable subscribers = Map.empty<string, IObserver<InstanceSnapshot<'State, 'Context>> list>

            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Load(id, reply) ->
                        reply.Reply(Map.tryFind id instances)
                        return! loop ()

                    | Save(id, snapshot, reply) ->
                        instances <- Map.add id snapshot instances

                        match Map.tryFind id subscribers with
                        | Some observers ->
                            for obs in observers do
                                try
                                    obs.OnNext(snapshot)
                                with ex ->
                                    logger.LogWarning(
                                        ex,
                                        "Store subscriber OnNext threw for instance {InstanceId}",
                                        id
                                    )
                        | None -> ()

                        reply.Reply(())
                        return! loop ()

                    | Subscribe(id, observer, reply) ->
                        let current = Map.tryFind id subscribers |> Option.defaultValue []

                        subscribers <- Map.add id (observer :: current) subscribers

                        match Map.tryFind id instances with
                        | Some snapshot ->
                            try
                                observer.OnNext(snapshot)
                            with ex ->
                                logger.LogWarning(
                                    ex,
                                    "Store subscriber OnNext threw during initial emit for instance {InstanceId}",
                                    id
                                )
                        | None -> ()

                        let disposable =
                            { new IDisposable with
                                member _.Dispose() = inbox.Post(Unsubscribe(id, observer)) }

                        reply.Reply(disposable)
                        return! loop ()

                    | Unsubscribe(id, observer) ->
                        match Map.tryFind id subscribers with
                        | Some observers ->
                            let filtered =
                                observers |> List.filter (fun o -> not (obj.ReferenceEquals(o, observer)))

                            subscribers <- Map.add id filtered subscribers
                        | None -> ()

                        return! loop ()

                    | Stop reply ->
                        for KeyValue(id, observers) in subscribers do
                            for obs in observers do
                                try
                                    obs.OnCompleted()
                                with ex ->
                                    logger.LogWarning(
                                        ex,
                                        "Store subscriber OnCompleted threw for instance {InstanceId}",
                                        id
                                    )

                        subscribers <- Map.empty
                        instances <- Map.empty
                        reply.Reply(())
                }

            loop ())

    let ensureNotDisposed () =
        if disposed then
            raise (ObjectDisposedException(nameof MailboxProcessorStore))

    interface IStatechartsStore<'State, 'Context> with
        member _.Load(instanceId) =
            ensureNotDisposed ()

            agent.PostAndAsyncReply(fun reply -> Load(instanceId, reply))
            |> Async.StartAsTask

        member _.Save instanceId snapshot =
            ensureNotDisposed ()

            agent.PostAndAsyncReply(fun reply -> Save(instanceId, snapshot, reply))
            |> Async.StartAsTask

        member _.Subscribe instanceId observer =
            ensureNotDisposed ()

            agent.PostAndAsyncReply(fun reply -> Subscribe(instanceId, observer, reply))
            |> Async.RunSynchronously

    interface IDisposable with
        member _.Dispose() =
            if not disposed then
                disposed <- true
                agent.PostAndReply(Stop)

[<AutoOpen>]
module StoreServiceCollectionExtensions =

    type IServiceCollection with

        member services.AddStatechartsStore<'State, 'Context when 'State: equality and 'State: comparison>() =
            services.AddSingleton<IStatechartsStore<'State, 'Context>>(fun sp ->
                let logger =
                    sp.GetRequiredService<ILogger<MailboxProcessorStore<'State, 'Context>>>()

                new MailboxProcessorStore<'State, 'Context>(logger) :> IStatechartsStore<'State, 'Context>)
