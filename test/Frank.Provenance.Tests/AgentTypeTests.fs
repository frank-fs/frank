module Frank.Provenance.Tests.AgentTypeTests

open System
open System.Security.Claims
open Expecto
open Frank.Provenance
open VDS.RDF

type MockStore() =
    let records = ResizeArray<ProvenanceRecord>()
    member _.Records = records |> Seq.toList

    interface IProvenanceStore with
        member _.Append(r) = records.Add(r)
        member _.QueryByResource(_) = Threading.Tasks.Task.FromResult([])
        member _.QueryByAgent(_) = Threading.Tasks.Task.FromResult([])
        member _.QueryByTimeRange(_, _) = Threading.Tasks.Task.FromResult([])

    interface IDisposable with
        member _.Dispose() = ()

let private makeEvent (user: ClaimsPrincipal option) (headers: Map<string, string>) =
    { TransitionEvent.InstanceId = "inst-1"
      ResourceUri = "/items/1"
      PreviousState = "Draft"
      NewState = "Submitted"
      Event = "submit"
      Timestamp = DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
      User = user
      HttpMethod = "POST"
      Headers = headers
      Roles = [] }

let private authenticatedPrincipal name identifier =
    let claims =
        [ Claim(ClaimTypes.Name, name); Claim(ClaimTypes.NameIdentifier, identifier) ]

    ClaimsPrincipal(ClaimsIdentity(claims, "TestAuth"))

let private minimalPrincipal () =
    ClaimsPrincipal(ClaimsIdentity([], "TestAuth"))

let private hasTriple (graph: IGraph) (subjectUri: string) (predicateUri: string) (objectUri: string) =
    let s = graph.CreateUriNode(UriFactory.Create(subjectUri))
    let p = graph.CreateUriNode(UriFactory.Create(predicateUri))
    let o = graph.CreateUriNode(UriFactory.Create(objectUri))
    graph.ContainsTriple(Triple(s, p, o))

let private hasLiteralTriple (graph: IGraph) (subjectUri: string) (predicateUri: string) (value: string) =
    let s = graph.CreateUriNode(UriFactory.Create(subjectUri))
    let p = graph.CreateUriNode(UriFactory.Create(predicateUri))
    let o = graph.CreateLiteralNode(value)
    graph.ContainsTriple(Triple(s, p, o))

let private runObserver (event: TransitionEvent) =
    let store = new MockStore()

    let logger =
        Microsoft.Extensions.Logging.Abstractions.NullLogger<TransitionObserver>.Instance

    let observer = TransitionObserver(store, logger)
    (observer :> IObserver<TransitionEvent>).OnNext(event)
    let record = store.Records |> List.head
    let graph = GraphBuilder.toGraph [ record ]
    (record, graph)

