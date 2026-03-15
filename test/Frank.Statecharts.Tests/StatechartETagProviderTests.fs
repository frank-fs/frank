module StatechartETagProviderTests

open System
open System.Text
open Expecto
open Frank
open Frank.Statecharts
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Routing
open Microsoft.Extensions.Logging

// --- Test helpers ---

type TestState =
    | Idle
    | Active
    | Completed

type TestContext = { Counter: int; Label: string }

let contextSerializer (ctx: TestContext) : byte[] =
    Encoding.UTF8.GetBytes(sprintf "%d:%s" ctx.Counter ctx.Label)

let makeTestLogger<'T> () =
    { new ILogger<'T> with
        member _.Log(_, _, _, _, _) = ()

        member _.IsEnabled(_) = true

        member _.BeginScope(_) =
            { new IDisposable with
                member _.Dispose() = () } }

let makeStore () =
    let logger = makeTestLogger<MailboxProcessorStore<TestState, TestContext>> ()
    new MailboxProcessorStore<TestState, TestContext>(logger)

let makeCache () =
    let logger = makeTestLogger<ETagCache> ()
    new ETagCache(1000, logger)

let makeProvider (store: MailboxProcessorStore<TestState, TestContext>) =
    let iface = store :> IStateMachineStore<TestState, TestContext>

    StatechartETagProvider<TestState, TestContext>(iface, contextSerializer) :> IETagProvider

let makeFactory (store: MailboxProcessorStore<TestState, TestContext>) =
    let cache = makeCache ()

    let iface = store :> IStateMachineStore<TestState, TestContext>

    StatechartETagProviderFactory<TestState, TestContext>(iface, contextSerializer) :> IETagProviderFactory

// --- Tests ---

[<Tests>]
let deterministicHashingTests =
    testList
        "StatechartETagProvider.DeterministicHashing"
        [ testAsync "same state and context produce same ETag" {
              let store = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStateMachineStore<TestState, TestContext>

              do! iface.SetState "inst1" Active { Counter = 1; Label = "test" } |> Async.AwaitTask

              let provider = makeProvider store

              let! etag1 = provider.ComputeETag("inst1") |> Async.AwaitTask
              let! etag2 = provider.ComputeETag("inst1") |> Async.AwaitTask

              Expect.isSome etag1 "first ETag should be Some"
              Expect.isSome etag2 "second ETag should be Some"
              Expect.equal etag1 etag2 "same state and context should produce same ETag"
          }

          testAsync "different state produces different ETag" {
              let store = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStateMachineStore<TestState, TestContext>

              let ctx = { Counter = 1; Label = "test" }
              do! iface.SetState "inst1" Active ctx |> Async.AwaitTask
              do! iface.SetState "inst2" Completed ctx |> Async.AwaitTask

              let provider = makeProvider store

              let! etag1 = provider.ComputeETag("inst1") |> Async.AwaitTask
              let! etag2 = provider.ComputeETag("inst2") |> Async.AwaitTask

              Expect.isSome etag1 "first ETag should be Some"
              Expect.isSome etag2 "second ETag should be Some"
              Expect.notEqual etag1 etag2 "different state should produce different ETag"
          }

          testAsync "different context produces different ETag" {
              let store = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStateMachineStore<TestState, TestContext>

              do!
                  iface.SetState "inst1" Active { Counter = 1; Label = "alpha" }
                  |> Async.AwaitTask

              do! iface.SetState "inst2" Active { Counter = 2; Label = "beta" } |> Async.AwaitTask

              let provider = makeProvider store

              let! etag1 = provider.ComputeETag("inst1") |> Async.AwaitTask
              let! etag2 = provider.ComputeETag("inst2") |> Async.AwaitTask

              Expect.isSome etag1 "first ETag should be Some"
              Expect.isSome etag2 "second ETag should be Some"
              Expect.notEqual etag1 etag2 "different context should produce different ETag"
          }

          testAsync "ETag format is raw 32 hex char string (no quotes)" {
              let store = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStateMachineStore<TestState, TestContext>

              do! iface.SetState "inst1" Active { Counter = 1; Label = "test" } |> Async.AwaitTask

              let provider = makeProvider store

              let! etag = provider.ComputeETag("inst1") |> Async.AwaitTask

              let value = Expect.wantSome etag "ETag should be Some"
              // Raw format: 32 hex chars, no surrounding quotes
              Expect.equal value.Length 32 "ETag should be 32 hex chars (128 bits)"

              let isHex = value |> Seq.forall (fun c -> Char.IsAsciiHexDigit c)

              Expect.isTrue isHex "ETag should be hex characters only"
          }

          test "string DUCase produces expected case name" {
              Expect.equal (string Idle) "Idle" "Idle case name"
              Expect.equal (string Active) "Active" "Active case name"
              Expect.equal (string Completed) "Completed" "Completed case name"
          } ]

[<Tests>]
let providerBehaviorTests =
    testList
        "StatechartETagProvider.ProviderBehavior"
        [ testAsync "returns None when store has no state for instanceId" {
              let store = makeStore ()
              use _s = store :> IDisposable

              let provider = makeProvider store

              let! etag = provider.ComputeETag("nonexistent") |> Async.AwaitTask

              Expect.isNone etag "should return None for unknown instanceId"
          }

          testAsync "returns Some ETag when store has state" {
              let store = makeStore ()
              use _s = store :> IDisposable

              let iface = store :> IStateMachineStore<TestState, TestContext>

              do! iface.SetState "inst1" Idle { Counter = 0; Label = "" } |> Async.AwaitTask

              let provider = makeProvider store

              let! etag = provider.ComputeETag("inst1") |> Async.AwaitTask

              Expect.isSome etag "should return Some ETag for known instanceId"
          } ]

[<Tests>]
let factoryTests =
    testList
        "StatechartETagProviderFactory"
        [ test "returns None for endpoint without StateMachineMetadata" {
              let store = makeStore ()
              use _s = store :> IDisposable

              let factory = makeFactory store

              // Create an endpoint with no metadata
              let endpoint = Endpoint(null, EndpointMetadataCollection(), "test-endpoint")

              let result = factory.CreateProvider(endpoint)

              Expect.isNone result "should return None for endpoint without StateMachineMetadata"
          }

          test "returns Some provider for endpoint with StateMachineMetadata" {
              let store = makeStore ()
              use _s = store :> IDisposable

              let factory = makeFactory store

              let metadata: StateMachineMetadata =
                  { Machine = box "dummy"
                    StateHandlerMap = Map.empty
                    ResolveInstanceId = fun _ -> "dummy"
                    TransitionObservers = []
                    InitialStateKey = "Idle"
                    GetCurrentStateKey = fun _ _ _ -> System.Threading.Tasks.Task.FromResult("Idle")
                    EvaluateGuards = fun _ -> Allowed
                    ExecuteTransition =
                      fun _ _ _ -> System.Threading.Tasks.Task.FromResult(TransitionAttemptResult.NoEvent) }

              let endpoint =
                  Endpoint(null, EndpointMetadataCollection(metadata :> obj), "test-endpoint")

              let result = factory.CreateProvider(endpoint)

              Expect.isSome result "should return Some provider for endpoint with StateMachineMetadata"
          } ]
