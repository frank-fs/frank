module SqliteStoreTests

open System
open System.IO
open System.Text.Json
open System.Text.Json.Serialization
open Expecto
open Frank.Statecharts
open Frank.Statecharts.Sqlite
open Microsoft.Extensions.DependencyInjection
open Microsoft.Extensions.Logging

// -- Test types ---------------------------------------------------------------

type TestState =
    | Active
    | Completed
    | Error of string

// -- JSON options with FSharp.SystemTextJson for DU serialization -------------

let jsonOptions = JsonFSharpOptions.Default().ToJsonSerializerOptions()

// -- Test logger (duplicated from StoreTests.fs; small helper) ----------------

type TestLogger<'T>() =
    let warnings = ResizeArray<string * exn option>()
    member _.Warnings = warnings :> System.Collections.Generic.IReadOnlyList<_>

    interface ILogger<'T>

    interface ILogger with
        member _.IsEnabled(_) = true

        member _.BeginScope(_) =
            { new IDisposable with
                member _.Dispose() = () }

        member _.Log(logLevel, _eventId, state, ex, formatter) =
            let msg = formatter.Invoke(state, ex)

            if logLevel = LogLevel.Warning then
                warnings.Add((msg, if isNull (box ex) then None else Some ex))

// -- Helpers ------------------------------------------------------------------

/// Helper: create a snapshot for testing (flat state, no hierarchy).
let mkSnapshot state context : InstanceSnapshot<TestState, int> =
    { State = state
      Context = context
      HierarchyConfig = ActiveStateConfiguration.empty
      HistoryRecord = HistoryRecord.empty }

/// Create a store backed by in-memory SQLite (fast, non-persistent).
let makeStore () =
    let logger = TestLogger<SqliteStatechartsStore<TestState, int>>()

    let store =
        new SqliteStatechartsStore<TestState, int>("Data Source=:memory:", logger, jsonOptions)

    store, logger

/// Create a store backed by a temp-file SQLite database (persistent across connections).
let makeFileStore (path: string) =
    let logger = TestLogger<SqliteStatechartsStore<TestState, int>>()
    let connStr = sprintf "Data Source=%s" path

    let store =
        new SqliteStatechartsStore<TestState, int>(connStr, logger, jsonOptions)

    store, logger

// =============================================================================
// T030 -- CRUD tests
// =============================================================================

[<Tests>]
let crudTests =
    testList
        "SqliteStore.CRUD"
        [ testAsync "Load returns None for unknown instance" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              let! result = iface.Load("unknown") |> Async.AwaitTask
              Expect.isNone result "should be None"
          }

          testAsync "Save then Load returns stored value" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              do! iface.Save "game1" (mkSnapshot Active 42) |> Async.AwaitTask
              let! result = iface.Load("game1") |> Async.AwaitTask
              Expect.isSome result "should return stored snapshot"
              Expect.equal result.Value.State Active "state should match"
              Expect.equal result.Value.Context 42 "context should match"
          }

          testAsync "Save overwrites previous value" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              do! iface.Save "game1" (mkSnapshot Active 1) |> Async.AwaitTask
              do! iface.Save "game1" (mkSnapshot Completed 2) |> Async.AwaitTask
              let! result = iface.Load("game1") |> Async.AwaitTask
              Expect.isSome result "should return latest snapshot"
              Expect.equal result.Value.State Completed "should return latest state"
              Expect.equal result.Value.Context 2 "should return latest context"
          }

          testAsync "Multiple instances are independent" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              do! iface.Save "a" (mkSnapshot Active 1) |> Async.AwaitTask
              do! iface.Save "b" (mkSnapshot Completed 2) |> Async.AwaitTask
              let! a = iface.Load("a") |> Async.AwaitTask
              let! b = iface.Load("b") |> Async.AwaitTask
              Expect.isSome a "instance a should exist"
              Expect.isSome b "instance b should exist"
              Expect.equal a.Value.State Active "instance a state"
              Expect.equal b.Value.State Completed "instance b state"
          }

          testAsync "Parameterized state (Error of string) round-trips correctly" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              do! iface.Save "err1" (mkSnapshot (Error "timeout") 99) |> Async.AwaitTask
              let! result = iface.Load("err1") |> Async.AwaitTask
              Expect.isSome result "should return stored snapshot"
              Expect.equal result.Value.State (Error "timeout") "parameterized DU should round-trip"
              Expect.equal result.Value.Context 99 "context should round-trip"
          } ]

// =============================================================================
// T031 -- Rehydration tests (file-based SQLite)
// =============================================================================

