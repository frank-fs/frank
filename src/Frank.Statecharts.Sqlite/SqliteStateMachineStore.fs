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
    | Load of instanceId: string * replyChannel: AsyncReplyChannel<InstanceSnapshot<'State, 'Context> option>
    | Save of instanceId: string * snapshot: InstanceSnapshot<'State, 'Context> * replyChannel: AsyncReplyChannel<unit>
    | Subscribe of
        instanceId: string *
        observer: IObserver<InstanceSnapshot<'State, 'Context>> *
        replyChannel: AsyncReplyChannel<IDisposable>
    | Unsubscribe of instanceId: string * observer: IObserver<InstanceSnapshot<'State, 'Context>>
    | Stop of replyChannel: AsyncReplyChannel<unit>

/// <summary>
/// SQLite-backed durable implementation of <see cref="IStatechartsStore{TState, TContext}"/>.
/// All operations are serialized through a <see cref="MailboxProcessor{T}"/> actor.
/// State is persisted to SQLite on every <c>Save</c> call and lazily rehydrated
/// from SQLite on <c>Load</c> cache misses.
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
type SqliteStatechartsStore<'State, 'Context when 'State: equality>
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

    // Auto-create schema with hierarchy_config and history_record columns.
    do
        use cmd = connection.CreateCommand()

        cmd.CommandText <-
            """
            CREATE TABLE IF NOT EXISTS state_machine_instances (
                instance_id      TEXT NOT NULL,
                state_type       TEXT NOT NULL,
                state_json       TEXT NOT NULL,
                context_json     TEXT NOT NULL,
                hierarchy_config TEXT NOT NULL DEFAULT '{}',
                history_record   TEXT NOT NULL DEFAULT '{}',
                updated_at       TEXT NOT NULL DEFAULT (datetime('now')),
                PRIMARY KEY (instance_id, state_type)
            );"""

        cmd.ExecuteNonQuery() |> ignore

    let mutable disposed = false

    /// Serialize ActiveStateConfiguration as a JSON array of state ID strings.
    let serializeHierarchyConfig (config: ActiveStateConfiguration) : string =
        let states = ActiveStateConfiguration.toSet config |> Set.toList
        JsonSerializer.Serialize(states, options)

    /// Deserialize ActiveStateConfiguration from JSON array of state ID strings.
    let deserializeHierarchyConfig (json: string) : ActiveStateConfiguration =
        try
            let states = JsonSerializer.Deserialize<string list>(json, options)
            states |> Set.ofList |> ActiveStateConfiguration.ofSet
        with _ ->
            ActiveStateConfiguration.empty

    /// Serialize HistoryRecord as a JSON map of composite state ID -> set of state ID strings.
    let serializeHistoryRecord (history: HistoryRecord) : string =
        let entries =
            HistoryRecord.toMap history
            |> Map.map (fun _ config -> ActiveStateConfiguration.toSet config |> Set.toList)

        JsonSerializer.Serialize(entries, options)

    /// Deserialize HistoryRecord from JSON map of composite state ID -> list of state ID strings.
    let deserializeHistoryRecord (json: string) : HistoryRecord =
        try
            let raw = JsonSerializer.Deserialize<Map<string, string list>>(json, options)

            raw
            |> Map.map (fun _ states -> states |> Set.ofList |> ActiveStateConfiguration.ofSet)
            |> HistoryRecord.ofMap
        with _ ->
            HistoryRecord.empty

    /// Load snapshot from SQLite for the given instance ID.
    /// Returns None if not found. Logs and returns None on deserialization failure.
    let loadFromSqlite (id: string) =
        try
            use cmd = connection.CreateCommand()

            cmd.CommandText <-
                """
                SELECT state_json, context_json, hierarchy_config, history_record
                FROM state_machine_instances
                WHERE instance_id = @id AND state_type = @type;"""

            cmd.Parameters.AddWithValue("@id", id) |> ignore
            cmd.Parameters.AddWithValue("@type", stateTypeName) |> ignore
            use reader = cmd.ExecuteReader()

            if reader.Read() then
                let stateJson = reader.GetString(0)
                let ctxJson = reader.GetString(1)
                let hierarchyJson = reader.GetString(2)
                let historyJson = reader.GetString(3)
                let state = JsonSerializer.Deserialize<'State>(stateJson, options)
                let ctx = JsonSerializer.Deserialize<'Context>(ctxJson, options)
                let hierarchyConfig = deserializeHierarchyConfig hierarchyJson
                let historyRecord = deserializeHistoryRecord historyJson

                Some
                    { State = state
                      Context = ctx
                      HierarchyConfig = hierarchyConfig
                      HistoryRecord = historyRecord }
            else
                None
        with ex ->
            logger.LogError(ex, "Failed to load state for instance {InstanceId}", id)
            None

    let agent =
        MailboxProcessor<StoreMessage<'State, 'Context>>.Start(fun inbox ->
            let mutable instances = Map.empty<string, InstanceSnapshot<'State, 'Context>>
            let mutable subscribers = Map.empty<string, IObserver<InstanceSnapshot<'State, 'Context>> list>

            let rec loop () =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Load(id, reply) ->
                        match Map.tryFind id instances with
                        | Some snapshot -> reply.Reply(Some snapshot)
                        | None ->
                            // Lazy rehydration from SQLite
                            match loadFromSqlite id with
                            | Some snapshot ->
                                instances <- Map.add id snapshot instances
                                reply.Reply(Some snapshot)
                            | None -> reply.Reply(None)

                        return! loop ()

                    | Save(id, snapshot, reply) ->
                        // Update in-memory cache
                        instances <- Map.add id snapshot instances

                        // Persist to SQLite
                        let persistError =
                            try
                                use cmd = connection.CreateCommand()

                                cmd.CommandText <-
                                    """
                                    INSERT INTO state_machine_instances (instance_id, state_type, state_json, context_json, hierarchy_config, history_record, updated_at)
                                    VALUES (@id, @type, @stateJson, @ctxJson, @hierarchyConfig, @historyRecord, datetime('now'))
                                    ON CONFLICT (instance_id, state_type)
                                    DO UPDATE SET
                                        state_json = excluded.state_json,
                                        context_json = excluded.context_json,
                                        hierarchy_config = excluded.hierarchy_config,
                                        history_record = excluded.history_record,
                                        updated_at = excluded.updated_at;"""

                                cmd.Parameters.AddWithValue("@id", id) |> ignore
                                cmd.Parameters.AddWithValue("@type", stateTypeName) |> ignore
                                cmd.Parameters.AddWithValue("@stateJson", JsonSerializer.Serialize(snapshot.State, options)) |> ignore
                                cmd.Parameters.AddWithValue("@ctxJson", JsonSerializer.Serialize(snapshot.Context, options)) |> ignore
                                cmd.Parameters.AddWithValue("@hierarchyConfig", serializeHierarchyConfig snapshot.HierarchyConfig) |> ignore
                                cmd.Parameters.AddWithValue("@historyRecord", serializeHistoryRecord snapshot.HistoryRecord) |> ignore
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

                        // BehaviorSubject: emit current snapshot immediately
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
                        | None ->
                            // If not in cache, try loading from SQLite for initial emit
                            match loadFromSqlite id with
                            | Some snapshot ->
                                instances <- Map.add id snapshot instances

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
            raise (ObjectDisposedException(nameof SqliteStatechartsStore))

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
                connection.Close()
                connection.Dispose()

/// Extension methods for registering SqliteStatechartsStore with dependency injection.
[<AutoOpen>]
module SqliteStoreServiceCollectionExtensions =

    type IServiceCollection with

        /// <summary>
        /// Registers a <see cref="SqliteStatechartsStore{TState, TContext}"/> as the
        /// <see cref="IStatechartsStore{TState, TContext}"/> singleton.
        /// </summary>
        /// <param name="connectionString">SQLite connection string (e.g., "Data Source=state.db").</param>
        /// <param name="jsonOptions">Optional JSON serializer options for state/context serialization.</param>
        member services.AddSqliteStatechartsStore<'State, 'Context when 'State: equality and 'State: comparison>
            (connectionString: string, ?jsonOptions: JsonSerializerOptions)
            =
            services.AddSingleton<IStatechartsStore<'State, 'Context>>(fun sp ->
                let logger =
                    sp.GetRequiredService<ILogger<SqliteStatechartsStore<'State, 'Context>>>()

                let opts = defaultArg jsonOptions JsonSerializerOptions.Default

                new SqliteStatechartsStore<'State, 'Context>(connectionString, logger, opts)
                :> IStatechartsStore<'State, 'Context>)
