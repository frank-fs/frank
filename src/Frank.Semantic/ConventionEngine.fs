namespace Frank.Semantic

open System
open VDS.RDF

/// Extracted class/property/individual local names from a vocabulary IGraph.
/// Keys are lowercase local names; values are absolute IRI strings.
type VocabTerms =
    { Classes: Map<string, string>
      Properties: Map<string, string>
      Individuals: Map<string, string> }

/// Absolute IRI sets per term category, with NO local-name deduplication.
/// Used for term-existence checking in the accept oracle: identity is the absolute
/// IRI, never the local name. Two properties sharing a local name across different
/// namespaces (e.g. schema:identifier and dct:identifier) are both valid and must
/// both appear here — unlike VocabTerms which drops ambiguous local names to prevent
/// wrong-namespace convention matches.
type VocabTermIris =
    { ClassIris: Set<string>
      PropertyIris: Set<string>
      IndividualIris: Set<string> }

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

    /// Record fields used for the field-mapping path. A union contributes no
    /// record fields here (its cases are mapped separately in a later layer).
    let private recordFields (typeInfo: TypeInfo) : FieldInfo list =
        match typeInfo.Shape with
        | TypeShape.Record fields -> fields
        | TypeShape.Union _ -> []

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
            | Some baseUri ->
                let b = baseUri.AbsoluteUri

                iri.StartsWith(b, StringComparison.Ordinal)
                && (b.EndsWith("/")
                    || b.EndsWith("#")
                    || iri.Length = b.Length
                    || (let c = iri.[b.Length] in c = '/' || c = '#')))

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
    let private rdfsDatatypeIri = "http://www.w3.org/2000/01/rdf-schema#Datatype"
    let private rdfPropertyIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#Property"
    let private schemaClassIri = "https://schema.org/Class"
    let private schemaPropertyIri = "https://schema.org/Property"
    let private rdfTypeIri = "http://www.w3.org/1999/02/22-rdf-syntax-ns#type"
    let private owlClassIri = "http://www.w3.org/2002/07/owl#Class"
    let private owlObjectPropertyIri = "http://www.w3.org/2002/07/owl#ObjectProperty"

    let private owlDatatypePropertyIri =
        "http://www.w3.org/2002/07/owl#DatatypeProperty"

    let private owlNamedIndividualIri = "http://www.w3.org/2002/07/owl#NamedIndividual"
    let private skosConceptIri = "http://www.w3.org/2004/02/skos/core#Concept"

    let private iriLocalName (iri: string) : string =
        let hashIdx = iri.LastIndexOf('#')
        let slashIdx = iri.LastIndexOf('/')
        let idx = max hashIdx slashIdx

        if idx >= 0 && idx < iri.Length - 1 then
            iri.[idx + 1 ..].ToLowerInvariant()
        else
            iri.ToLowerInvariant()

    let private collectByTypeIri (typeIri: string) (graph: IGraph) : (string * string) seq =
        let typeNode = graph.CreateUriNode(Uri(rdfTypeIri))
        let typeValueNode = graph.CreateUriNode(Uri(typeIri))

        graph.GetTriplesWithPredicateObject(typeNode, typeValueNode)
        |> Seq.choose (fun triple ->
            match triple.Subject with
            | :? IUriNode as subj ->
                let iri = subj.Uri.AbsoluteUri
                Some(iriLocalName iri, iri)
            | _ -> None)

    let private mergeTermMaps (a: Map<string, string>) (b: Map<string, string>) : Map<string, string> =
        Map.fold (fun acc k v -> Map.add k v acc) a b

    /// Build a local-name to IRI map, excluding ambiguous local names (a name that
    /// maps to more than one distinct IRI). Ambiguous names are dropped so they cannot
    /// produce a Confirmed mapping to an arbitrary namespace; the affected type degrades
    /// to Unresolved (surfaced in the lock), never a silent wrong-namespace confirm.
    let private buildTermMap (pairs: (string * string) seq) : Map<string, string> =
        pairs
        |> Seq.groupBy fst
        |> Seq.choose (fun (key, group) ->
            match group |> Seq.map snd |> Seq.distinct |> Seq.toList with
            | [ single ] -> Some(key, single)
            | _ -> None)
        |> Map.ofSeq

    /// Collect enumeration members: subjects S where S rdf:type C and C is a known
    /// class IRI, excluding any S that is itself already a class or property IRI.
    let private collectEnumMembers
        (classIris: Set<string>)
        (excludeIris: Set<string>)
        (graph: IGraph)
        : (string * string) seq =
        let typeNode = graph.CreateUriNode(Uri(rdfTypeIri))

        graph.GetTriplesWithPredicate(typeNode)
        |> Seq.choose (fun triple ->
            match triple.Subject with
            | :? IUriNode as subj ->
                match triple.Object with
                | :? IUriNode as typeClass ->
                    let subjIri = subj.Uri.AbsoluteUri
                    let classIri = typeClass.Uri.AbsoluteUri

                    if Set.contains classIri classIris && not (Set.contains subjIri excludeIris) then
                        Some(iriLocalName subjIri, subjIri)
                    else
                        None
                | _ -> None
            | _ -> None)

    /// Extract class, property, and individual local names from a vocabulary IGraph.
    /// Recognized typings:
    ///   Classes    — rdfs:Class, schema:Class, owl:Class, rdfs:Datatype
    ///   Properties — rdf:Property, schema:Property, owl:ObjectProperty, owl:DatatypeProperty
    ///   Individuals — owl:NamedIndividual, skos:Concept, or any subject S where
    ///                 S rdf:type C and C is a known class (enumeration member pattern).
    ///                 A subject already in Classes or Properties is never re-bucketed here.
    /// Keys are lowercase local names; values are absolute IRI strings.
    /// A local name that maps to more than one distinct IRI is excluded (ambiguous).
    let extractVocabTerms (graph: IGraph) : VocabTerms =
        if isNull graph then
            invalidArg (nameof graph) "graph must not be null"

        let classPairs =
            Seq.append (collectByTypeIri rdfsClassIri graph) (collectByTypeIri schemaClassIri graph)
            |> Seq.append (collectByTypeIri owlClassIri graph)
            |> Seq.append (collectByTypeIri rdfsDatatypeIri graph)

        let propertyPairs =
            Seq.append (collectByTypeIri rdfPropertyIri graph) (collectByTypeIri schemaPropertyIri graph)
            |> Seq.append (collectByTypeIri owlObjectPropertyIri graph)
            |> Seq.append (collectByTypeIri owlDatatypePropertyIri graph)

        let classIris = classPairs |> Seq.map snd |> Set.ofSeq
        let propertyIris = propertyPairs |> Seq.map snd |> Set.ofSeq
        let excludeIris = Set.union classIris propertyIris

        { Classes = buildTermMap classPairs
          Properties = buildTermMap propertyPairs
          Individuals =
            buildTermMap (
                Seq.append (collectByTypeIri owlNamedIndividualIri graph) (collectByTypeIri skosConceptIri graph)
                |> Seq.append (collectEnumMembers classIris excludeIris graph)
            ) }

    /// Extract absolute IRI sets per term category from a vocabulary IGraph.
    /// Unlike extractVocabTerms, there is NO local-name deduplication: both
    /// http://a/identifier and http://b/identifier are kept even though they share
    /// the local name "identifier". Term-existence identity is the absolute IRI.
    let extractTermIris (graph: IGraph) : VocabTermIris =
        if isNull graph then
            invalidArg (nameof graph) "graph must not be null"

        let toIriSet (typeIris: string list) =
            typeIris
            |> List.collect (fun typeIri -> collectByTypeIri typeIri graph |> Seq.map snd |> Seq.toList)
            |> Set.ofList

        let classIris =
            toIriSet [ rdfsClassIri; schemaClassIri; owlClassIri; rdfsDatatypeIri ]

        let propertyIris =
            toIriSet
                [ rdfPropertyIri
                  schemaPropertyIri
                  owlObjectPropertyIri
                  owlDatatypePropertyIri ]

        let excludeIris = Set.union classIris propertyIris

        let individualIris =
            toIriSet [ owlNamedIndividualIri; skosConceptIri ]
            |> Set.union (collectEnumMembers classIris excludeIris graph |> Seq.map snd |> Set.ofSeq)

        { ClassIris = classIris
          PropertyIris = propertyIris
          IndividualIris = individualIris }

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

    // ── Case scoring (union join) ─────────────────────────────────────────────

    // Called only when entities is non-empty, so Seq.maxBy is safe.
    let private fuzzyEntity
        (prefixes: Map<string, Uri>)
        (entities: Map<string, string>)
        (key: string)
        : string option * float * MappingStatus =
        let _, bestIri, conf =
            entities
            |> Map.toSeq
            |> Seq.map (fun (k, iri) -> k, iri, jaroWinkler key k)
            |> Seq.maxBy (fun (_, _, c) -> c)

        if conf > 0.0 then
            Some(toCurie prefixes (Uri bestIri)), conf, Proposed
        else
            None, 0.0, Unresolved

    /// Resolve a case name against a role map (individuals for nullary, classes
    /// for payload) by normalized-key identity → Confirmed; fuzzy → Proposed;
    /// none → Unresolved. Mirrors the type-level confirm rule exactly.
    let private matchEntity
        (prefixes: Map<string, Uri>)
        (entities: Map<string, string>)
        (name: string)
        : string option * float * MappingStatus =
        if entities.IsEmpty then
            None, 0.0, Unresolved
        else
            let key = normKey name

            match Map.tryFind key entities with
            | Some iri -> Some(toCurie prefixes (Uri iri)), 1.0, Confirmed
            | None -> fuzzyEntity prefixes entities key

    let private buildCaseMapping (registry: VocabularyRegistry) (terms: VocabTerms) (case: CaseInfo) : CaseMapping =
        let role =
            (if case.Payload.IsEmpty then
                 mergeTermMaps terms.Classes terms.Individuals
             else
                 mergeTermMaps terms.Individuals terms.Classes)
            |> Map.filter (fun _ iri -> isInScope registry iri)

        let iri, conf, status = matchEntity registry.Prefixes role case.Name

        let payload =
            case.Payload |> List.map (buildFieldMapping registry.Prefixes terms.Properties)

        { Name = case.Name
          Iri = iri
          Confidence = conf
          Source = Convention
          Status = status
          Payload = payload }

    let private mapUnionCases (registry: VocabularyRegistry) (terms: VocabTerms) (cases: CaseInfo list) : MappingShape =
        MappingShape.Union(cases |> List.map (buildCaseMapping registry terms))

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

            let fields = recordFields typeInfo

            let fieldMappings =
                if (MappingShape.payloadFields convention.Shape).IsEmpty && not fields.IsEmpty then
                    fields |> List.map (buildFieldMapping registry.Prefixes terms.Properties)
                else
                    MappingShape.payloadFields convention.Shape

            { convention with
                Iri = Some curie
                Confidence = 1.0
                Source = Manual
                Status = Confirmed
                Shape = MappingShape.Record fieldMappings }

    // ── Union fallback ────────────────────────────────────────────────────────

    let private unionFallback
        (registry: VocabularyRegistry)
        (terms: VocabTerms)
        (typeInfo: TypeInfo)
        (empty: Mapping)
        : Mapping =
        match typeInfo.Shape with
        | TypeShape.Union cases ->
            { empty with
                Shape = mapUnionCases registry terms cases }
        | TypeShape.Record _ -> empty

    // ── Main entry ────────────────────────────────────────────────────────────

    let private unresolvedMapping (typeInfo: TypeInfo) : Mapping =
        { FSharpType = typeInfo.FullName
          Iri = None
          Confidence = 0.0
          Source = Convention
          Status = Unresolved
          Alternates = []
          Shape = MappingShape.Record [] }

    /// Score a TypeInfo against in-scope vocabulary terms and emit a candidate Mapping.
    /// Pure: takes pre-extracted VocabTerms and VocabularyRegistry as data — no I/O.
    let score (terms: VocabTerms) (registry: VocabularyRegistry) (typeInfo: TypeInfo) : Mapping =
        let inScopeClasses =
            terms.Classes |> Map.filter (fun _ iri -> isInScope registry iri)

        let emptyUnresolved = unresolvedMapping typeInfo

        let conventionResult =
            if inScopeClasses.IsEmpty then
                unionFallback registry terms typeInfo emptyUnresolved
            else
                let typeTokens = normalizeTokens typeInfo.LocalName
                let typeKey = normKey typeInfo.LocalName

                let candidates =
                    inScopeClasses
                    |> Map.toList
                    |> List.choose (fun (localName, classIri) ->
                        if hasTokenHit typeTokens localName then
                            Some(
                                combinedScore typeTokens (recordFields typeInfo) terms.Properties localName,
                                (localName, classIri)
                            )
                        else
                            None)
                    |> topK

                match candidates with
                | [] -> unionFallback registry terms typeInfo emptyUnresolved
                | (bestScore, (bestLocal, bestIri)) :: rest ->
                    let alternates = rest |> List.map (snd >> snd)

                    let fieldMappings =
                        recordFields typeInfo
                        |> List.map (buildFieldMapping registry.Prefixes terms.Properties)

                    let shape =
                        match typeInfo.Shape with
                        | TypeShape.Union cases -> mapUnionCases registry terms cases
                        | TypeShape.Record _ -> MappingShape.Record fieldMappings

                    let status = if typeKey = bestLocal then Confirmed else Proposed

                    { FSharpType = typeInfo.FullName
                      Iri = Some(toCurie registry.Prefixes (Uri bestIri))
                      Confidence = bestScore
                      Source = Convention
                      Status = status
                      Alternates = alternates
                      Shape = shape }

        applyExplicitClass registry typeInfo terms conventionResult
