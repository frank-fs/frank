namespace Frank.Semantic

open System
open VDS.RDF
open Frank.Cli.Core

// ── Jaro-Winkler implementation ───────────────────────────────────────────────

module private JaroWinkler =

    let private jaro (s1: string) (s2: string) : float =
        let len1 = s1.Length
        let len2 = s2.Length

        if len1 = 0 && len2 = 0 then
            1.0
        elif len1 = 0 || len2 = 0 then
            0.0
        else

            let matchWindow = max (max len1 len2 / 2 - 1) 0
            let s1Matched = Array.create len1 false
            let s2Matched = Array.create len2 false
            let mutable matches = 0
            let mutable transpositions = 0

            for i in 0 .. len1 - 1 do
                let start = max 0 (i - matchWindow)
                let finish = min (i + matchWindow + 1) len2

                let mutable found = false
                let mutable j = start

                while not found && j < finish do
                    if not s2Matched.[j] && s1.[i] = s2.[j] then
                        s1Matched.[i] <- true
                        s2Matched.[j] <- true
                        matches <- matches + 1
                        found <- true

                    j <- j + 1

            if matches = 0 then
                0.0
            else

                let mutable k = 0

                for i in 0 .. len1 - 1 do
                    if s1Matched.[i] then
                        while not s2Matched.[k] do
                            k <- k + 1

                        if s1.[i] <> s2.[k] then
                            transpositions <- transpositions + 1

                        k <- k + 1

                let m = float matches
                (m / float len1 + m / float len2 + (m - float transpositions / 2.0) / m) / 3.0

    let similarity (s1: string) (s2: string) : float =
        let jaroScore = jaro s1 s2

        let prefixLen =
            min 4 (Seq.zip s1 s2 |> Seq.takeWhile (fun (a, b) -> a = b) |> Seq.length)

        jaroScore + float prefixLen * 0.1 * (1.0 - jaroScore)

// ── Name normalization ────────────────────────────────────────────────────────

module private Normalize =

    let private suffixes = [| "Dto"; "Model"; "Record"; "Entity"; "View" |]

    let private stripSuffix (name: string) : string =
        suffixes
        |> Array.tryPick (fun s ->
            if name.EndsWith(s, StringComparison.Ordinal) && name.Length > s.Length then
                Some(name.Substring(0, name.Length - s.Length))
            else
                None)
        |> Option.defaultValue name

    let private splitPascalCase (name: string) : string list =
        if String.IsNullOrEmpty name then
            []
        else
            let mutable tokens = []
            let mutable start = 0

            for i in 1 .. name.Length - 1 do
                if Char.IsUpper(name.[i]) then
                    tokens <- name.Substring(start, i - start) :: tokens
                    start <- i

            tokens <- name.Substring(start) :: tokens
            tokens |> List.rev

    let typeName (name: string) : string =
        name
        |> stripSuffix
        |> splitPascalCase
        |> List.map (_.ToLowerInvariant())
        |> String.concat ""

    let localName (uri: string) : string =
        let trimmed = uri.TrimEnd('/')
        let idx = max (trimmed.LastIndexOf('#')) (trimmed.LastIndexOf('/'))
        if idx >= 0 then trimmed.Substring(idx + 1) else trimmed

// ── Vocabulary class extraction ───────────────────────────────────────────────

module private GraphQuery =

    let private rdfType =
        UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#type")

    let private owlClass = UriFactory.Create("http://www.w3.org/2002/07/owl#Class")

    let private rdfsClass =
        UriFactory.Create("http://www.w3.org/2000/01/rdf-schema#Class")

    let private owlDatatypeProperty =
        UriFactory.Create("http://www.w3.org/2002/07/owl#DatatypeProperty")

    let private owlObjectProperty =
        UriFactory.Create("http://www.w3.org/2002/07/owl#ObjectProperty")

    let private rdfProperty =
        UriFactory.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#Property")

    let private schemaDomainIncludes =
        UriFactory.Create("https://schema.org/domainIncludes")

    let classesInScope (registry: VocabularyRegistry) (graph: IGraph) : (string * string) list =
        let rdfTypeNode = graph.CreateUriNode(rdfType)
        let owlClassNode = graph.CreateUriNode(owlClass)
        let rdfsClassNode = graph.CreateUriNode(rdfsClass)

        let isInScope (subjectUri: string) =
            registry.Using
            |> Set.exists (fun prefix ->
                match registry.Prefixes |> Map.tryFind prefix with
                | None -> false
                | Some baseUri -> subjectUri.StartsWith(baseUri.ToString(), StringComparison.Ordinal))

        let collectClasses (classNode: INode) =
            graph.GetTriplesWithPredicateObject(rdfTypeNode, classNode)
            |> Seq.choose (fun t ->
                match t.Subject with
                | :? IUriNode as u ->
                    let uri = u.Uri.ToString()

                    if isInScope uri then
                        let localName = Normalize.localName uri
                        Some(localName, uri)
                    else
                        None
                | _ -> None)

        [ yield! collectClasses owlClassNode; yield! collectClasses rdfsClassNode ]
        |> List.distinctBy fst

    let propertiesForClass (graph: IGraph) (classUri: string) : string list =
        let classNode =
            try
                Some(graph.CreateUriNode(UriFactory.Create(classUri)))
            with _ ->
                None

        match classNode with
        | None -> []
        | Some cn ->
            let schemaDomainNode =
                try
                    Some(graph.CreateUriNode(schemaDomainIncludes))
                with _ ->
                    None

            match schemaDomainNode with
            | None -> []
            | Some domainNode ->
                graph.GetTriplesWithPredicateObject(domainNode, cn)
                |> Seq.choose (fun t ->
                    match t.Subject with
                    | :? IUriNode as u -> Some(Normalize.localName (u.Uri.ToString()))
                    | _ -> None)
                |> List.ofSeq

