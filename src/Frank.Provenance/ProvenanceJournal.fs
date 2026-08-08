namespace Frank.Provenance

open System
open System.IO
open System.Text.Json
open Microsoft.Extensions.Logging
open Microsoft.Extensions.Logging.Abstractions
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing

type IProvenanceJournal =
    abstract Append: graph: IGraph -> unit
    abstract Snapshot: graphs: IGraph seq -> unit
    abstract Recover: unit -> IGraph seq

// Manual JSON handling (JsonDocument for reading, sprintf-based construction for writing), not
// System.Text.Json.JsonSerializer's reflection-based (de)serialization: JsonSerializer.Deserialize
// on this record type -- with or without [<CLIMutable>] -- reproducibly throws "Deserialization of
// types without a parameterless constructor..." specifically under this repo's `dotnet test` VSTest
// execution host, despite round-tripping correctly in `dotnet fsi` and a plain `dotnet run` console
// app on the same TFM.
type private Manifest =
    { LatestSnapshot: int
      NextSnapshotSeq: int
      JournalSegmentsSince: int[]
      NextSegmentSeq: int }

module private Manifest =
    let empty =
        { LatestSnapshot = 0
          NextSnapshotSeq = 1
          JournalSegmentsSince = [||]
          NextSegmentSeq = 1 }

    let load (path: string) : Manifest =
        if File.Exists path then
            let json = File.ReadAllText path
            use doc = JsonDocument.Parse(json)
            let root = doc.RootElement
            let mutable prop = JsonElement()
            { LatestSnapshot = if root.TryGetProperty("latestSnapshot", &prop) then prop.GetInt32() else 0
              NextSnapshotSeq = if root.TryGetProperty("nextSnapshotSeq", &prop) then prop.GetInt32() else 1
              JournalSegmentsSince =
                  if root.TryGetProperty("journalSegmentsSince", &prop) then
                      prop.EnumerateArray()
                      |> Seq.map (fun e -> e.GetInt32())
                      |> Array.ofSeq
                  else
                      [||]
              NextSegmentSeq = if root.TryGetProperty("nextSegmentSeq", &prop) then prop.GetInt32() else 1 }
        else
            empty

    let save (path: string) (manifest: Manifest) : unit =
        let segmentsJson = String.concat ", " (manifest.JournalSegmentsSince |> Array.map string)
        let json = sprintf "{\n  \"latestSnapshot\": %d,\n  \"nextSnapshotSeq\": %d,\n  \"journalSegmentsSince\": [%s],\n  \"nextSegmentSeq\": %d\n}" manifest.LatestSnapshot manifest.NextSnapshotSeq segmentsJson manifest.NextSegmentSeq
        // Write-then-rename rather than File.WriteAllText straight onto `path`: WriteAllText
        // truncates before it writes, so a crash mid-write leaves a truncated manifest that
        // Manifest.load then throws on. The three-argument File.Move is an atomic replace on both
        // Windows and Linux, so a reader only ever sees the whole old file or the whole new one.
        let tempPath = path + ".tmp"
        File.WriteAllText(tempPath, json)
        File.Move(tempPath, path, true)

type private JournalMessage =
    | AppendSegment of IGraph
    | TakeSnapshot of IGraph list
    | Flush of AsyncReplyChannel<unit>

