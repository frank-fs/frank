namespace Frank.Statecharts.Validation

open Frank.Statecharts.Ast

/// End-to-end validation pipeline: parse format sources and validate.
module Pipeline =

    let private emptyReport =
        { TotalChecks = 0; TotalSkipped = 0; TotalFailures = 0
          Checks = []; Failures = [] }

    /// Look up the parser function for a given format tag.
    /// Returns None for formats with no registered parser (e.g., XState).
    let private parserFor (tag: FormatTag) : (string -> ParseResult) option =
        match tag with
        | FormatTag.Wsd -> Some Frank.Statecharts.Wsd.Parser.parseWsd
        | FormatTag.Smcat -> Some Frank.Statecharts.Smcat.Parser.parseSmcat
        | FormatTag.Scxml -> Some Frank.Statecharts.Scxml.Parser.parseString
        | FormatTag.Alps -> Some Frank.Statecharts.Alps.JsonParser.parseAlpsJson
        | FormatTag.AlpsXml -> Some Frank.Statecharts.Alps.XmlParser.parseAlpsXml
        | FormatTag.XState -> Some Frank.Statecharts.XState.Deserializer.deserialize

    /// Parse a single (FormatTag * string) pair, returning either a
    /// (FormatParseResult * FormatArtifact) on success or a PipelineError.
    let private parseSource (tag: FormatTag) (source: string) =
        match parserFor tag with
        | None -> Error (UnsupportedFormat tag)
        | Some parser ->
            let result = parser source
            let pr =
                { Format = tag
                  Errors = result.Errors
                  Warnings = result.Warnings
                  Succeeded = List.isEmpty result.Errors }
            let art = { Format = tag; Document = result.Document }
            Ok (pr, art)

    /// Validate format sources with custom rules prepended to built-in rules.
    let validateSourcesWithRules
        (customRules: ValidationRule list)
        (sources: (FormatTag * string) list)
        : PipelineResult =
        if List.isEmpty sources then
            { ParseResults = []; Report = emptyReport; Errors = [] }
        else
            let duplicates =
                sources
                |> List.map fst
                |> List.groupBy id
                |> List.filter (fun (_, group) -> group.Length > 1)
                |> List.map (fun (tag, _) -> DuplicateFormat tag)

            if not (List.isEmpty duplicates) then
                { ParseResults = []; Report = emptyReport; Errors = duplicates }
            else
                let parseResults, artifacts, pipelineErrors =
                    (([], [], []), sources)
                    ||> List.fold (fun (prs, arts, errs) (tag, source) ->
                        match parseSource tag source with
                        | Ok (pr, art) -> (pr :: prs, art :: arts, errs)
                        | Error e -> (prs, arts, e :: errs))
                    |> fun (prs, arts, errs) -> (List.rev prs, List.rev arts, List.rev errs)

                let allRules = customRules @ SelfConsistencyRules.rules @ CrossFormatRules.rules
                let report = Validator.validate allRules artifacts

                { ParseResults = parseResults
                  Report = report
                  Errors = pipelineErrors }

    /// Validate format sources using built-in self-consistency and cross-format rules.
    let validateSources (sources: (FormatTag * string) list) : PipelineResult =
        validateSourcesWithRules [] sources

    // -------------------------------------------------------------------------
    // T007 – Format priority (lower number = higher priority; wins on conflict)
    // -------------------------------------------------------------------------

    let private formatPriority (tag: FormatTag) : int =
        match tag with
        | FormatTag.Scxml -> 0
        | FormatTag.XState -> 1
        | FormatTag.Smcat -> 2
        | FormatTag.Wsd -> 3
        | FormatTag.Alps -> 4
        | FormatTag.AlpsXml -> 4

    // -------------------------------------------------------------------------
    // T008 / T009 – Merge helpers
    // -------------------------------------------------------------------------

    /// Empty document used as the identity element for the merge fold.
    let private emptyDocument : StatechartDocument =
        { Title = None
          InitialStateId = None
          Elements = []
          DataEntries = []
          Annotations = [] }

    /// Extract all StateNode values from a document's Elements list.
    let private statesOf (doc: StatechartDocument) : StateNode list =
        doc.Elements
        |> List.choose (function
            | StateDecl s -> Some s
            | _ -> None)

    /// Extract all TransitionEdge values from a document's Elements list.
    let private transitionsOf (doc: StatechartDocument) : TransitionEdge list =
        doc.Elements
        |> List.choose (function
            | TransitionElement t -> Some t
            | _ -> None)

    /// Extract all non-state, non-transition elements from a document.
    let private otherElementsOf (doc: StatechartDocument) : StatechartElement list =
        doc.Elements
        |> List.filter (function
            | StateDecl _ | TransitionElement _ -> false
            | _ -> true)

    /// Merge two StateNode values: keep structural fields from base (higher priority),
    /// accumulate annotations from both, and fill None fields from enriching doc.
    let private mergeState (base': StateNode) (enriching: StateNode) : StateNode =
        { base' with
            // Fill None fields from enriching (enrichment, not override)
            Label = match base'.Label with Some _ -> base'.Label | None -> enriching.Label
            Activities = match base'.Activities with Some _ -> base'.Activities | None -> enriching.Activities
            // Structural fields (Kind, Children) stay with base (higher priority)
            // Annotations accumulate from both
            Annotations = base'.Annotations @ enriching.Annotations }

    /// Merge two TransitionEdge values: keep structural fields from base,
    /// accumulate annotations from both.
    let private mergeTransition (base': TransitionEdge) (enriching: TransitionEdge) : TransitionEdge =
        { base' with
            // Fill None fields from enriching
            Guard = match base'.Guard with Some _ -> base'.Guard | None -> enriching.Guard
            Action = match base'.Action with Some _ -> base'.Action | None -> enriching.Action
            // Annotations accumulate
            Annotations = base'.Annotations @ enriching.Annotations }

    /// Merge DataEntry lists: union by Name, preferring base entries on conflict.
    let private mergeDataEntries (base': DataEntry list) (enriching: DataEntry list) : DataEntry list =
        let baseNames = base' |> List.map (fun d -> d.Name) |> Set.ofList
        let newEntries = enriching |> List.filter (fun d -> not (Set.contains d.Name baseNames))
        base' @ newEntries

    /// Core two-document merge: base wins on structural conflict,
    /// enriching fills None fields, annotations always accumulate.
    let private mergeDocuments (base': StatechartDocument) (enriching: StatechartDocument) : StatechartDocument =
        // --- States ---
        let baseStates = statesOf base'
        let enrichingStates = statesOf enriching

        // Index enriching states by Identifier for O(n) lookup
        let enrichingStateMap =
            enrichingStates
            |> List.choose (fun s -> s.Identifier |> Option.map (fun id -> id, s))
            |> Map.ofList

        // Merge matched states; keep unmatched base states as-is
        let mergedBaseStates =
            baseStates
            |> List.map (fun s ->
                match s.Identifier with
                | Some id ->
                    match Map.tryFind id enrichingStateMap with
                    | Some enrichingState -> mergeState s enrichingState
                    | None -> s
                | None -> s)

        // Collect identifiers already covered by the base
        let baseStateIds =
            baseStates
            |> List.choose (fun s -> s.Identifier)
            |> Set.ofList

        // Add unmatched states from enriching doc (T011: union for non-overlapping)
        let unmatchedEnrichingStates =
            enrichingStates
            |> List.filter (fun s ->
                match s.Identifier with
                | Some id -> not (Set.contains id baseStateIds)
                | None -> true)  // anonymous states always added

        let allStates = mergedBaseStates @ unmatchedEnrichingStates

        // --- Transitions ---
        let baseTransitions = transitionsOf base'
        let enrichingTransitions = transitionsOf enriching

        // Key: (Source, Target, Event) triple
        let transitionKey (t: TransitionEdge) = (t.Source, t.Target, t.Event)

        let enrichingTransitionMap =
            enrichingTransitions
            |> List.map (fun t -> transitionKey t, t)
            |> Map.ofList

        let mergedBaseTransitions =
            baseTransitions
            |> List.map (fun t ->
                match Map.tryFind (transitionKey t) enrichingTransitionMap with
                | Some enrichingT -> mergeTransition t enrichingT
                | None -> t)

        let baseTransitionKeys =
            baseTransitions
            |> List.map transitionKey
            |> Set.ofList

        let unmatchedEnrichingTransitions =
            enrichingTransitions
            |> List.filter (fun t -> not (Set.contains (transitionKey t) baseTransitionKeys))

        let allTransitions = mergedBaseTransitions @ unmatchedEnrichingTransitions

        // --- Other elements (NoteElement, GroupElement, DirectiveElement) ---
        let otherElements = otherElementsOf base' @ otherElementsOf enriching

        // --- Reassemble Elements list: states first, then transitions, then others ---
        let allElements =
            (allStates |> List.map StateDecl)
            @ (allTransitions |> List.map TransitionElement)
            @ otherElements

        // --- Document-level fields ---
        { Title = match base'.Title with Some _ -> base'.Title | None -> enriching.Title
          InitialStateId = match base'.InitialStateId with Some _ -> base'.InitialStateId | None -> enriching.InitialStateId
          Elements = allElements
          DataEntries = mergeDataEntries base'.DataEntries enriching.DataEntries
          Annotations = base'.Annotations @ enriching.Annotations }

    // -------------------------------------------------------------------------
    // T010 – mergeSources public entry point
    // -------------------------------------------------------------------------

    /// Merge multiple (FormatTag * source) pairs into a single StatechartDocument.
    /// Format priority determines which structural fields win on conflict (SCXML=0 is
    /// highest priority; Alps/AlpsXml=4 is lowest). Annotations always accumulate.
    /// Returns the merged document, or an empty document when sources is empty.
    let mergeSources (sources: (FormatTag * string) list) : Result<StatechartDocument, PipelineError list> =
        if List.isEmpty sources then
            Ok emptyDocument
        else
            // Parse all sources, collecting errors for unsupported formats
            let parsedOrErrors =
                sources
                |> List.map (fun (tag, source) ->
                    match parserFor tag with
                    | Some parser -> Ok (tag, (parser source).Document)
                    | None -> Error (UnsupportedFormat tag))

            let errors =
                parsedOrErrors |> List.choose (function Error e -> Some e | Ok _ -> None)

            if not (List.isEmpty errors) then
                Error errors
            else
                let parsed =
                    parsedOrErrors |> List.choose (function Ok v -> Some v | Error _ -> None)

                // Sort by priority ascending so the highest-priority format is first
                let sorted = parsed |> List.sortBy (fun (tag, _) -> formatPriority tag)

                match sorted with
                | [] ->
                    // All sources were skipped (shouldn't happen given the error check above)
                    Ok emptyDocument
                | (_, base') :: rest ->
                    // Left fold: accumulate enrichment from lower-priority formats
                    let merged = rest |> List.fold (fun acc (_, doc) -> mergeDocuments acc doc) base'
                    Ok merged