// ── Scoring ───────────────────────────────────────────────────────────────────

module private Scoring =

    let private fieldOverlapRatio (fields: FieldInfo list) (classProperties: string list) : float =
        if List.isEmpty fields || List.isEmpty classProperties then
            0.0
        else

            let fieldNames =
                fields
                |> List.collect (fun f ->
                    let base' = [ f.Name.ToLowerInvariant() ]

                    let jsonName =
                        f.Attributes
                        |> Map.tryFind "JsonPropertyName"
                        |> Option.map (fun v -> v.ToLowerInvariant())
                        |> Option.toList

                    base' @ jsonName)
                |> Set.ofList

            let propNames = classProperties |> List.map (_.ToLowerInvariant()) |> Set.ofList

            let overlap = Set.intersect fieldNames propNames |> Set.count
            let total = Set.union fieldNames propNames |> Set.count

            if total = 0 then 0.0
            // Integer-division rule: use multiplication not division
            elif overlap * 2 >= total then float overlap / float total
            else float overlap / float total

    type ClassCandidate =
        { LocalName: string
          Iri: string
          TypeScore: float
          FieldScore: float
          Combined: float }

    // Minimum type-name similarity to be considered a candidate at all.
    let private minTypeScore = 0.5

    let rankCandidates
        (typeNorm: string)
        (fields: FieldInfo list)
        (classes: (string * string) list)
        (graph: IGraph)
        : ClassCandidate list =
        classes
        |> List.choose (fun (localName, iri) ->
            let classNorm = localName.ToLowerInvariant()
            let typeScore = JaroWinkler.similarity typeNorm classNorm

            if typeScore < minTypeScore then
                None
            else

                let props = GraphQuery.propertiesForClass graph iri

                let combined =
                    if List.isEmpty fields || List.isEmpty props then
                        typeScore
                    else
                        let fieldScore = fieldOverlapRatio fields props
                        0.6 * typeScore + 0.4 * fieldScore

                let fieldScore =
                    if List.isEmpty fields then
                        0.0
                    else
                        fieldOverlapRatio fields props

                Some
                    { LocalName = localName
                      Iri = iri
                      TypeScore = typeScore
                      FieldScore = fieldScore
                      Combined = combined })
        |> List.sortByDescending _.Combined
        |> List.truncate 3

// ── Public module ─────────────────────────────────────────────────────────────

module ConventionEngine =

    let private confirmedLlmOrManual (existing: TypeMapping list) : Map<string, TypeMapping> =
        existing
        |> List.choose (fun m ->
            match m.Status, m.Source with
            | Confirmed, (Llm | Manual) -> Some(m.FsharpType, m)
            | _ -> None)
        |> Map.ofList

    let private resolveOne
        (registry: VocabularyRegistry)
        (graph: IGraph)
        (classes: (string * string) list)
        (typeInfo: TypeInfo)
        : TypeMapping =
        let norm = Normalize.typeName typeInfo.LocalName
        let candidates = Scoring.rankCandidates norm typeInfo.Fields classes graph

        match candidates with
        | [] ->
            { FsharpType = typeInfo.FullName
              Iri = ""
              Confidence = 0.0
              Source = Convention
              Status = Unresolved
              Fields = [] }
        | best :: _ ->
            let status = if best.Combined >= 0.85 then Confirmed else Proposed

            { FsharpType = typeInfo.FullName
              Iri = best.Iri
              Confidence = best.Combined
              Source = Convention
              Status = status
              Fields = [] }

    /// Match a list of TypeInfos against vocabulary classes.
    /// Existing lock file mappings with source = Llm | Manual and status = Confirmed are preserved unchanged.
    let matchTypes
        (registry: VocabularyRegistry)
        (graph: IGraph)
        (existingMappings: TypeMapping list)
        (types: TypeInfo list)
        : TypeMapping list =
        let preserved = confirmedLlmOrManual existingMappings
        let classes = GraphQuery.classesInScope registry graph

        types
        |> List.map (fun t ->
            match preserved |> Map.tryFind t.FullName with
            | Some existing -> existing
            | None -> resolveOne registry graph classes t)
