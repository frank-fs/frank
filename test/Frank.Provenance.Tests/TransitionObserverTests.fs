module Frank.Provenance.Tests.TransitionObserverTests

open System
open System.Security.Claims
open System.Threading.Tasks
open Expecto
open Frank.Provenance
open Microsoft.Extensions.Logging

/// A mock provenance store that captures appended records for assertion.
type MockProvenanceStore(?shouldThrow: exn) =
    let records = ResizeArray<ProvenanceRecord>()
    let mutable disposed = false

    member _.Records = records |> Seq.toList
    member _.RecordCount = records.Count

    interface IProvenanceStore with
        member _.Append(record) =
            if disposed then
                raise (ObjectDisposedException(nameof MockProvenanceStore))

            match shouldThrow with
            | Some ex -> raise ex
            | None -> records.Add(record)

        member _.QueryByResource(_) =
            Task.FromResult<ProvenanceRecord list>([])

        member _.QueryByAgent(_) =
            Task.FromResult<ProvenanceRecord list>([])

        member _.QueryByTimeRange(_, _) =
            Task.FromResult<ProvenanceRecord list>([])

    interface IDisposable with
        member _.Dispose() = disposed <- true

    member _.MarkDisposed() = disposed <- true

/// Helper type to build TransitionEvents with optional overrides.
type EventBuilder() =
    static member Create
        (
            ?user: ClaimsPrincipal,
            ?headers: Map<string, string>,
            ?httpMethod: string,
            ?previousState: string,
            ?newState: string,
            ?eventName: string,
            ?resourceUri: string,
            ?instanceId: string,
            ?timestamp: DateTimeOffset
        ) =
        { TransitionEvent.InstanceId = defaultArg instanceId "instance-1"
          ResourceUri = defaultArg resourceUri "/orders/1"
          PreviousState = defaultArg previousState "Draft"
          NewState = defaultArg newState "Submitted"
          Event = defaultArg eventName "submit"
          Timestamp = defaultArg timestamp (DateTimeOffset(2025, 7, 1, 12, 0, 0, TimeSpan.Zero))
          User = user
          HttpMethod = defaultArg httpMethod "POST"
          Headers = defaultArg headers Map.empty }

let private createLogger () =
    let factory: ILoggerFactory =
        LoggerFactory.Create(fun (builder: ILoggingBuilder) ->
            builder.AddConsole().SetMinimumLevel(LogLevel.Debug) |> ignore)

    factory.CreateLogger<TransitionObserver>()

let private makeAuthenticatedPrincipal (name: string) (id: string) =
    let claims = [ Claim(ClaimTypes.Name, name); Claim(ClaimTypes.NameIdentifier, id) ]

    let identity = ClaimsIdentity(claims, "TestAuth")
    ClaimsPrincipal(identity)

let private makeUnauthenticatedPrincipal () =
    let identity = ClaimsIdentity() // No authenticationType => IsAuthenticated = false
    ClaimsPrincipal(identity)

