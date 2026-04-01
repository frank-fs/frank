module StoreTests

open System
open System.Threading
open Expecto
open Frank.Statecharts
open Microsoft.Extensions.Logging

/// Simple ILogger implementation for testing that captures log calls.
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
                warnings.Add(msg, if isNull (box ex) then None else Some ex)

let makeStore () =
    let logger = TestLogger<MailboxProcessorStore<string, int>>()

    let store = new MailboxProcessorStore<string, int>(logger)

    store, logger

let makeUnitStore () =
    let logger = TestLogger<MailboxProcessorStore<string, unit>>()

    let store = new MailboxProcessorStore<string, unit>(logger)

    store, logger

/// Helper: create a snapshot for testing (flat state, no hierarchy).
let mkSnapshot state context : InstanceSnapshot<string, int> =
    { State = state
      Context = context
      HierarchyConfig = ActiveStateConfiguration.empty
      HistoryRecord = HistoryRecord.empty }

[<Tests>]
let basicCrudTests =
    testList
        "Store.BasicCRUD"
        [ testAsync "Load returns None for unknown instance" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              let! result = iface.Load("unknown") |> Async.AwaitTask
              Expect.isNone result "should be None"
          }

          testAsync "Save then Load returns stored value" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              do! iface.Save "game1" (mkSnapshot "Playing" 42) |> Async.AwaitTask
              let! result = iface.Load("game1") |> Async.AwaitTask
              Expect.isSome result "should return stored snapshot"
              let snapshot = result.Value
              Expect.equal snapshot.State "Playing" "state should match"
              Expect.equal snapshot.Context 42 "context should match"
          }

          testAsync "Save overwrites previous value" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              do! iface.Save "game1" (mkSnapshot "Playing" 1) |> Async.AwaitTask
              do! iface.Save "game1" (mkSnapshot "Finished" 2) |> Async.AwaitTask
              let! result = iface.Load("game1") |> Async.AwaitTask
              Expect.isSome result "should return latest snapshot"
              let snapshot = result.Value
              Expect.equal snapshot.State "Finished" "should return latest state"
              Expect.equal snapshot.Context 2 "should return latest context"
          }

          testAsync "Multiple instances are independent" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              do! iface.Save "a" (mkSnapshot "StateA" 1) |> Async.AwaitTask
              do! iface.Save "b" (mkSnapshot "StateB" 2) |> Async.AwaitTask
              let! a = iface.Load("a") |> Async.AwaitTask
              let! b = iface.Load("b") |> Async.AwaitTask
              Expect.isSome a "instance a should exist"
              Expect.isSome b "instance b should exist"
              Expect.equal a.Value.State "StateA" "instance a state"
              Expect.equal b.Value.State "StateB" "instance b state"
          } ]

[<Tests>]
let behaviorSubjectTests =
    testList
        "Store.BehaviorSubject"
        [ testAsync "Subscribe to existing state emits current state immediately" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              do! iface.Save "inst1" (mkSnapshot "Ready" 10) |> Async.AwaitTask

              let received = ResizeArray<string * int>()

              let observer =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub = iface.Subscribe "inst1" observer
              sub.Dispose()

              Expect.equal received.Count 1 "should have received one emission"
              Expect.equal received[0] ("Ready", 10) "should be current state"
          }

          testAsync "Subscribe to nonexistent instance emits nothing initially" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              let received = ResizeArray<string * int>()

              let observer =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub = iface.Subscribe "nonexistent" observer
              sub.Dispose()

              Expect.equal received.Count 0 "should have received nothing"
          } ]

