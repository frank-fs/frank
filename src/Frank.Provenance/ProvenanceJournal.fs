namespace Frank.Provenance

open System
open System.IO
open System.Text.Json
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Writing

type IProvenanceJournal =
    abstract Append: graph: IGraph -> unit
    abstract Snapshot: graphs: IGraph seq -> unit
    abstract Recover: unit -> IGraph seq

// Not marked `private`/`internal`: System.Text.Json's reflection-based deserializer needs this
// record's generated constructor to be public. Omitting Manifest from ProvenanceJournal.fsi already
// makes it inaccessible outside this module -- adding `private` here would additionally make the
// constructor non-public and break JSON deserialization for no encapsulation benefit.
type Manifest =
    { LatestSnapshot: int
      NextSnapshotSeq: int
      JournalSegmentsSince: int[]
      NextSegmentSeq: int }

module Manifest =
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
        let segmentsJson = String.concat ", " (manifest.JournalSegmentsSince |> Array.map (fun x -> x.ToString()))
        let json = sprintf "{\n  \"latestSnapshot\": %d,\n  \"nextSnapshotSeq\": %d,\n  \"journalSegmentsSince\": [%s],\n  \"nextSegmentSeq\": %d\n}" manifest.LatestSnapshot manifest.NextSnapshotSeq segmentsJson manifest.NextSegmentSeq
        File.WriteAllText(path, json)

type JournalMessage =
    | AppendSegment of IGraph
    | TakeSnapshot of IGraph list
    | Flush of AsyncReplyChannel<unit>

[<Sealed>]
type FileProvenanceJournal(baseDirectory: string, actorId: string) =
    do Directory.CreateDirectory(baseDirectory) |> ignore

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

    let agent =
        MailboxProcessor<JournalMessage>.Start(fun inbox ->
            let rec loop (manifest: Manifest) =
                async {
                    let! msg = inbox.Receive()

                    match msg with
                    | AppendSegment graph ->
                        let seqNum = manifest.NextSegmentSeq
                        writeGraphs (segmentPath seqNum) [ graph ]

                        let updated =
                            { manifest with
                                JournalSegmentsSince = Array.append manifest.JournalSegmentsSince [| seqNum |]
                                NextSegmentSeq = seqNum + 1 }

                        Manifest.save manifestPath updated
                        return! loop updated

                    | TakeSnapshot graphs ->
                        let seqNum = manifest.NextSnapshotSeq
                        writeGraphs (snapshotPath seqNum) graphs

                        let updated =
                            { manifest with
                                LatestSnapshot = seqNum
                                NextSnapshotSeq = seqNum + 1
                                JournalSegmentsSince = [||] }

                        Manifest.save manifestPath updated
                        return! loop updated

                    | Flush reply ->
                        reply.Reply(())
                        return! loop manifest
                }

            loop (Manifest.load manifestPath))

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
