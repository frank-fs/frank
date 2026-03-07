namespace Frank.Statecharts

open System
open System.Threading.Tasks
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

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

type private StoreMessage<'State, 'Context when 'State: equality> =
    | GetState of instanceId: string * replyChannel: AsyncReplyChannel<('State * 'Context) option>
    | SetState of instanceId: string * state: 'State * context: 'Context * replyChannel: AsyncReplyChannel<unit>
    | Subscribe of
        instanceId: string *
        observer: IObserver<'State * 'Context> *
        replyChannel: AsyncReplyChannel<IDisposable>
    | Unsubscribe of instanceId: string * observer: IObserver<'State * 'Context>
    | Stop of replyChannel: AsyncReplyChannel<unit>

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