[<Tests>]
let rehydrationTests =
    testList
        "SqliteStore.Rehydration"
        [ testAsync "State survives store restart" {
              let tempDb = Path.GetTempFileName()

              try
                  // First store: write state
                  let store1, _ = makeFileStore tempDb
                  let iface1 = store1 :> IStatechartsStore<TestState, int>
                  do! iface1.Save "game1" (mkSnapshot Active 42) |> Async.AwaitTask
                  (store1 :> IDisposable).Dispose()

                  // Second store: read state (simulates restart)
                  let store2, _ = makeFileStore tempDb
                  let iface2 = store2 :> IStatechartsStore<TestState, int>
                  let! result = iface2.Load("game1") |> Async.AwaitTask
                  Expect.isSome result "state should survive restart"
                  Expect.equal result.Value.State Active "state value should survive restart"
                  Expect.equal result.Value.Context 42 "context value should survive restart"
                  (store2 :> IDisposable).Dispose()
              finally
                  try
                      File.Delete(tempDb)
                  with _ ->
                      ()
          }

          testAsync "Parameterized state survives restart" {
              let tempDb = Path.GetTempFileName()

              try
                  let store1, _ = makeFileStore tempDb
                  let iface1 = store1 :> IStatechartsStore<TestState, int>
                  do! iface1.Save "err1" (mkSnapshot (Error "disk_full") 500) |> Async.AwaitTask
                  (store1 :> IDisposable).Dispose()

                  let store2, _ = makeFileStore tempDb
                  let iface2 = store2 :> IStatechartsStore<TestState, int>
                  let! result = iface2.Load("err1") |> Async.AwaitTask
                  Expect.isSome result "parameterized state should survive restart"
                  Expect.equal result.Value.State (Error "disk_full") "parameterized state value should survive"
                  Expect.equal result.Value.Context 500 "context should survive restart"
                  (store2 :> IDisposable).Dispose()
              finally
                  try
                      File.Delete(tempDb)
                  with _ ->
                      ()
          }

          testAsync "Multiple instances survive restart" {
              let tempDb = Path.GetTempFileName()

              try
                  let store1, _ = makeFileStore tempDb
                  let iface1 = store1 :> IStatechartsStore<TestState, int>
                  do! iface1.Save "a" (mkSnapshot Active 1) |> Async.AwaitTask
                  do! iface1.Save "b" (mkSnapshot Completed 2) |> Async.AwaitTask
                  do! iface1.Save "c" (mkSnapshot (Error "oops") 3) |> Async.AwaitTask
                  (store1 :> IDisposable).Dispose()

                  let store2, _ = makeFileStore tempDb
                  let iface2 = store2 :> IStatechartsStore<TestState, int>
                  let! a = iface2.Load("a") |> Async.AwaitTask
                  let! b = iface2.Load("b") |> Async.AwaitTask
                  let! c = iface2.Load("c") |> Async.AwaitTask
                  Expect.isSome a "instance a should survive"
                  Expect.isSome b "instance b should survive"
                  Expect.isSome c "instance c should survive"
                  Expect.equal a.Value.State Active "instance a state should match"
                  Expect.equal b.Value.State Completed "instance b state should match"
                  Expect.equal c.Value.State (Error "oops") "instance c state should match"
                  (store2 :> IDisposable).Dispose()
              finally
                  try
                      File.Delete(tempDb)
                  with _ ->
                      ()
          }

          testAsync "Overwritten state reflects latest value after restart" {
              let tempDb = Path.GetTempFileName()

              try
                  let store1, _ = makeFileStore tempDb
                  let iface1 = store1 :> IStatechartsStore<TestState, int>
                  do! iface1.Save "game1" (mkSnapshot Active 1) |> Async.AwaitTask
                  do! iface1.Save "game1" (mkSnapshot Completed 99) |> Async.AwaitTask
                  (store1 :> IDisposable).Dispose()

                  let store2, _ = makeFileStore tempDb
                  let iface2 = store2 :> IStatechartsStore<TestState, int>
                  let! result = iface2.Load("game1") |> Async.AwaitTask
                  Expect.isSome result "overwritten state should survive"
                  Expect.equal result.Value.State Completed "should reflect latest overwrite"
                  Expect.equal result.Value.Context 99 "context should reflect latest overwrite"
                  (store2 :> IDisposable).Dispose()
              finally
                  try
                      File.Delete(tempDb)
                  with _ ->
                      ()
          } ]

// =============================================================================
// T032 -- Subscription notification tests
// =============================================================================

