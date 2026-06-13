module Frank.Provenance.ProvenanceStore

open System

/// Represents a recorded PROV-O activity for an HTTP request.
type HttpActivity =
    { ActivityId: string
      Method: string
      Path: string
      StartTime: DateTimeOffset
      EndTime: DateTimeOffset option
      Principal: string option
      ProvOClass: string
      TypeIri: string option }

type StoreMessage =
    | Add of HttpActivity
    | Get of string * AsyncReplyChannel<HttpActivity option>
    | GetAll of AsyncReplyChannel<HttpActivity list>

/// Concurrent in-memory store backed by MailboxProcessor.
type ProvenanceStore() =
    let agent =
        MailboxProcessor.Start(fun inbox ->
            let rec loop (store: Map<string, HttpActivity>) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | Add activity -> return! loop (Map.add activity.ActivityId activity store)
                    | Get(id, reply) ->
                        reply.Reply(Map.tryFind id store)
                        return! loop store
                    | GetAll reply ->
                        reply.Reply(Map.toList store |> List.map snd)
                        return! loop store
                }

            loop Map.empty)

    member _.Add(activity: HttpActivity) = agent.Post(Add activity)

    member _.Get(id: string) =
        agent.PostAndAsyncReply(fun ch -> Get(id, ch)) |> Async.StartAsTask

    member _.GetAll() =
        agent.PostAndAsyncReply(GetAll) |> Async.StartAsTask

/// Serialize an HttpActivity to JSON-LD string.
let serializeActivity (activity: HttpActivity) : string =
    let prov = "http://www.w3.org/ns/prov#"
    let xsd = "http://www.w3.org/2001/XMLSchema#"
    let activityType = activity.ProvOClass

    let typeArray =
        match activity.TypeIri with
        | Some iri -> $"[\"{activityType}\", \"{iri}\"]"
        | None -> $"[\"{activityType}\"]"

    let timeStr = activity.StartTime.ToString("o")

    let agentPart =
        match activity.Principal with
        | Some p ->
            $""",
  "prov:wasAssociatedWith": {{"@type": "{prov}Agent", "@id": "urn:frank:agent:{p}"}}"""
        | None -> ""

    $"""{{
  "@context": {{"prov": "{prov}"}},
  "@id": "urn:frank:activity:{activity.ActivityId}",
  "@type": {typeArray},
  "{prov}startedAtTime": {{"@value": "{timeStr}", "@type": "{xsd}dateTime"}},
  "{prov}atLocation": "{activity.Method} {activity.Path}"{agentPart}
}}"""