[<Tests>]
let observableNotificationTests =
    testList
        "Store.ObservableNotification"
        [ testAsync "Save notifies subscriber" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              let received = ResizeArray<string * int>()

              let observer =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub = iface.Subscribe "inst1" observer

              do! iface.Save "inst1" (mkSnapshot "Active" 5) |> Async.AwaitTask

              sub.Dispose()

              Expect.equal received.Count 1 "should have received one notification"
              Expect.equal received[0] ("Active", 5) "should be new state"
          }

          testAsync "Multiple subscribers all receive notifications" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              let received1 = ResizeArray<string * int>()
              let received2 = ResizeArray<string * int>()

              let obs1 =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(v) = received1.Add(v.State, v.Context)
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let obs2 =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(v) = received2.Add(v.State, v.Context)
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub1 = iface.Subscribe "inst1" obs1
              let sub2 = iface.Subscribe "inst1" obs2

              do! iface.Save "inst1" (mkSnapshot "Active" 5) |> Async.AwaitTask

              sub1.Dispose()
              sub2.Dispose()

              Expect.equal received1.Count 1 "sub1 should have received one notification"
              Expect.equal received2.Count 1 "sub2 should have received one notification"
          }

          testAsync "Unsubscribe stops notifications" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              let received = ResizeArray<string * int>()

              let observer =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub = iface.Subscribe "inst1" observer
              do! iface.Save "inst1" (mkSnapshot "First" 1) |> Async.AwaitTask

              sub.Dispose()
              // Small delay to let Unsubscribe message be processed
              do! Async.Sleep 50

              do! iface.Save "inst1" (mkSnapshot "Second" 2) |> Async.AwaitTask

              Expect.equal received.Count 1 "should only have received one notification"
              Expect.equal received[0] ("First", 1) "should be first state only"
          }

          testAsync "Failing subscriber does not break other subscribers" {
              let store, logger = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              let received = ResizeArray<string * int>()

              let badObserver =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(_) = failwith "I'm a bad observer"
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let goodObserver =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub1 = iface.Subscribe "inst1" badObserver
              let sub2 = iface.Subscribe "inst1" goodObserver

              do! iface.Save "inst1" (mkSnapshot "Active" 5) |> Async.AwaitTask

              sub1.Dispose()
              sub2.Dispose()

              Expect.equal received.Count 1 "good subscriber should still receive notification"
              Expect.isGreaterThan logger.Warnings.Count 0 "should have logged warning for bad subscriber"
          } ]

[<Tests>]
let concurrencyTests =
    testList
        "Store.Concurrency"
        [ testAsync "100 concurrent Save operations complete without error" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              let ops =
                  [| for i in 0..99 ->
                         async {
                             do!
                                 iface.Save (sprintf "inst-%d" (i % 10)) (mkSnapshot (sprintf "State-%d" i) i)
                                 |> Async.AwaitTask
                         } |]

              do! Async.Parallel ops |> Async.Ignore

              // Verify all instances have some state
              for i in 0..9 do
                  let! result = iface.Load(sprintf "inst-%d" i) |> Async.AwaitTask
                  Expect.isSome result (sprintf "instance inst-%d should have state" i)
          }

          testAsync "Concurrent Load during Save does not throw" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStatechartsStore<string, int>

              do! iface.Save "inst1" (mkSnapshot "Initial" 0) |> Async.AwaitTask

              let ops =
                  [| for i in 0..99 ->
                         if i % 2 = 0 then
                             async { do! iface.Save "inst1" (mkSnapshot (sprintf "State-%d" i) i) |> Async.AwaitTask }
                         else
                             async {
                                 let! _ = iface.Load("inst1") |> Async.AwaitTask
                                 return ()
                             } |]

              do! Async.Parallel ops |> Async.Ignore
          } ]