[<Tests>]
let transitionObserverTests =
    testList
        "TransitionObserver"
        [ testList
              "Agent extraction"
              [ test "authenticated user produces Person agent" {
                    let store = new MockProvenanceStore()
                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>
                    let user = makeAuthenticatedPrincipal "Alice" "alice-123"

                    observer.OnNext(EventBuilder.Create(user = user))

                    Expect.equal store.RecordCount 1 "Should have one record"
                    let record = store.Records.[0]

                    match record.Agent.AgentType with
                    | AgentType.Person(name, id) ->
                        Expect.equal name "Alice" "Agent name should be Alice"
                        Expect.equal id "alice-123" "Agent id should be alice-123"
                    | other -> failtest $"Expected Person agent, got {other}"

                    Expect.equal record.Agent.Id "urn:frank:agent:person:alice-123" "Agent URN should use person scheme"
                }

                test "None user produces SoftwareAgent system" {
                    let store = new MockProvenanceStore()
                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>

                    observer.OnNext(EventBuilder.Create())

                    Expect.equal store.RecordCount 1 "Should have one record"
                    let record = store.Records.[0]

                    match record.Agent.AgentType with
                    | AgentType.SoftwareAgent(id) -> Expect.equal id "system" "Agent identifier should be 'system'"
                    | other -> failtest $"Expected SoftwareAgent, got {other}"

                    Expect.equal record.Agent.Id "urn:frank:agent:system" "Agent URN should be system"
                }

                test "unauthenticated principal produces SoftwareAgent system" {
                    let store = new MockProvenanceStore()
                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>
                    let user = makeUnauthenticatedPrincipal ()

                    observer.OnNext(EventBuilder.Create(user = user))

                    Expect.equal store.RecordCount 1 "Should have one record"
                    let record = store.Records.[0]

                    match record.Agent.AgentType with
                    | AgentType.SoftwareAgent(id) ->
                        Expect.equal id "system" "Unauthenticated principal should map to system agent"
                    | other -> failtest $"Expected SoftwareAgent for unauthenticated principal, got {other}"

                    Expect.equal record.Agent.Id "urn:frank:agent:system" "Agent URN should be system"
                }

                test "X-Agent-Type llm header produces LlmAgent" {
                    let store = new MockProvenanceStore()
                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>
                    let user = makeAuthenticatedPrincipal "bot-1" "bot-1"
                    let headers = Map.ofList [ "X-Agent-Type", "llm" ]

                    observer.OnNext(EventBuilder.Create(user = user, headers = headers))

                    Expect.equal store.RecordCount 1 "Should have one record"
                    let record = store.Records.[0]

                    match record.Agent.AgentType with
                    | AgentType.LlmAgent(id, model) ->
                        Expect.equal id "bot-1" "LLM agent identifier should be bot-1"
                        Expect.equal model None "Model should be None when X-Agent-Model header is absent"
                    | other -> failtest $"Expected LlmAgent, got {other}"

                    Expect.equal record.Agent.Id "urn:frank:agent:llm:bot-1" "Agent URN should use llm scheme"
                }

                test "X-Agent-Model header is captured in LlmAgent" {
                    let store = new MockProvenanceStore()
                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>
                    let user = makeAuthenticatedPrincipal "bot-2" "bot-2"

                    let headers = Map.ofList [ "X-Agent-Type", "llm"; "X-Agent-Model", "gpt-4o" ]

                    observer.OnNext(EventBuilder.Create(user = user, headers = headers))

                    Expect.equal store.RecordCount 1 "Should have one record"
                    let record = store.Records.[0]

                    match record.Agent.AgentType with
                    | AgentType.LlmAgent(id, model) ->
                        Expect.equal id "bot-2" "LLM agent identifier should be bot-2"
                        Expect.equal model (Some "gpt-4o") "Model should be captured from X-Agent-Model header"
                    | other -> failtest $"Expected LlmAgent with model, got {other}"
                } ]

          testList
              "Record construction"
              [ test "record fields are correctly mapped from event" {
                    let store = new MockProvenanceStore()
                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>
                    let user = makeAuthenticatedPrincipal "Bob" "bob-42"
                    let timestamp = DateTimeOffset(2025, 8, 15, 10, 30, 0, TimeSpan.Zero)

                    let event =
                        EventBuilder.Create(
                            user = user,
                            previousState = "Pending",
                            newState = "Approved",
                            eventName = "approve",
                            resourceUri = "/invoices/42",
                            httpMethod = "PUT",
                            instanceId = "inst-99",
                            timestamp = timestamp
                        )

                    observer.OnNext(event)

                    Expect.equal store.RecordCount 1 "Should have one record"
                    let record = store.Records.[0]

                    // Record-level fields
                    Expect.equal record.ResourceUri "/invoices/42" "ResourceUri should match"
                    Expect.equal record.RecordedAt timestamp "RecordedAt should match event timestamp"

                    Expect.isTrue
                        (record.Id.StartsWith("urn:frank:record:"))
                        "Record Id should use urn:frank:record: scheme"

                    // Activity fields
                    Expect.equal record.Activity.HttpMethod "PUT" "Activity HttpMethod should match"
                    Expect.equal record.Activity.ResourceUri "/invoices/42" "Activity ResourceUri should match"
                    Expect.equal record.Activity.EventName "approve" "Activity EventName should match"
                    Expect.equal record.Activity.PreviousState "Pending" "Activity PreviousState should match"
                    Expect.equal record.Activity.NewState "Approved" "Activity NewState should match"
                    Expect.equal record.Activity.StartedAt timestamp "Activity StartedAt should match"
                    Expect.equal record.Activity.EndedAt timestamp "Activity EndedAt should match"

                    Expect.isTrue
                        (record.Activity.Id.StartsWith("urn:frank:activity:"))
                        "Activity Id should use urn:frank:activity: scheme"

                    // Used entity (previous state)
                    Expect.equal record.UsedEntity.ResourceUri "/invoices/42" "UsedEntity ResourceUri should match"

                    Expect.equal
                        record.UsedEntity.StateName
                        "Pending"
                        "UsedEntity StateName should be the previous state"

                    Expect.equal record.UsedEntity.CapturedAt timestamp "UsedEntity CapturedAt should match"

                    Expect.isTrue
                        (record.UsedEntity.Id.StartsWith("urn:frank:entity:"))
                        "UsedEntity Id should use urn:frank:entity: scheme"

                    // Generated entity (new state)
                    Expect.equal
                        record.GeneratedEntity.ResourceUri
                        "/invoices/42"
                        "GeneratedEntity ResourceUri should match"

                    Expect.equal
                        record.GeneratedEntity.StateName
                        "Approved"
                        "GeneratedEntity StateName should be the new state"

                    Expect.equal record.GeneratedEntity.CapturedAt timestamp "GeneratedEntity CapturedAt should match"

                    Expect.isTrue
                        (record.GeneratedEntity.Id.StartsWith("urn:frank:entity:"))
                        "GeneratedEntity Id should use urn:frank:entity: scheme"

                    // Agent fields
                    Expect.equal record.Agent.Id "urn:frank:agent:person:bob-42" "Agent Id should match"

                    match record.Agent.AgentType with
                    | AgentType.Person(name, id) ->
                        Expect.equal name "Bob" "Agent name should be Bob"
                        Expect.equal id "bob-42" "Agent identifier should be bob-42"
                    | other -> failtest $"Expected Person agent, got {other}"
                } ]

          testList
              "Error resilience"
              [ test "store throws on Append - no exception propagates" {
                    let store =
                        new MockProvenanceStore(shouldThrow = InvalidOperationException("Store error"))

                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>

                    // Should not throw
                    observer.OnNext(EventBuilder.Create())

                    Expect.equal store.RecordCount 0 "No records should be stored when Append throws"
                }

                test "disposed store - no exception propagates" {
                    let store = new MockProvenanceStore()
                    store.MarkDisposed()
                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>

                    // Should not throw (ObjectDisposedException is caught)
                    observer.OnNext(EventBuilder.Create())
                }

                test "OnError does not propagate" {
                    let store = new MockProvenanceStore()
                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>

                    // Should not throw
                    observer.OnError(InvalidOperationException("Stream error"))
                }

                test "OnCompleted does not throw" {
                    let store = new MockProvenanceStore()
                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>

                    // Should not throw
                    observer.OnCompleted()
                } ]

          testList
              "Multiple events"
              [ test "multiple events produce correct count of records" {
                    let store = new MockProvenanceStore()
                    let logger = createLogger ()
                    let observer = TransitionObserver(store, logger) :> IObserver<TransitionEvent>
                    let baseTime = DateTimeOffset(2025, 7, 1, 12, 0, 0, TimeSpan.Zero)

                    for i in 1..5 do
                        observer.OnNext(
                            EventBuilder.Create(
                                instanceId = $"inst-{i}",
                                resourceUri = $"/orders/{i}",
                                timestamp = baseTime.AddSeconds(float i)
                            )
                        )

                    Expect.equal store.RecordCount 5 "Should have 5 records after 5 events"

                    // Each record should have a unique Id
                    let ids = store.Records |> List.map (fun r -> r.Id) |> Set.ofList
                    Expect.equal ids.Count 5 "All record IDs should be unique"

                    // Each record should have matching resource URIs
                    for i in 1..5 do
                        let record = store.Records.[i - 1]
                        Expect.equal record.ResourceUri $"/orders/{i}" $"Record {i} resource URI should match"
                } ] ]
