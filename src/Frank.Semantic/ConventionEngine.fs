namespace Frank.Semantic

open System
open VDS.RDF

/// Extracted class/property local names from a vocabulary IGraph.
/// Keys are lowercase local names; values are absolute IRI strings.
type VocabTerms =
    { Classes: Map<string, string>
      Properties: Map<string, string> }

module ConventionEngine =

    // ── Jaro distance ─────────────────────────────────────────────────────────

    let private countMatches (s: string) (t: string) (matchDist: int) : int * bool[] * bool[] =
        let sLen = s.Length
        let tLen = t.Length
        let sMatched = Array.create sLen false
        let tMatched = Array.create tLen false
        let mutable count = 0

        for i in 0 .. sLen - 1 do
            let lo = max 0 (i - matchDist)
            let hi = min (tLen - 1) (i + matchDist)
            let mutable j = lo

            while j <= hi do
                if not tMatched.[j] && s.[i] = t.[j] then
                    sMatched.[i] <- true
                    tMatched.[j] <- true
                    count <- count + 1
                    j <- hi + 1
                else
                    j <- j + 1

        count, sMatched, tMatched

    let private countTranspositions (s: string) (t: string) (sMatched: bool[]) (tMatched: bool[]) : int =
        let sLen = s.Length
        let tLen = t.Length
        let mutable tIdx = 0
        let mutable transpositions = 0

        for i in 0 .. sLen - 1 do
            if sMatched.[i] then
                while tIdx < tLen && not tMatched.[tIdx] do
                    tIdx <- tIdx + 1

                if tIdx < tLen && s.[i] <> t.[tIdx] then
                    transpositions <- transpositions + 1

                tIdx <- tIdx + 1

        transpositions

    /// Pure Jaro distance between two strings. Result in [0.0, 1.0].
    /// Null inputs treated as empty strings.
    let jaro (s: string) (t: string) : float =
        let s = if isNull s then "" else s
        let t = if isNull t then "" else t

        if s = t then
            1.0
        elif s.Length = 0 || t.Length = 0 then
            0.0
        else
            let matchDist = max (max (s.Length / 3) (t.Length / 3) - 1) 0
            let m, sMatched, tMatched = countMatches s t matchDist

            if m = 0 then
                0.0
            else
                let transpositions = countTranspositions s t sMatched tMatched
                let mf = float m

                (mf / float s.Length
                 + mf / float t.Length
                 + (mf - float transpositions / 2.0) / mf)
                / 3.0

    /// Winkler prefix boost applied to a Jaro score. Result in [0.0, 1.0].
    let winklerBoost (jaroScore: float) (s: string) (t: string) : float =
        let s = if isNull s then "" else s
        let t = if isNull t then "" else t
        let maxPrefix = min 4 (min s.Length t.Length)
        let mutable prefixLen = 0

        while prefixLen < maxPrefix && s.[prefixLen] = t.[prefixLen] do
            prefixLen <- prefixLen + 1

        jaroScore + (float prefixLen * 0.1 * (1.0 - jaroScore))

    /// Jaro-Winkler similarity between two strings. Result in [0.0, 1.0].
    /// Null inputs treated as empty strings.
    let jaroWinkler (s: string) (t: string) : float =
        let j = jaro s t
        if j = 0.0 then 0.0 else winklerBoost j s t

    // ── Name normalization ────────────────────────────────────────────────────

    let private suffixesToStrip = [| "Dto"; "Model"; "Record" |]

    let private stripKnownSuffix (name: string) : string =
        suffixesToStrip
        |> Array.tryFind (fun s -> name.EndsWith(s, StringComparison.Ordinal) && name.Length > s.Length)
        |> Option.map (fun s -> name.[.. name.Length - s.Length - 1])
        |> Option.defaultValue name

    let private splitPascalCase (name: string) : string list =
        let mutable tokens = []
        let mutable start = 0

        for i in 1 .. name.Length - 1 do
            let prev = name.[i - 1]
            let cur = name.[i]
            // boundary A: lower/digit → upper   (squareP|osition, point|X)
            let lowerToUpper = Char.IsUpper cur && not (Char.IsUpper prev)
            // boundary B: end of an acronym run → start of a capitalized word
            // (HTTPS|Config): cur is upper, prev is upper, and the char AFTER cur is lower
            let acronymToWord =
                Char.IsUpper cur
                && Char.IsUpper prev
                && i + 1 < name.Length
                && Char.IsLower name.[i + 1]

            if lowerToUpper || acronymToWord then
                tokens <- name.[start .. i - 1].ToLowerInvariant() :: tokens
                start <- i

        tokens <- name.[start .. name.Length - 1].ToLowerInvariant() :: tokens
        List.rev tokens

    /// PascalCase type/field name → lowercase tokens, with known suffixes stripped.
    let normalizeTokens (name: string) : string list =
        if String.IsNullOrEmpty name then
            []
        else
            name |> stripKnownSuffix |> splitPascalCase

    /// Join normalized tokens with a space (used for full-string JW comparison of attr values).
    let canonicalName (name: string) : string =
        normalizeTokens name |> String.concat " "

    /// Normalized-token key with no separator (for exact-identity comparison).
    let private normKey (name: string) : string =
        normalizeTokens name |> String.concat ""

    // ── Type name similarity ──────────────────────────────────────────────────

    /// Average JW of each type token against a class local name.
    /// Per-token average is correct; whole-string JW inverts the cases —
    /// "widgetforge" scores higher than "customerorderrecord" against "order" on the
    /// joined string (0.527 vs 0.586), ranking a no-match above a proposed match.
    let private tokenAverageScore (tokens: string list) (classLocalName: string) : float =
        if tokens.IsEmpty then
            0.0
        else
            tokens |> List.averageBy (fun tok -> jaroWinkler tok classLocalName)

    // ── Field scoring ─────────────────────────────────────────────────────────

    let private bestFieldName (field: FieldInfo) : string =
        field.Attributes
        |> Map.tryFind "JsonPropertyName"
        |> Option.map canonicalName
        |> Option.defaultWith (fun () -> canonicalName field.Name)

    let private fieldSimScore (field: FieldInfo) (properties: Map<string, string>) : float =
        if properties.IsEmpty then
            0.0
        else
            let name = bestFieldName field
            properties |> Map.toSeq |> Seq.map (fun (k, _) -> jaroWinkler name k) |> Seq.max

    /// Field overlap ratio using multiplication form to avoid integer-division truncation.
    /// AT5 rule: `overlap * 2 >= total` (multiplication), not `overlap >= total / 2` (int division).
    /// A field is considered matched when its best property similarity >= 0.5.
    let private fieldOverlapRatio (fields: FieldInfo list) (properties: Map<string, string>) : float =
        if fields.IsEmpty || properties.IsEmpty then
            0.0
        else
            let total = fields.Length

            let overlap =
                fields
                |> List.filter (fun f -> fieldSimScore f properties >= 0.5)
                |> List.length

            // AT5: overlap * 2 >= total is the half-overlap check; here we compute the ratio directly
            float overlap / float total

    // ── In-scope class filtering ──────────────────────────────────────────────

    let private isInScope (registry: VocabularyRegistry) (iri: string) : bool =
        registry.Using
        |> Set.exists (fun prefix ->
            match Map.tryFind prefix registry.Prefixes with
            | None -> false
            | Some baseUri -> iri.StartsWith(baseUri.AbsoluteUri, StringComparison.Ordinal))

    // ── Candidate scoring ─────────────────────────────────────────────────────

    let private combinedScore
        (typeTokens: string list)
        (fields: FieldInfo list)
        (properties: Map<string, string>)
        (classLocalName: string)
        : float =
        let typeScore = tokenAverageScore typeTokens classLocalName
        let fieldScore = fieldOverlapRatio fields properties
        0.6 * typeScore + 0.4 * fieldScore

    // Explicit bound K=3: the algorithm considers at most 3 top candidates.
    let private topKCandidatesBound = 3

    let private topK (candidates: (float * 'a) list) : (float * 'a) list =
        candidates |> List.sortByDescending fst |> List.truncate topKCandidatesBound

    let private confirmationThreshold = 0.85

    /// True when at least one type token has JW >= confirmationThreshold against classLocalName.
    /// A real lexical token hit is required for a class to be a viable candidate.
    let private hasTokenHit (typeTokens: string list) (classLocalName: string) : bool =
        List.exists (fun tok -> jaroWinkler tok classLocalName >= confirmationThreshold) typeTokens

    // ── IGraph extraction ─────────────────────────────────────────────────────

    let private rdfsClassIri = "http://www.w3.org/2000/01/rdf-schema#Class"
    let private rdfPropertyIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#Property"
    let private schemaClassIri = "https://schema.org/Class"
    let private schemaPropertyIri = "https://schema.org/Property"
    let private rdfTypeIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"

    let private iriLocalName (iri: string) : string =
        let hashIdx = iri.LastIndexOf('#')
        let slashIdx = iri.LastIndexOf('/')
        let idx = max hashIdx slashIdx

        if idx >= 0 && idx < iri.Length - 1 then
            iri.[idx + 1 ..].ToLowerInvariant()
        else
            iri.ToLowerInvariant()

    let private collectByTypeIri (typeIri: string) (graph: IGraph) : Map<string, string> =
        let typeNode = graph.CreateUriNode(Uri(rdfTypeIri))
        let typeValueNode = graph.CreateUriNode(Uri(typeIri))

        graph.GetTriplesWithPredicateObject(typeNode, typeValueNode)
        |> Seq.choose (fun triple ->
            match triple.Subject with
            | :? IUriNode as subj ->
                let iri = subj.Uri.AbsoluteUri
                Some(iriLocalName iri, iri)
            | _ -> None)
        |> Map.ofSeq

    let private mergeTermMaps (a: Map<string, string>) (b: Map<string, string>) : Map<string, string> =
        Map.fold (fun acc k v -> Map.add k v acc) a b

    /// Extract class and property local names from a vocabulary IGraph.
    /// Recognizes rdfs:Class, rdf:Property, schema:Class, schema:Property typings.
    /// Keys are lowercase local names; values are absolute IRI strings.
    let extractVocabTerms (graph: IGraph) : VocabTerms =
        let rdfsClasses = collectByTypeIri rdfsClassIri graph
        let schemaClasses = collectByTypeIri schemaClassIri graph
        let rdfProperties = collectByTypeIri rdfPropertyIri graph
        let schemaProperties = collectByTypeIri schemaPropertyIri graph

        { Classes = mergeTermMaps rdfsClasses schemaClasses
          Properties = mergeTermMaps rdfProperties schemaProperties }

    // ── CURIE reverse-resolution ──────────────────────────────────────────────

    /// Reverse-resolve an absolute Uri to a CURIE string using declared prefixes.
    /// Finds the longest matching prefix base URI, returns "prefix:local". Falls
    /// back to the absolute URI string if no prefix matches.
    let private toCurie (prefixes: Map<string, Uri>) (uri: Uri) : string =
        let absUri = uri.AbsoluteUri

        prefixes
        |> Map.toSeq
        |> Seq.tryFind (fun (_, baseUri) -> absUri.StartsWith(baseUri.AbsoluteUri, StringComparison.Ordinal))
        |> Option.map (fun (prefix, baseUri) -> $"{prefix}:{absUri.[baseUri.AbsoluteUri.Length ..]}")
        |> Option.defaultValue absUri

    // ── Field mapping construction ────────────────────────────────────────────

    let private buildFieldMapping
        (prefixes: Map<string, Uri>)
        (properties: Map<string, string>)
        (field: FieldInfo)
        : FieldMapping =
        if properties.IsEmpty then
            { Name = field.Name
              Iri = None
              Confidence = 0.0
              Source = Convention
              Status = Unresolved }
        else
            let name = bestFieldName field

            let fieldKey =
                field.Attributes
                |> Map.tryFind "JsonPropertyName"
                |> Option.defaultValue field.Name
                |> normKey

            let bestK, bestIri, conf =
                properties
                |> Map.toSeq
                |> Seq.map (fun (k, iri) -> k, iri, jaroWinkler name k)
                |> Seq.maxBy (fun (_, _, c) -> c)

            let status =
                if fieldKey = bestK then Confirmed
                elif conf > 0.0 then Proposed
                else Unresolved

            { Name = field.Name
              Iri = Some(toCurie prefixes (Uri bestIri))
              Confidence = conf
              Source = Convention
              Status = status }

    // ── Explicit equivalentClass override ─────────────────────────────────────

    /// If registry.EquivalentClasses contains an entry for typeInfo.FullName,
    /// override the class IRI to the explicit one, keeping convention-scored fields.
    let private applyExplicitClass
        (registry: VocabularyRegistry)
        (typeInfo: TypeInfo)
        (terms: VocabTerms)
        (convention: Mapping)
        : Mapping =
        match Map.tryFind typeInfo.FullName registry.EquivalentClasses with
        | None -> convention
        | Some explicitUri ->
            let curie = toCurie registry.Prefixes explicitUri

            let fieldMappings =
                if convention.Fields.IsEmpty && not typeInfo.Fields.IsEmpty then
                    typeInfo.Fields
                    |> List.map (buildFieldMapping registry.Prefixes terms.Properties)
                else
                    convention.Fields

            { convention with
                Iri = Some curie
                Confidence = 1.0
                Source = Manual
                Status = Confirmed
                Fields = fieldMappings }

    // ── Main entry ────────────────────────────────────────────────────────────

    /// Score a TypeInfo against in-scope vocabulary terms and emit a candidate Mapping.
    /// Pure: takes pre-extracted VocabTerms and VocabularyRegistry as data — no I/O.
    let score (terms: VocabTerms) (registry: VocabularyRegistry) (typeInfo: TypeInfo) : Mapping =
        let inScopeClasses =
            terms.Classes |> Map.filter (fun _ iri -> isInScope registry iri)

        let emptyUnresolved =
            { FSharpType = typeInfo.FullName
              Iri = None
              Confidence = 0.0
              Source = Convention
              Status = Unresolved
              Alternates = []
              Fields = [] }

        let conventionResult =
            if inScopeClasses.IsEmpty then
                emptyUnresolved
            else
                let typeTokens = normalizeTokens typeInfo.LocalName
                let typeKey = normKey typeInfo.LocalName

                let candidates =
                    inScopeClasses
                    |> Map.toList
                    |> List.choose (fun (localName, classIri) ->
                        if hasTokenHit typeTokens localName then
                            Some(
                                combinedScore typeTokens typeInfo.Fields terms.Properties localName,
                                (localName, classIri)
                            )
                        else
                            None)
                    |> topK

                match candidates with
                | [] -> emptyUnresolved
                | (bestScore, (bestLocal, bestIri)) :: rest ->
                    let alternates = rest |> List.map (snd >> snd)

                    let fieldMappings =
                        typeInfo.Fields
                        |> List.map (buildFieldMapping registry.Prefixes terms.Properties)

                    let status = if typeKey = bestLocal then Confirmed else Proposed

                    { FSharpType = typeInfo.FullName
                      Iri = Some(toCurie registry.Prefixes (Uri bestIri))
                      Confidence = bestScore
                      Source = Convention
                      Status = status
                      Alternates = alternates
                      Fields = fieldMappings }

        applyExplicitClass registry typeInfo terms conventionResult
