namespace Frank.Cli.Core.State

open System
open System.Text
open VDS.RDF

type DiffType =
    | Added
    | Removed
    | Modified

module DiffType =
    let toString =
        function
        | Added -> "Added"
        | Removed -> "Removed"
        | Modified -> "Modified"

type DiffEntry =
    { Type: DiffType
      Uri: Uri
      Label: string option
      Field: string option
      From: string option
      To: string option }

type DiffResult =
    { Added: DiffEntry list
      Removed: DiffEntry list
      Modified: DiffEntry list }

module DiffEngine =

    let private tripleKey (t: Triple) =
        (t.Subject.ToString(), t.Predicate.ToString())

    let private tripleToString (t: Triple) = t.Object.ToString()

    let private nodeUri (node: INode) : Uri =
        match node with
        | :? IUriNode as u -> u.Uri
        | _ -> Uri("urn:blank:" + node.ToString())

    let private entryFromTriple (diffType: DiffType) (t: Triple) : DiffEntry =
        { Type = diffType
          Uri = nodeUri t.Subject
          Label = None
          Field = Some(t.Predicate.ToString())
          From = None
          To = Some(t.Object.ToString()) }

    let diffGraphs (oldGraph: IGraph) (newGraph: IGraph) : DiffResult =
        let oldTriples = oldGraph.Triples |> Seq.toList
        let newTriples = newGraph.Triples |> Seq.toList

        let oldSet =
            oldTriples
            |> List.map (fun t -> (t.Subject.ToString(), t.Predicate.ToString(), t.Object.ToString()))
            |> Set.ofList

        let newSet =
            newTriples
            |> List.map (fun t -> (t.Subject.ToString(), t.Predicate.ToString(), t.Object.ToString()))
            |> Set.ofList

        // Triples in new but not in old
        let addedTripleKeys = Set.difference newSet oldSet

        // Triples in old but not in new
        let removedTripleKeys = Set.difference oldSet newSet

        // Group by (subject, predicate) to detect modifications
        let addedByKey =
            addedTripleKeys
            |> Set.toList
            |> List.groupBy (fun (s, p, _) -> (s, p))
            |> Map.ofList

        let removedByKey =
            removedTripleKeys
            |> Set.toList
            |> List.groupBy (fun (s, p, _) -> (s, p))
            |> Map.ofList

        let allKeys =
            Set.union (addedByKey |> Map.keys |> Set.ofSeq) (removedByKey |> Map.keys |> Set.ofSeq)

        let mutable added = []
        let mutable removed = []
        let mutable modified = []

        for key in allKeys do
            let (s, p) = key
            let hasAdded = Map.containsKey key addedByKey
            let hasRemoved = Map.containsKey key removedByKey

            let subjectUri =
                try
                    Uri(s)
                with _ ->
                    Uri("urn:blank:" + s)

            if hasAdded && hasRemoved then
                // Modified: same subject+predicate but different object
                let removedObjs = removedByKey.[key] |> List.map (fun (_, _, o) -> o)
                let addedObjs = addedByKey.[key] |> List.map (fun (_, _, o) -> o)

                modified <-
                    { Type = Modified
                      Uri = subjectUri
                      Label = None
                      Field = Some p
                      From = Some(String.concat ", " removedObjs)
                      To = Some(String.concat ", " addedObjs) }
                    :: modified
            elif hasAdded then
                let objs = addedByKey.[key]

                for (_, _, o) in objs do
                    added <-
                        { Type = Added
                          Uri = subjectUri
                          Label = None
                          Field = Some p
                          From = None
                          To = Some o }
                        :: added
            else
                let objs = removedByKey.[key]

                for (_, _, o) in objs do
                    removed <-
                        { Type = Removed
                          Uri = subjectUri
                          Label = None
                          Field = Some p
                          From = Some o
                          To = None }
                        :: removed

        { Added = List.rev added
          Removed = List.rev removed
          Modified = List.rev modified }

    let diffStates (oldState: ExtractionState) (newState: ExtractionState) : DiffResult =
        let ontoDiff = diffGraphs oldState.Ontology newState.Ontology
        let shapesDiff = diffGraphs oldState.Shapes newState.Shapes

        { Added = ontoDiff.Added @ shapesDiff.Added
          Removed = ontoDiff.Removed @ shapesDiff.Removed
          Modified = ontoDiff.Modified @ shapesDiff.Modified }

    let formatDiff (diff: DiffResult) : string =
        let sb = StringBuilder()

        if not (List.isEmpty diff.Added) then
            sb.AppendLine("Added:") |> ignore

            for entry in diff.Added do
                let field = entry.Field |> Option.defaultValue ""
                let toVal = entry.To |> Option.defaultValue ""
                sb.AppendLine($"  + {entry.Uri} [{field}] = {toVal}") |> ignore

        if not (List.isEmpty diff.Removed) then
            sb.AppendLine("Removed:") |> ignore

            for entry in diff.Removed do
                let field = entry.Field |> Option.defaultValue ""
                let fromVal = entry.From |> Option.defaultValue ""
                sb.AppendLine($"  - {entry.Uri} [{field}] = {fromVal}") |> ignore

        if not (List.isEmpty diff.Modified) then
            sb.AppendLine("Modified:") |> ignore

            for entry in diff.Modified do
                let field = entry.Field |> Option.defaultValue ""
                let fromVal = entry.From |> Option.defaultValue ""
                let toVal = entry.To |> Option.defaultValue ""
                sb.AppendLine($"  ~ {entry.Uri} [{field}]: {fromVal} -> {toVal}") |> ignore

        if
            List.isEmpty diff.Added
            && List.isEmpty diff.Removed
            && List.isEmpty diff.Modified
        then
            sb.AppendLine("No changes detected.") |> ignore

        sb.ToString().TrimEnd()