[<Tests>]
let agentTypeTests =
    testList
        "AgentTypeDiscrimination"
        [ test "authenticated user produces Person with correct RDF triples" {
              let principal = authenticatedPrincipal "Jane Doe" "jane@example.com"
              let event = makeEvent (Some principal) Map.empty
              let (record, graph) = runObserver event

              match record.Agent.AgentType with
              | AgentType.Person(name, id) ->
                  Expect.equal name "Jane Doe" "name"
                  Expect.equal id "jane@example.com" "identifier"
              | _ -> failtest "expected Person"

              Expect.isTrue
                  (hasTriple graph record.Agent.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.Person)
                  "rdf:type prov:Person"

              Expect.isTrue (hasLiteralTriple graph record.Agent.Id ProvVocabulary.Rdfs.label "Jane Doe") "rdfs:label"
          }

          test "unauthenticated (None user) produces SoftwareAgent system" {
              let event = makeEvent None Map.empty
              let (record, graph) = runObserver event

              match record.Agent.AgentType with
              | AgentType.SoftwareAgent id -> Expect.equal id "system" "system agent"
              | _ -> failtest "expected SoftwareAgent"

              Expect.isTrue
                  (hasTriple graph record.Agent.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.SoftwareAgent)
                  "rdf:type prov:SoftwareAgent"
          }

          test "LLM origin produces dual-typed agent with model triple" {
              let principal = authenticatedPrincipal "bot" "bot-001"

              let headers =
                  Map.ofList [ ("X-Agent-Type", "llm"); ("X-Agent-Model", "claude-opus-4") ]

              let event = makeEvent (Some principal) headers
              let (record, graph) = runObserver event

              match record.Agent.AgentType with
              | AgentType.LlmAgent(id, model) ->
                  Expect.equal id "bot-001" "identifier from claims"
                  Expect.equal model (Some "claude-opus-4") "model"
              | _ -> failtest "expected LlmAgent"

              Expect.isTrue
                  (hasTriple graph record.Agent.Id ProvVocabulary.Rdf.Type ProvVocabulary.Class.SoftwareAgent)
                  "rdf:type prov:SoftwareAgent"

              Expect.isTrue
                  (hasTriple graph record.Agent.Id ProvVocabulary.Rdf.Type ProvVocabulary.Frank.LlmAgent)
                  "rdf:type frank:LlmAgent"

              Expect.isTrue
                  (hasLiteralTriple graph record.Agent.Id ProvVocabulary.Frank.agentModel "claude-opus-4")
                  "frank:agentModel"
          }

          test "LLM without model has no agentModel triple" {
              let principal = authenticatedPrincipal "bot" "bot-001"
              let headers = Map.ofList [ ("X-Agent-Type", "llm") ]
              let event = makeEvent (Some principal) headers
              let (record, graph) = runObserver event

              match record.Agent.AgentType with
              | AgentType.LlmAgent(_, model) -> Expect.isNone model "no model"
              | _ -> failtest "expected LlmAgent"

              let agentNode = graph.CreateUriNode(UriFactory.Create(record.Agent.Id))

              let modelPred =
                  graph.CreateUriNode(UriFactory.Create(ProvVocabulary.Frank.agentModel))

              let modelTriples = graph.GetTriplesWithSubjectPredicate(agentNode, modelPred)
              Expect.isEmpty modelTriples "no agentModel triple"
          }

          test "anonymous principal (IsAuthenticated=false) produces SoftwareAgent" {
              let principal = ClaimsPrincipal()
              let event = makeEvent (Some principal) Map.empty
              let (record, _) = runObserver event

              match record.Agent.AgentType with
              | AgentType.SoftwareAgent id -> Expect.equal id "system" "system"
              | _ -> failtest "expected SoftwareAgent"
          }

          test "minimal claims produces Person with Unknown name" {
              let principal = minimalPrincipal ()
              let event = makeEvent (Some principal) Map.empty
              let (record, _) = runObserver event

              match record.Agent.AgentType with
              | AgentType.Person(name, _) -> Expect.equal name "Unknown" "Unknown name"
              | _ -> failtest "expected Person"
          }

          test "unknown X-Agent-Type robot is ignored, treated as Person" {
              let principal = authenticatedPrincipal "Jane" "jane"
              let headers = Map.ofList [ ("X-Agent-Type", "robot") ]
              let event = makeEvent (Some principal) headers
              let (record, _) = runObserver event

              match record.Agent.AgentType with
              | AgentType.Person _ -> ()
              | _ -> failtest "expected Person (robot header ignored)"
          }

          test "unauthenticated LLM gets anonymous-llm identifier" {
              let headers = Map.ofList [ ("X-Agent-Type", "llm") ]
              let event = makeEvent None headers
              let (record, _) = runObserver event

              match record.Agent.AgentType with
              | AgentType.LlmAgent(id, _) -> Expect.equal id "anonymous-llm" "anonymous-llm"
              | _ -> failtest "expected LlmAgent"

              Expect.stringContains record.Agent.Id "anonymous-llm" "URN contains anonymous-llm"
          } ]