[<Tests>]
let actorSerializationTests =
    testList
        "Store.ActorSerialization"
        [ testAsync "Concurrent Save to same instance are serialized" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable
              let iface = store :> IStatechartsStore<string, int>

              // Fire 20 concurrent Save operations to the same instance
              let completionOrder = System.Collections.Concurrent.ConcurrentBag<int>()

              let ops =
                  [| for i in 0..19 ->
                         async {
                             do! iface.Save "same-instance" (mkSnapshot (sprintf "State-%d" i) i) |> Async.AwaitTask
                             completionOrder.Add(i)
                         } |]

              do! Async.Parallel ops |> Async.Ignore

              // All 20 operations should have completed
              Expect.equal completionOrder.Count 20 "all operations should complete"

              // The final state should be one of the 20 values (the last one processed)
              let! result = iface.Load("same-instance") |> Async.AwaitTask
              Expect.isSome result "should have state after concurrent writes"
          }

          testAsync "Interleaved Load and Save on same instance produce no torn reads" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable
              let iface = store :> IStatechartsStore<string, int>

              do! iface.Save "inst1" (mkSnapshot "State-0" 0) |> Async.AwaitTask

              // Interleave reads and writes to the same instance
              let ops =
                  [| for i in 0..49 ->
                         if i % 2 = 0 then
                             async {
                                 do! iface.Save "inst1" (mkSnapshot (sprintf "State-%d" i) i) |> Async.AwaitTask
                             }
                         else
                             async {
                                 let! result = iface.Load("inst1") |> Async.AwaitTask

                                 // Every read must return a valid state (no torn reads)
                                 Expect.isSome result "Load should never return None for an existing instance"

                                 let snapshot = result.Value
                                 // The state and context must be from the same Save call
                                 let expectedState = sprintf "State-%d" snapshot.Context
                                 Expect.equal snapshot.State expectedState "state and context must be consistent (no torn read)"
                             } |]

              do! Async.Parallel ops |> Async.Ignore
          }

          testAsync "All state changes are observed (no lost updates via subscriber)" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable
              let iface = store :> IStatechartsStore<string, int>

              let received = System.Collections.Concurrent.ConcurrentBag<string * int>()

              let observer =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(v) = received.Add((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub = iface.Subscribe "tracked" observer

              // 50 concurrent Save calls
              let ops =
                  [| for i in 1..50 ->
                         async { do! iface.Save "tracked" (mkSnapshot (sprintf "S%d" i) i) |> Async.AwaitTask } |]

              do! Async.Parallel ops |> Async.Ignore

              sub.Dispose()

              // Every Save should have triggered a subscriber notification
              Expect.equal received.Count 50 "subscriber should receive all 50 state changes"
          }

          testAsync "Subscriber notifications preserve sequential consistency" {
              let store, _ = makeStore ()
              use _s = store :> IDisposable
              let iface = store :> IStatechartsStore<string, int>

              let received = System.Collections.Concurrent.ConcurrentQueue<string * int>()

              let observer =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(v) = received.Enqueue((v.State, v.Context))
                      member _.OnError(_) = ()
                      member _.OnCompleted() = () }

              let sub = iface.Subscribe "ordered" observer

              // Sequential Save calls to verify ordering is preserved
              for i in 1..20 do
                  do! iface.Save "ordered" (mkSnapshot (sprintf "S%d" i) i) |> Async.AwaitTask

              sub.Dispose()

              let items = received |> Seq.toList
              Expect.equal items.Length 20 "should have received all 20 notifications"

              // Verify ordering: since these were sequential calls, notifications
              // should arrive in the same order
              for i in 0..19 do
                  let state, ctx = items[i]
                  Expect.equal state (sprintf "S%d" (i + 1)) (sprintf "notification %d should have correct state" i)
                  Expect.equal ctx (i + 1) (sprintf "notification %d should have correct context" i)
          } ]

[<Tests>]
let disposalLifecycleTests =
    testList
        "Store.DisposalLifecycle"
        [ testAsync "Dispose sends OnCompleted to subscribers" {
              let store, _ = makeStore ()

              let iface = store :> IStatechartsStore<string, int>

              let completed = ref false

              let observer =
                  { new IObserver<InstanceSnapshot<string, int>> with
                      member _.OnNext(_) = ()
                      member _.OnError(_) = ()
                      member _.OnCompleted() = completed.Value <- true }

              let _sub = iface.Subscribe "inst1" observer

              (store :> IDisposable).Dispose()

              Expect.isTrue completed.Value "subscriber should have received OnCompleted"
          }

          test "Disposed store throws ObjectDisposedException on Load" {
              let store, _ = makeStore ()

              let iface = store :> IStatechartsStore<string, int>

              (store :> IDisposable).Dispose()

              Expect.throwsT<ObjectDisposedException>
                  (fun () -> iface.Load("inst1") |> ignore)
                  "should throw ObjectDisposedException"
          }

          test "Disposed store throws ObjectDisposedException on Save" {
              let store, _ = makeStore ()

              let iface = store :> IStatechartsStore<string, int>

              (store :> IDisposable).Dispose()

              Expect.throwsT<ObjectDisposedException>
                  (fun () -> iface.Save "inst1" (mkSnapshot "x" 1) |> ignore)
                  "should throw ObjectDisposedException"
          }

          test "Disposed store throws ObjectDisposedException on Subscribe" {
              let store, _ = makeStore ()

              let iface = store :> IStatechartsStore<string, int>

              (store :> IDisposable).Dispose()

              let observer =
                  { new IObserver<InstanceSnapshot<string, int>> with
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