[<Tests>]
let subscriptionTests =
    testList
        "SqliteStore.Subscriptions"
        [ testAsync "Subscribe to existing state emits current state immediately" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              do! iface.Save "inst1" (mkSnapshot Active 10) |> Async.AwaitTask

              let received = ResizeArray<TestState * int>()

              let observer =
                  { new IObserver<InstanceSnapshot<TestState, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub = iface.Subscribe "inst1" observer
              sub.Dispose()

              Expect.equal received.Count 1 "should have received one emission"
              Expect.equal received[0] (Active, 10) "should be current state"
          }

          testAsync "Subscribe to nonexistent instance emits nothing initially" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              let received = ResizeArray<TestState * int>()

              let observer =
                  { new IObserver<InstanceSnapshot<TestState, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub = iface.Subscribe "nonexistent" observer
              sub.Dispose()

              Expect.equal received.Count 0 "should have received nothing"
          }

          testAsync "Save notifies subscriber" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              let received = ResizeArray<TestState * int>()

              let observer =
                  { new IObserver<InstanceSnapshot<TestState, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub = iface.Subscribe "inst1" observer

              do! iface.Save "inst1" (mkSnapshot Active 5) |> Async.AwaitTask

              sub.Dispose()

              Expect.equal received.Count 1 "should have received one notification"
              Expect.equal received[0] (Active, 5) "should be new state"
          }

          testAsync "Multiple subscribers all receive notifications" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              let received1 = ResizeArray<TestState * int>()
              let received2 = ResizeArray<TestState * int>()

              let obs1 =
                  { new IObserver<InstanceSnapshot<TestState, int>> with
                      member _.OnNext(v) = received1.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let obs2 =
                  { new IObserver<InstanceSnapshot<TestState, int>> with
                      member _.OnNext(v) = received2.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub1 = iface.Subscribe "inst1" obs1
              let sub2 = iface.Subscribe "inst1" obs2

              do! iface.Save "inst1" (mkSnapshot Active 5) |> Async.AwaitTask

              sub1.Dispose()
              sub2.Dispose()

              Expect.equal received1.Count 1 "sub1 should have received one notification"
              Expect.equal received2.Count 1 "sub2 should have received one notification"
          }

          testAsync "Unsubscribe stops notifications" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              let received = ResizeArray<TestState * int>()

              let observer =
                  { new IObserver<InstanceSnapshot<TestState, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub = iface.Subscribe "inst1" observer
              do! iface.Save "inst1" (mkSnapshot Active 1) |> Async.AwaitTask

              sub.Dispose()
              // Small delay to let Unsubscribe message be processed
              do! Async.Sleep 50

              do! iface.Save "inst1" (mkSnapshot Completed 2) |> Async.AwaitTask

              Expect.equal received.Count 1 "should only have received one notification"
              Expect.equal received[0] (Active, 1) "should be first state only"
          }

          testAsync "Failing subscriber does not break other subscribers" {
              let store, logger = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              let received = ResizeArray<TestState * int>()

              let badObserver =
                  { new IObserver<InstanceSnapshot<TestState, int>> with
                      member _.OnNext(_) = failwith "I'm a bad observer"
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let goodObserver =
                  { new IObserver<InstanceSnapshot<TestState, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub1 = iface.Subscribe "inst1" badObserver
              let sub2 = iface.Subscribe "inst1" goodObserver

              do! iface.Save "inst1" (mkSnapshot Active 5) |> Async.AwaitTask

              sub1.Dispose()
              sub2.Dispose()

              Expect.equal received.Count 1 "good subscriber should still receive notification"
              Expect.isGreaterThan logger.Warnings.Count 0 "should have logged warning for bad subscriber"
          } ]

// =============================================================================
// T033 -- Concurrent access serialization tests
// =============================================================================

[<Tests>]
let concurrencyTests =
    testList
        "SqliteStore.Concurrency"
        [ testAsync "Concurrent Save operations complete without error" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              let ops =
                  [| for i in 0..49 ->
                         async {
                             do!
                                 iface.Save (sprintf "inst-%d" (i % 10)) (mkSnapshot Active i)
                                 |> Async.AwaitTask
                         } |]

              do! Async.Parallel ops |> Async.Ignore

              // Verify all instances have some state
              for i in 0..9 do
                  let! result = iface.Load(sprintf "inst-%d" i) |> Async.AwaitTask
                  Expect.isSome result (sprintf "instance inst-%d should have state" i)
          }

          testAsync "Concurrent Load during Save does not corrupt data" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              do! iface.Save "inst1" (mkSnapshot Active 0) |> Async.AwaitTask

              let ops =
                  [| for i in 0..99 ->
                         if i % 2 = 0 then
                             async { do! iface.Save "inst1" (mkSnapshot Active i) |> Async.AwaitTask }
                         else
                             async {
                                 let! _ = iface.Load("inst1") |> Async.AwaitTask
                                 return ()
                             } |]

              do! Async.Parallel ops |> Async.Ignore

              // Final state should exist and be valid
              let! result = iface.Load("inst1") |> Async.AwaitTask
              Expect.isSome result "should have state after concurrent reads/writes"
          }

          testAsync "Concurrent writes to same instance produce consistent final state" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<TestState, int>

              // Fire 100 sequential-value writes concurrently; actor serializes them
              let ops =
                  [| for i in 0..99 ->
                         async { do! iface.Save "race" (mkSnapshot Active i) |> Async.AwaitTask } |]

              do! Async.Parallel ops |> Async.Ignore

              let! result = iface.Load("race") |> Async.AwaitTask
              let snapshot = Expect.wantSome result "should have a final state"

              // The final context value should be one of the values [0..99]
              // (whichever was last processed by the actor)
              Expect.isTrue (snapshot.Context >= 0 && snapshot.Context <= 99) "context should be a valid value from the writes"
          } ]

