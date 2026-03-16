namespace Frank.Statecharts.Sqlite

open System
open System.Runtime.ExceptionServices
open System.Text.Json
open System.Threading.Tasks
open Microsoft.Data.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging
open Frank.Statecharts

/// Messages processed by the internal MailboxProcessor actor.
/// Identical pattern to MailboxProcessorStore but with SQLite persistence.
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
/// SQLite-backed durable implementation of <see cref="IStateMachineStore{TState, TContext}"/>.
/// All operations are serialized through a <see cref="MailboxProcessor{T}"/> actor.
/// State is persisted to SQLite on every <c>SetState</c> call and lazily rehydrated
/// from SQLite on <c>GetState</c> cache misses.
/// </summary>
/// <remarks>
/// <para>
/// <b>JSON Serialization</b>: State and context values are serialized using <see cref="System.Text.Json.JsonSerializer"/>.
/// F# discriminated unions require a custom converter (e.g., <c>FSharp.SystemTextJson</c>).
/// Pass a configured <see cref="JsonSerializerOptions"/> to the constructor.
/// </para>
/// <para>
/// <b>Single Process</b>: This store is designed for single-process use. The actor model
/// assumes it is the sole accessor of the database. For multi-process deployments,
/// use a database with proper concurrent access support (e.g., PostgreSQL).
/// </para>
/// <para>
/// <b>Memory</b>: Accessed instances are cached in memory for the lifetime of the store.
/// There is no cache eviction. LRU eviction may be added in a future version.
/// </para>
/// </remarks>
type SqliteStateMachineStore<'State, 'Context when 'State: equality>
    (connectionString: string, logger: ILogger, ?jsonOptions: JsonSerializerOptions) =

    let options = defaultArg jsonOptions JsonSerializerOptions.Default
    let stateTypeName = typeof<'State>.FullName

    // Open a single connection for the lifetime of the store.
    // Safe because the actor serializes all access.
    let connection = new SqliteConnection(connectionString)

    do connection.Open()

    // Enable WAL mode for better external read compatibility.
    do
        use cmd = connection.CreateCommand()
        cmd.CommandText <- "PRAGMA journal_mode=WAL;"
        cmd.ExecuteNonQuery() |> ignore

    // Set busy timeout for external lock contention (5 seconds).
    do
        use cmd = connection.CreateCommand()
        cmd.CommandText <- "PRAGMA busy_timeout=5000;"
        cmd.ExecuteNonQuery() |> ignore

    // Auto-create schema (FR-008).
    do
        use cmd = connection.CreateCommand()

        cmd.CommandText <-
            """
            CREATE TABLE IF NOT EXISTS state_machine_instances (
                instance_id  TEXT NOT NULL,
                state_type   TEXT NOT NULL,
                state_json   TEXT NOT NULL,
                context_json TEXT NOT NULL,
                updated_at   TEXT NOT NULL DEFAULT (datetime('now')),
                PRIMARY KEY (instance_id, state_type)
            );"""

        cmd.ExecuteNonQuery() |> ignore

    let mutable disposed = false

    /// Load state from SQLite for the given instance ID.
    /// Returns None if not found. Logs and returns None on deserialization failure.
    let loadFromSqlite (id: string) =
        try
            use cmd = connection.CreateCommand()

            cmd.CommandText <-
                """
                SELECT state_json, context_json
                FROM state_machine_instances
                WHERE instance_id = @id AND state_type = @type;"""

            cmd.Parameters.AddWithValue("@id", id) |> ignore
            cmd.Parameters.AddWithValue("@type", stateTypeName) |> ignore
            use reader = cmd.ExecuteReader()

            if reader.Read() then
                let stateJson = reader.GetString(0)
                let ctxJson = reader.GetString(1)
                let state = JsonSerializer.Deserialize<'State>(stateJson, options)
                let ctx = JsonSerializer.Deserialize<'Context>(ctxJson, options)
                Some(state, ctx)
            else
                None
        with ex ->
            logger.LogError(ex, "Failed to load state for instance {InstanceId}", id)
            None

    let agent =
        MailboxProcessor<StoreMessage<'State, 'Context>>.Start(fun inbox ->
            let mutable instances = Map.empty<string, 'State * 'Context>
            let mutable subscribers = Map.empty<string, IObserver<'State * 'Context> list>

            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | GetState(id, reply) ->
                        match Map.tryFind id instances with
                        | Some state -> reply.Reply(Some state)
                        | None ->
                            // Lazy rehydration from SQLite (T025)
                            match loadFromSqlite id with
                            | Some(state, ctx) ->
                                instances <- Map.add id (state, ctx) instances
                                reply.Reply(Some(state, ctx))
                            | None -> reply.Reply(None)

                        return! loop ()

                    | SetState(id, state, ctx, reply) ->
                        // Update in-memory cache
                        instances <- Map.add id (state, ctx) instances

                        // Persist to SQLite
                        let persistError =
                            try
                                use cmd = connection.CreateCommand()

                                cmd.CommandText <-
                                    """
                                    INSERT INTO state_machine_instances (instance_id, state_type, state_json, context_json, updated_at)
                                    VALUES (@id, @type, @stateJson, @ctxJson, datetime('now'))
                                    ON CONFLICT (instance_id, state_type)
                                    DO UPDATE SET
                                        state_json = excluded.state_json,
                                        context_json = excluded.context_json,
                                        updated_at = excluded.updated_at;"""

                                cmd.Parameters.AddWithValue("@id", id) |> ignore
                                cmd.Parameters.AddWithValue("@type", stateTypeName) |> ignore
                                cmd.Parameters.AddWithValue("@stateJson", JsonSerializer.Serialize(state, options)) |> ignore
                                cmd.Parameters.AddWithValue("@ctxJson", JsonSerializer.Serialize(ctx, options)) |> ignore
                                cmd.ExecuteNonQuery() |> ignore
                                None
                            with ex ->
                                logger.LogError(ex, "Failed to persist state for instance {InstanceId}", id)
                                Some(ExceptionDispatchInfo.Capture(ex))

                        match persistError with
                        | Some edi -> edi.Throw()
                        | None -> ()

                        // Notify subscribers after persistence
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

                        // BehaviorSubject: emit current state immediately
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
                        | None ->
                            // If not in cache, try loading from SQLite for initial emit
                            match loadFromSqlite id with
                            | Some(state, ctx) ->
                                instances <- Map.add id (state, ctx) instances

                                try
                                    observer.OnNext((state, ctx))
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
            raise (ObjectDisposedException(nameof SqliteStateMachineStore))

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
                connection.Close()
                connection.Dispose()

/// Extension methods for registering SqliteStateMachineStore with dependency injection.
[<AutoOpen>]
module SqliteStoreServiceCollectionExtensions =

    type IServiceCollection with

        /// <summary>
        /// Registers a <see cref="SqliteStateMachineStore{TState, TContext}"/> as the
        /// <see cref="IStateMachineStore{TState, TContext}"/> singleton.
        /// </summary>
        /// <param name="connectionString">SQLite connection string (e.g., "Data Source=state.db").</param>
        /// <param name="jsonOptions">Optional JSON serializer options for state/context serialization.</param>
        member services.AddSqliteStateMachineStore<'State, 'Context when 'State: equality and 'State: comparison>
            (connectionString: string, ?jsonOptions: JsonSerializerOptions)
            =
            services.AddSingleton<IStateMachineStore<'State, 'Context>>(fun sp ->
                let logger =
                    sp.GetRequiredService<ILogger<SqliteStateMachineStore<'State, 'Context>>>()

                let opts = defaultArg jsonOptions JsonSerializerOptions.Default

                new SqliteStateMachineStore<'State, 'Context>(connectionString, logger, opts)
                :> IStateMachineStore<'State, 'Context>)
