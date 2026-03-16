namespace Frank.Statecharts

open System
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

/// <summary>
/// Abstraction for state machine instance persistence.
/// </summary>
/// <remarks>
/// <para>
/// All implementations MUST serialize state access through an actor (e.g., <c>MailboxProcessor</c>).
/// This ensures concurrent requests to the same instance are processed sequentially,
/// preventing lost updates without requiring optimistic concurrency tokens.
/// </para>
/// <para>
/// The actor is the sole accessor of the backing store. External code never reads or writes
/// the backing store directly -- all operations go through <c>GetState</c>/<c>SetState</c>.
/// </para>
/// <para>
/// For durable implementations (e.g., SQLite), persistence operations occur inside the actor loop.
/// The actor wraps the backing store, not the other way around.
/// </para>
/// </remarks>
type IStateMachineStore<'State, 'Context when 'State: equality> =
    /// <summary>
    /// Retrieve the current state and context for an instance.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the state machine instance.</param>
    /// <returns>The current state and context, or <c>None</c> if the instance does not exist.</returns>
    /// <remarks>
    /// This operation is serialized through the actor. Concurrent calls to <c>GetState</c>
    /// are queued and processed one at a time, ensuring consistent reads.
    /// </remarks>
    abstract GetState: instanceId: string -> Task<('State * 'Context) option>

    /// <summary>
    /// Persist a state change for an instance.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the state machine instance.</param>
    /// <param name="state">The new state value.</param>
    /// <param name="context">The new context value.</param>
    /// <remarks>
    /// This operation is serialized through the actor. Concurrent calls to <c>SetState</c>
    /// for the same instance are queued and applied sequentially, guaranteeing no lost updates.
    /// Subscribers are notified after each state change within the actor loop.
    /// </remarks>
    abstract SetState: instanceId: string -> state: 'State -> context: 'Context -> Task<unit>

    /// <summary>
    /// Subscribe to state changes for an instance.
    /// </summary>
    /// <param name="instanceId">The unique identifier of the state machine instance.</param>
    /// <param name="observer">The observer to receive state change notifications.</param>
    /// <returns>An <see cref="IDisposable"/> that unsubscribes when disposed.</returns>
    /// <remarks>
    /// BehaviorSubject semantics: new subscribers immediately receive current state if it exists.
    /// Subscription management is serialized through the actor alongside state operations.
    /// </remarks>
    abstract Subscribe: instanceId: string -> observer: IObserver<'State * 'Context> -> IDisposable

/// <summary>
/// Internal message type for the <c>MailboxProcessor</c> actor loop.
/// </summary>
/// <remarks>
/// These messages are private to the store implementation. All state operations
/// are encoded as messages and processed sequentially by the actor, ensuring
/// thread-safe access to the in-memory state map and subscriber list.
/// </remarks>
type private StoreMessage<'State, 'Context when 'State: equality> =
    | GetState of instanceId: string * replyChannel: AsyncReplyChannel<('State * 'Context) option>
    | SetState of instanceId: string * state: 'State * context: 'Context * replyChannel: AsyncReplyChannel<unit>
    | Subscribe of
        instanceId: string *
        observer: IObserver<'State * 'Context> *
        replyChannel: AsyncReplyChannel<IDisposable>
    | Unsubscribe of instanceId: string * observer: IObserver<'State * 'Context>
    | Stop of replyChannel: AsyncReplyChannel<unit>

/// <summary>
/// In-memory <see cref="IStateMachineStore{TState, TContext}"/> backed by a <c>MailboxProcessor</c>.
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
            let mutable instances = Map.empty<string, 'State * 'Context>
            let mutable subscribers = Map.empty<string, IObserver<'State * 'Context> list>

            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | GetState(id, reply) ->
                        reply.Reply(Map.tryFind id instances)
                        return! loop ()

                    | SetState(id, state, ctx, reply) ->
                        instances <- Map.add id (state, ctx) instances

                        match Map.tryFind id subscribers with
                        | Some observers ->
                            for obs in observers do
                                try
                                    obs.OnNext(state, ctx)
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
                        | Some state ->
                            try
                                observer.OnNext(state)
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

    interface IStateMachineStore<'State, 'Context> with
        member _.GetState(instanceId) =
            ensureNotDisposed ()

            agent.PostAndAsyncReply(fun reply -> GetState(instanceId, reply))
            |> Async.StartAsTask

        member _.SetState instanceId state context =
            ensureNotDisposed ()

            agent.PostAndAsyncReply(fun reply -> SetState(instanceId, state, context, reply))
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

        member services.AddStateMachineStore<'State, 'Context when 'State: equality and 'State: comparison>() =
            services.AddSingleton<IStateMachineStore<'State, 'Context>>(fun sp ->
                let logger =
                    sp.GetRequiredService<ILogger<MailboxProcessorStore<'State, 'Context>>>()

                new MailboxProcessorStore<'State, 'Context>(logger) :> IStateMachineStore<'State, 'Context>)