[<Sealed>]
type FileProvenanceJournal(baseDirectory: string, actorId: string, ?logger: ILogger) =
    do Directory.CreateDirectory(baseDirectory) |> ignore

    let logger = defaultArg logger (NullLogger.Instance :> ILogger)

    let manifestPath = Path.Combine(baseDirectory, sprintf "%s.manifest.json" actorId)
    let snapshotPath (seqNum: int) = Path.Combine(baseDirectory, sprintf "%s.snapshot.%d.nq" actorId seqNum)
    let segmentPath (seqNum: int) = Path.Combine(baseDirectory, sprintf "%s.journal.%d.nq" actorId seqNum)

    let writeGraphs (path: string) (graphs: IGraph seq) : unit =
        let store = new TripleStore()

        for g in graphs do
            store.Add(g, true) |> ignore

        use writer = new StreamWriter(path)
        NQuadsWriter().Save(store, writer, true)

    let readGraphs (path: string) : IGraph list =
        let store = new TripleStore()
        NQuadsParser().Load(store, path)
        [ for g in store.Graphs -> g ]

    // A write that throws (disk full, permission denied, an obstruction at the target path, a
    // corrupt manifest) must never escape the mailbox loop: an unhandled exception there stops
    // MailboxProcessor's Receive loop for good, so every later Append/Snapshot -- an agent.Post,
    // which never throws to its caller -- vanishes into a mailbox nobody reads while the store goes
    // on reporting success with no durability at all. Log, keep the last known-good manifest (never
    // a partially-updated one), and carry on serving the next message.
    let tryStep (operation: string) (lastKnownGood: Manifest) (step: unit -> Manifest) : Manifest =
        try
            step ()
        with ex ->
            logger.LogError(ex, "Frank.Provenance: journal {Operation} failed for actor {ActorId}; keeping the last known-good manifest and continuing", operation, actorId)
            lastKnownGood

    let appendSegment (manifest: Manifest) (graph: IGraph) : Manifest =
        let seqNum = manifest.NextSegmentSeq
        writeGraphs (segmentPath seqNum) [ graph ]

        let updated =
            { manifest with
                JournalSegmentsSince = Array.append manifest.JournalSegmentsSince [| seqNum |]
                NextSegmentSeq = seqNum + 1 }

        Manifest.save manifestPath updated
        updated

    let takeSnapshot (manifest: Manifest) (graphs: IGraph list) : Manifest =
        let seqNum = manifest.NextSnapshotSeq
        writeGraphs (snapshotPath seqNum) graphs

        let updated =
            { manifest with
                LatestSnapshot = seqNum
                NextSnapshotSeq = seqNum + 1
                JournalSegmentsSince = [||] }

        Manifest.save manifestPath updated
        updated

    let agent =
        MailboxProcessor<JournalMessage>.Start(fun inbox ->
            // The step result is computed inside tryStep and only then handed to `loop`, rather than
            // recursing from inside a try/with: an async `return!` under an exception handler keeps
            // that handler alive for the rest of the loop's life, stacking one more per message.
            let rec loop (manifest: Manifest) =
                async {
                    let! msg = inbox.Receive()

                    let next =
                        match msg with
                        | AppendSegment graph -> tryStep "append" manifest (fun () -> appendSegment manifest graph)
                        | TakeSnapshot graphs -> tryStep "snapshot" manifest (fun () -> takeSnapshot manifest graphs)
                        | Flush reply ->
                            // Always replies, whatever happened on prior messages: Flush is a
                            // PostAndReply with an infinite default timeout, so a missed Reply hangs
                            // its caller forever.
                            reply.Reply(())
                            manifest

                    return! loop next
                }

            loop (tryStep "manifest load" Manifest.empty (fun () -> Manifest.load manifestPath)))

    interface IProvenanceJournal with
        member _.Append(graph: IGraph) = agent.Post(AppendSegment graph)
        member _.Snapshot(graphs: IGraph seq) = agent.Post(TakeSnapshot(List.ofSeq graphs))

        member _.Recover() : IGraph seq =
            let manifest = Manifest.load manifestPath

            let snapshotGraphs =
                if manifest.LatestSnapshot > 0 then
                    readGraphs (snapshotPath manifest.LatestSnapshot)
                else
                    []

            let segmentGraphs =
                manifest.JournalSegmentsSince
                |> Array.toList
                |> List.collect (fun seqNum -> readGraphs (segmentPath seqNum))

            snapshotGraphs @ segmentGraphs

    member internal _.Flush() : unit = agent.PostAndReply Flush