// =============================================================================
// Disposal lifecycle tests
// =============================================================================

[<Tests>]
let disposalTests =
    testList
        "SqliteStore.Disposal"
        [ testAsync "Dispose sends OnCompleted to subscribers" {
              let store, _ = makeStore ()

              let iface = store :> IStatechartsStore<TestState, int>

              let completed = ref false

              let observer =
                  { new IObserver<InstanceSnapshot<TestState, int>> with
                      member _.OnNext(_) = ()
                      member _.OnError(_) = ()
                      member _.OnCompleted() = completed.Value <- true }

              let _sub = iface.Subscribe "inst1" observer

              (store :> IDisposable).Dispose()

              Expect.isTrue completed.Value "subscriber should have received OnCompleted"
          }

          test "Disposed store throws ObjectDisposedException on Load" {
              let store, _ = makeStore ()

              let iface = store :> IStatechartsStore<TestState, int>

              (store :> IDisposable).Dispose()

              Expect.throwsT<ObjectDisposedException>
                  (fun () -> iface.Load("inst1") |> ignore)
                  "should throw ObjectDisposedException"
          }

          test "Disposed store throws ObjectDisposedException on Save" {
              let store, _ = makeStore ()

              let iface = store :> IStatechartsStore<TestState, int>

              (store :> IDisposable).Dispose()

              Expect.throwsT<ObjectDisposedException>
                  (fun () -> iface.Save "inst1" (mkSnapshot Active 1) |> ignore)
                  "should throw ObjectDisposedException"
          }

          test "Disposed store throws ObjectDisposedException on Subscribe" {
              let store, _ = makeStore ()

              let iface = store :> IStatechartsStore<TestState, int>

              (store :> IDisposable).Dispose()

              let observer =
                  { new IObserver<InstanceSnapshot<TestState, int>> with
                      member _.OnNext(_) = ()
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              Expect.throwsT<ObjectDisposedException>
                  (fun () -> iface.Subscribe "inst1" observer |> ignore)
                  "should throw ObjectDisposedException"
          }

          test "Double dispose does not throw" {
              let store, _ = makeStore ()
              (store :> IDisposable).Dispose()
              (store :> IDisposable).Dispose()
          } ]

// =============================================================================
// DI registration tests (T029 validation)
// =============================================================================

[<Tests>]
let diRegistrationTests =
    testList
        "SqliteStore.DIRegistration"
        [ test "AddSqliteStatechartsStore registers IStatechartsStore singleton" {
              let services = ServiceCollection() :> IServiceCollection

              services.AddLogging(fun (builder: ILoggingBuilder) ->
                  builder.SetMinimumLevel(LogLevel.Debug) |> ignore)
              |> ignore

              services.AddSqliteStatechartsStore<TestState, int>("Data Source=:memory:", jsonOptions)
              |> ignore

              use sp = (services :?> ServiceCollection).BuildServiceProvider()
              let store = sp.GetRequiredService<IStatechartsStore<TestState, int>>()
              Expect.isNotNull (box store) "should resolve IStatechartsStore from DI"

              // Clean up the store (it opens a SQLite connection)
              match store with
              | :? IDisposable as d -> d.Dispose()
              | _ -> ()
          } ]

// =============================================================================
// Schema auto-creation tests (FR-008)
// =============================================================================

[<Tests>]
let schemaTests =
    testList
        "SqliteStore.Schema"
        [ testAsync "Schema is auto-created on first use with file database" {
              let tempDb = Path.GetTempFileName()

              try
                  let store, _ = makeFileStore tempDb
                  let iface = store :> IStatechartsStore<TestState, int>

                  // Writing and reading should work without manual schema setup
                  do! iface.Save "test1" (mkSnapshot Active 1) |> Async.AwaitTask
                  let! result = iface.Load("test1") |> Async.AwaitTask
                  Expect.isSome result "should work with auto-created schema"
                  Expect.equal result.Value.State Active "state should be stored correctly"
                  Expect.equal result.Value.Context 1 "context should be stored correctly"

                  (store :> IDisposable).Dispose()
              finally
                  try
                      File.Delete(tempDb)
                  with _ ->
                      ()
          } ]
