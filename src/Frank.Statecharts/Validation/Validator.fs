namespace Frank.Statecharts.Validation

open Frank.Statecharts.Ast

/// Shared traversal functions for extracting elements from StatechartDocument.
module AstHelpers =

    /// Extract all StateNode values from a document, recursively including
    /// children and states nested in GroupBlock branches.
    let allStates (doc: StatechartDocument) : StateNode list =
        let rec collectFromElements (elements: StatechartElement list) =
            elements
            |> List.collect (fun elem ->
                match elem with
                | StateDecl node ->
                    node :: collectFromChildren node
                | GroupElement group ->
                    group.Branches
                    |> List.collect (fun branch -> collectFromElements branch.Elements)
                | _ -> [])

        and collectFromChildren (node: StateNode) =
            node.Children
            |> List.collect (fun child -> child :: collectFromChildren child)

        collectFromElements doc.Elements

    /// Extract all TransitionEdge values from a document, including those
    /// nested in GroupBlock branches.
    let allTransitions (doc: StatechartDocument) : TransitionEdge list =
        let rec collectFromElements (elements: StatechartElement list) =
            elements
            |> List.collect (fun elem ->
                match elem with
                | TransitionElement edge -> [ edge ]
                | GroupElement group ->
                    group.Branches
                    |> List.collect (fun branch -> collectFromElements branch.Elements)
                | _ -> [])

        collectFromElements doc.Elements

    /// Extract the set of all state identifiers from a document.
    let stateIdentifiers (doc: StatechartDocument) : string Set =
        allStates doc |> List.choose _.Identifier |> Set.ofList

    /// Extract the set of all event names from transitions in a document.
    /// Filters out None events.
    let eventNames (doc: StatechartDocument) : string Set =
        allTransitions doc
        |> List.choose _.Event
        |> Set.ofList

    /// Extract the set of all transition target identifiers from a document.
    /// Filters out None (internal/completion) targets.
    let transitionTargets (doc: StatechartDocument) : string Set =
        allTransitions doc
        |> List.choose _.Target
        |> Set.ofList

/// Universal self-consistency rules that validate structural integrity
/// of any single-format artifact.
module SelfConsistencyRules =

    /// Check that all transition targets reference existing states within each artifact.
    let orphanTransitionTargets: ValidationRule =
        { Name = "Orphan transition targets"
          RequiredFormats = Set.empty
          Check =
            fun artifacts ->
                let checks, failures =
                    artifacts
                    |> List.fold
                        (fun (cs, fs) artifact ->
                            let stateIds = AstHelpers.stateIdentifiers artifact.Document
                            let targets = AstHelpers.transitionTargets artifact.Document
                            let orphans = targets - stateIds

                            if Set.isEmpty orphans then
                                let c =
                                    { Name = sprintf "Orphan transition targets (%A)" artifact.Format
                                      Status = Pass
                                      Reason = None }

                                (c :: cs, fs)
                            else
                                let newChecks, newFailures =
                                    orphans
                                    |> Set.toList
                                    |> List.map (fun orphan ->
                                        let c =
                                            { Name = sprintf "Orphan transition target '%s' (%A)" orphan artifact.Format
                                              Status = Fail
                                              Reason =
                                                Some(
                                                    sprintf
                                                        "Transition target '%s' does not reference any state in %A artifact"
                                                        orphan
                                                        artifact.Format
                                                ) }

                                        let f =
                                            { Formats = [ artifact.Format ]
                                              EntityType = "transition target"
                                              Expected = sprintf "Target '%s' should reference an existing state" orphan
                                              Actual = sprintf "State '%s' not found in %A artifact" orphan artifact.Format
                                              Description =
                                                sprintf
                                                    "Transition targets state '%s' which does not exist in the %A artifact"
                                                    orphan
                                                    artifact.Format }

                                        (c, f))
                                    |> List.unzip

                                (newChecks @ cs, newFailures @ fs))
                        ([], [])

                (checks |> List.rev, failures |> List.rev) }

    /// Check that all state identifiers are unique within each artifact.
    let duplicateStateIdentifiers: ValidationRule =
        { Name = "Duplicate state identifiers"
          RequiredFormats = Set.empty
          Check =
            fun artifacts ->
                let checks, failures =
                    artifacts
                    |> List.fold
                        (fun (cs, fs) artifact ->
                            let allStates = AstHelpers.allStates artifact.Document
                            let ids = allStates |> List.choose (fun s -> s.Identifier)

                            let duplicates =
                                ids
                                |> List.groupBy id
                                |> List.filter (fun (_, group) -> group.Length > 1)
                                |> List.map fst

                            if List.isEmpty duplicates then
                                let c =
                                    { Name = sprintf "Duplicate state identifiers (%A)" artifact.Format
                                      Status = Pass
                                      Reason = None }

                                (c :: cs, fs)
                            else
                                let newChecks, newFailures =
                                    duplicates
                                    |> List.map (fun dup ->
                                        let c =
                                            { Name = sprintf "Duplicate state identifier '%s' (%A)" dup artifact.Format
                                              Status = Fail
                                              Reason =
                                                Some(
                                                    sprintf
                                                        "State identifier '%s' appears multiple times in %A artifact"
                                                        dup
                                                        artifact.Format
                                                ) }

                                        let f =
                                            { Formats = [ artifact.Format ]
                                              EntityType = "state identifier"
                                              Expected = sprintf "State identifier '%s' should be unique" dup
                                              Actual =
                                                sprintf
                                                    "State identifier '%s' appears multiple times in %A artifact"
                                                    dup
                                                    artifact.Format
                                              Description =
                                                sprintf "Duplicate state identifier '%s' in %A artifact" dup artifact.Format }

                                        (c, f))
                                    |> List.unzip

                                (newChecks @ cs, newFailures @ fs))
                        ([], [])

                (checks |> List.rev, failures |> List.rev) }

    /// Check that required AST fields are populated in each artifact.
    let requiredAstFields: ValidationRule =
        { Name = "Required AST fields"
          RequiredFormats = Set.empty
          Check =
            fun artifacts ->
                let checks, failures =
                    artifacts
                    |> List.fold
                        (fun (cs, fs) artifact ->
                            let states = AstHelpers.allStates artifact.Document
                            let transitions = AstHelpers.allTransitions artifact.Document

                            let emptyStateChecks, emptyStateFailures =
                                states
                                |> List.filter (fun s ->
                                    match s.Identifier with
                                    | None -> true
                                    | Some id -> System.String.IsNullOrWhiteSpace id)
                                |> List.mapi (fun i _ ->
                                    let c =
                                        { Name = sprintf "Empty state identifier #%d (%A)" (i + 1) artifact.Format
                                          Status = Fail
                                          Reason =
                                            Some(
                                                sprintf
                                                    "State at index %d has empty identifier in %A artifact"
                                                    i
                                                    artifact.Format
                                            ) }

                                    let f =
                                        { Formats = [ artifact.Format ]
                                          EntityType = "state identifier"
                                          Expected = "Non-empty state identifier"
                                          Actual = "Empty or whitespace-only state identifier"
                                          Description =
                                            sprintf
                                                "State at index %d has empty identifier in %A artifact"
                                                i
                                                artifact.Format }

                                    (c, f))
                                |> List.unzip

                            let emptySourceChecks, emptySourceFailures =
                                transitions
                                |> List.filter (fun t -> System.String.IsNullOrWhiteSpace t.Source)
                                |> List.mapi (fun i _ ->
                                    let c =
                                        { Name = sprintf "Empty transition source #%d (%A)" (i + 1) artifact.Format
                                          Status = Fail
                                          Reason =
                                            Some(
                                                sprintf
                                                    "Transition at index %d has empty source in %A artifact"
                                                    i
                                                    artifact.Format
                                            ) }

                                    let f =
                                        { Formats = [ artifact.Format ]
                                          EntityType = "transition source"
                                          Expected = "Non-empty transition source"
                                          Actual = "Empty or whitespace-only transition source"
                                          Description =
                                            sprintf
                                                "Transition at index %d has empty source in %A artifact"
                                                i
                                                artifact.Format }

                                    (c, f))
                                |> List.unzip

                            let allIssueChecks = emptyStateChecks @ emptySourceChecks
                            let allIssueFailures = emptyStateFailures @ emptySourceFailures

                            if List.isEmpty allIssueChecks then
                                let c =
                                    { Name = sprintf "Required AST fields (%A)" artifact.Format
                                      Status = Pass
                                      Reason = None }

                                (c :: cs, fs)
                            else
                                (allIssueChecks @ cs, allIssueFailures @ fs))
                        ([], [])

                (checks |> List.rev, failures |> List.rev) }

    /// Warn about states with no incoming or outgoing transitions.
    /// Isolated states may be intentional, so this is a warning (Pass with reason), not a failure.
    let isolatedStates: ValidationRule =
        { Name = "Isolated states"
          RequiredFormats = Set.empty
          Check =
            fun artifacts ->
                let checks =
                    artifacts
                    |> List.collect (fun artifact ->
                        let stateIds = AstHelpers.stateIdentifiers artifact.Document
                        let transitions = AstHelpers.allTransitions artifact.Document
                        let sources = transitions |> List.map (fun t -> t.Source) |> Set.ofList
                        let targets = transitions |> List.choose (fun t -> t.Target) |> Set.ofList
                        let connected = Set.union sources targets
                        let isolated = stateIds - connected

                        if Set.isEmpty isolated then
                            [ { Name = sprintf "Isolated states (%A)" artifact.Format
                                Status = Pass
                                Reason = None } ]
                        else
                            isolated
                            |> Set.toList
                            |> List.map (fun stateId ->
                                { Name = sprintf "Isolated state '%s' (%A)" stateId artifact.Format
                                  Status = Pass // WARNING, not failure
                                  Reason =
                                    Some(
                                        sprintf
                                            "State '%s' has no incoming or outgoing transitions in %A artifact (may be intentional)"
                                            stateId
                                            artifact.Format
                                    ) }))

                (checks, []) }

    /// Warn about artifacts with empty state machines (no states, no transitions).
    let emptyStatechart: ValidationRule =
        { Name = "Empty statechart"
          RequiredFormats = Set.empty
          Check =
            fun artifacts ->
                let checks =
                    artifacts
                    |> List.collect (fun artifact ->
                        let states = AstHelpers.allStates artifact.Document
                        let transitions = AstHelpers.allTransitions artifact.Document

                        if List.isEmpty states && List.isEmpty transitions then
                            [ { Name = sprintf "Empty statechart (%A)" artifact.Format
                                Status = Pass // WARNING, not failure
                                Reason = Some(sprintf "%A artifact contains no states and no transitions" artifact.Format) } ]
                        else
                            [ { Name = sprintf "Empty statechart (%A)" artifact.Format
                                Status = Pass
                                Reason = None } ])

                (checks, []) }

    /// All universal self-consistency rules.
    let rules: ValidationRule list =
        [ orphanTransitionTargets
          duplicateStateIdentifiers
          requiredAstFields
          isolatedStates
          emptyStatechart ]

/// Cross-format pairwise validation rules for state name agreement,
/// event name agreement, and transition target agreement.
module CrossFormatRules =

    open Frank.Statecharts.Validation.StringDistance

    /// Check if a value has a case-insensitive match in a set but not an exact match.
    /// Returns a descriptive note about the casing difference, or empty string if no near-match.
    let private describeCasingMismatch (value: string) (candidates: string Set) : string =
        let nearMatch =
            candidates
            |> Set.toList
            |> List.tryFind (fun c ->
                System.String.Equals(value, c, System.StringComparison.OrdinalIgnoreCase) && value <> c)

        match nearMatch with
        | Some found -> sprintf " Note: case-insensitive match found: '%s' vs '%s' (casing differs)" value found
        | None -> ""

    /// Create a state name agreement rule for a specific format pair.
    let stateNameAgreement (formatA: FormatTag) (formatB: FormatTag) : ValidationRule =
        { Name = sprintf "%A-%A state name agreement" formatA formatB
          RequiredFormats = set [ formatA; formatB ]
          Check =
            fun artifacts ->
                let artA = artifacts |> List.find (fun a -> a.Format = formatA)
                let artB = artifacts |> List.find (fun a -> a.Format = formatB)
                let statesA = AstHelpers.stateIdentifiers artA.Document
                let statesB = AstHelpers.stateIdentifiers artB.Document

                let missingFromB = statesA - statesB
                let missingFromA = statesB - statesA

                let failuresFromB =
                    missingFromB
                    |> Set.toList
                    |> List.map (fun stateId ->
                        let casingNote = describeCasingMismatch stateId statesB

                        let c =
                            { Name = sprintf "State '%s' missing from %A" stateId formatB
                              Status = Fail
                              Reason =
                                Some(
                                    sprintf "State '%s' exists in %A but not in %A.%s" stateId formatA formatB casingNote
                                ) }

                        let f =
                            { Formats = [ formatA; formatB ]
                              EntityType = "state name"
                              Expected = sprintf "State '%s' should exist in %A" stateId formatB
                              Actual = sprintf "State '%s' not found in %A" stateId formatB
                              Description =
                                sprintf
                                    "State '%s' exists in %A but not in %A.%s"
                                    stateId
                                    formatA
                                    formatB
                                    casingNote }

                        (c, f))

                let failuresFromA =
                    missingFromA
                    |> Set.toList
                    |> List.map (fun stateId ->
                        let casingNote = describeCasingMismatch stateId statesA

                        let c =
                            { Name = sprintf "State '%s' missing from %A" stateId formatA
                              Status = Fail
                              Reason =
                                Some(
                                    sprintf "State '%s' exists in %A but not in %A.%s" stateId formatB formatA casingNote
                                ) }

                        let f =
                            { Formats = [ formatA; formatB ]
                              EntityType = "state name"
                              Expected = sprintf "State '%s' should exist in %A" stateId formatA
                              Actual = sprintf "State '%s' not found in %A" stateId formatA
                              Description =
                                sprintf
                                    "State '%s' exists in %A but not in %A.%s"
                                    stateId
                                    formatB
                                    formatA
                                    casingNote }

                        (c, f))

                let allPairs = failuresFromB @ failuresFromA

                if List.isEmpty allPairs then
                    ([ { Name = sprintf "%A-%A state name agreement" formatA formatB
                         Status = Pass
                         Reason = None } ],
                     [])
                else
                    allPairs |> List.unzip }

    /// Create an event name agreement rule for a specific format pair.
    let eventNameAgreement (formatA: FormatTag) (formatB: FormatTag) : ValidationRule =
        { Name = sprintf "%A-%A event name agreement" formatA formatB
          RequiredFormats = set [ formatA; formatB ]
          Check =
            fun artifacts ->
                let artA = artifacts |> List.find (fun a -> a.Format = formatA)
                let artB = artifacts |> List.find (fun a -> a.Format = formatB)
                let eventsA = AstHelpers.eventNames artA.Document
                let eventsB = AstHelpers.eventNames artB.Document

                let missingFromB = eventsA - eventsB
                let missingFromA = eventsB - eventsA

                let failuresFromB =
                    missingFromB
                    |> Set.toList
                    |> List.map (fun eventName ->
                        let casingNote = describeCasingMismatch eventName eventsB

                        let c =
                            { Name = sprintf "Event '%s' missing from %A" eventName formatB
                              Status = Fail
                              Reason =
                                Some(
                                    sprintf
                                        "Event '%s' exists in %A but not in %A.%s"
                                        eventName
                                        formatA
                                        formatB
                                        casingNote
                                ) }

                        let f =
                            { Formats = [ formatA; formatB ]
                              EntityType = "event name"
                              Expected = sprintf "Event '%s' should exist in %A" eventName formatB
                              Actual = sprintf "Event '%s' not found in %A" eventName formatB
                              Description =
                                sprintf
                                    "Event '%s' exists in %A but not in %A.%s"
                                    eventName
                                    formatA
                                    formatB
                                    casingNote }

                        (c, f))

                let failuresFromA =
                    missingFromA
                    |> Set.toList
                    |> List.map (fun eventName ->
                        let casingNote = describeCasingMismatch eventName eventsA

                        let c =
                            { Name = sprintf "Event '%s' missing from %A" eventName formatA
                              Status = Fail
                              Reason =
                                Some(
                                    sprintf
                                        "Event '%s' exists in %A but not in %A.%s"
                                        eventName
                                        formatB
                                        formatA
                                        casingNote
                                ) }

                        let f =
                            { Formats = [ formatA; formatB ]
                              EntityType = "event name"
                              Expected = sprintf "Event '%s' should exist in %A" eventName formatA
                              Actual = sprintf "Event '%s' not found in %A" eventName formatA
                              Description =
                                sprintf
                                    "Event '%s' exists in %A but not in %A.%s"
                                    eventName
                                    formatB
                                    formatA
                                    casingNote }

                        (c, f))

                let allPairs = failuresFromB @ failuresFromA

                if List.isEmpty allPairs then
                    ([ { Name = sprintf "%A-%A event name agreement" formatA formatB
                         Status = Pass
                         Reason = None } ],
                     [])
                else
                    allPairs |> List.unzip }

    /// Create a transition target agreement rule for a specific format pair.
    let transitionTargetAgreement (formatA: FormatTag) (formatB: FormatTag) : ValidationRule =
        { Name = sprintf "%A-%A transition target agreement" formatA formatB
          RequiredFormats = set [ formatA; formatB ]
          Check =
            fun artifacts ->
                let artA = artifacts |> List.find (fun a -> a.Format = formatA)
                let artB = artifacts |> List.find (fun a -> a.Format = formatB)
                let targetsA = AstHelpers.transitionTargets artA.Document
                let statesB = AstHelpers.stateIdentifiers artB.Document
                let targetsB = AstHelpers.transitionTargets artB.Document
                let statesA = AstHelpers.stateIdentifiers artA.Document

                let missingInB = targetsA - statesB
                let missingInA = targetsB - statesA

                let failuresAtoB =
                    missingInB
                    |> Set.toList
                    |> List.map (fun target ->
                        let casingNote = describeCasingMismatch target statesB

                        let c =
                            { Name =
                                sprintf "Transition target '%s' from %A missing in %A states" target formatA formatB
                              Status = Fail
                              Reason =
                                Some(
                                    sprintf
                                        "Transition target '%s' in %A does not correspond to any state in %A.%s"
                                        target
                                        formatA
                                        formatB
                                        casingNote
                                ) }

                        let f =
                            { Formats = [ formatA; formatB ]
                              EntityType = "transition target"
                              Expected = sprintf "Transition target '%s' should reference a state in %A" target formatB
                              Actual = sprintf "State '%s' not found in %A" target formatB
                              Description =
                                sprintf
                                    "Transition target '%s' in %A does not correspond to any state in %A.%s"
                                    target
                                    formatA
                                    formatB
                                    casingNote }

                        (c, f))

                let failuresBtoA =
                    missingInA
                    |> Set.toList
                    |> List.map (fun target ->
                        let casingNote = describeCasingMismatch target statesA

                        let c =
                            { Name =
                                sprintf "Transition target '%s' from %A missing in %A states" target formatB formatA
                              Status = Fail
                              Reason =
                                Some(
                                    sprintf
                                        "Transition target '%s' in %A does not correspond to any state in %A.%s"
                                        target
                                        formatB
                                        formatA
                                        casingNote
                                ) }

                        let f =
                            { Formats = [ formatA; formatB ]
                              EntityType = "transition target"
                              Expected = sprintf "Transition target '%s' should reference a state in %A" target formatA
                              Actual = sprintf "State '%s' not found in %A" target formatA
                              Description =
                                sprintf
                                    "Transition target '%s' in %A does not correspond to any state in %A.%s"
                                    target
                                    formatB
                                    formatA
                                    casingNote }

                        (c, f))

                let allPairs = failuresAtoB @ failuresBtoA

                if List.isEmpty allPairs then
                    ([ { Name = sprintf "%A-%A transition target agreement" formatA formatB
                         Status = Pass
                         Reason = None } ],
                     [])
                else
                    allPairs |> List.unzip }

    /// Similarity threshold above which two identifiers are flagged as near-matches.
    let nearMatchThreshold = 0.8

    /// Universal near-match rule: detects identifiers that are similar but not identical
    /// across any pair of artifacts using Jaro-Winkler similarity.
    let nearMatchRule: ValidationRule =
        { Name = "cross-format-near-match"
          RequiredFormats = Set.empty
          Check =
            fun artifacts ->
                let checks = ResizeArray<ValidationCheck>()
                let failures = ResizeArray<ValidationFailure>()

                for i in 0 .. artifacts.Length - 2 do
                    for j in i + 1 .. artifacts.Length - 1 do
                        let a = artifacts.[i]
                        let b = artifacts.[j]

                        let statesA = AstHelpers.stateIdentifiers a.Document
                        let statesB = AstHelpers.stateIdentifiers b.Document

                        // Compare unmatched states from both directions (symmetric)
                        let unmatchedA = Set.difference statesA statesB
                        let unmatchedB = Set.difference statesB statesA
                        let reported = System.Collections.Generic.HashSet<string * string>()

                        for sA in unmatchedA do
                            for sB in unmatchedB do
                                let score = jaroWinkler sA sB

                                if score > nearMatchThreshold then
                                    let key = if sA < sB then (sA, sB) else (sB, sA)
                                    if reported.Add(key) then
                                        let desc =
                                            sprintf
                                                "Near-match: '%s' in %A <-> '%s' in %A (similarity: %.2f)"
                                                sA
                                                a.Format
                                                sB
                                                b.Format
                                                score

                                        checks.Add(
                                            { Name = sprintf "near-match-state-%s-%s" sA sB
                                              Status = Fail
                                              Reason = Some desc }
                                        )

                                        failures.Add(
                                            { Formats = [ a.Format; b.Format ]
                                              EntityType = "state"
                                              Expected = sA
                                              Actual = sB
                                              Description = desc }
                                        )

                        let eventsA = AstHelpers.eventNames a.Document
                        let eventsB = AstHelpers.eventNames b.Document

                        // Compare unmatched events from both directions (symmetric)
                        let unmatchedEventsA = Set.difference eventsA eventsB
                        let unmatchedEventsB = Set.difference eventsB eventsA
                        let reportedEvents = System.Collections.Generic.HashSet<string * string>()

                        for eA in unmatchedEventsA do
                            for eB in unmatchedEventsB do
                                let score = jaroWinkler eA eB

                                if score > nearMatchThreshold then
                                    let key = if eA < eB then (eA, eB) else (eB, eA)
                                    if reportedEvents.Add(key) then
                                        let desc =
                                            sprintf
                                                "Near-match: '%s' in %A <-> '%s' in %A (similarity: %.2f)"
                                                eA
                                                a.Format
                                                eB
                                                b.Format
                                                score

                                        checks.Add(
                                            { Name = sprintf "near-match-event-%s-%s" eA eB
                                              Status = Fail
                                              Reason = Some desc }
                                        )

                                        failures.Add(
                                            { Formats = [ a.Format; b.Format ]
                                              EntityType = "event"
                                              Expected = eA
                                              Actual = eB
                                              Description = desc }
                                        )

                if checks.Count = 0 then
                    checks.Add(
                        { Name = "cross-format-near-match"
                          Status = Pass
                          Reason = None }
                    )

                (Seq.toList checks, Seq.toList failures) }

    /// All unique pairs of format tags.
    let private formatPairs: (FormatTag * FormatTag) list =
        let tags = [ Wsd; Alps; AlpsXml; Scxml; Smcat; XState ]

        [ for i in 0 .. tags.Length - 2 do
              for j in i + 1 .. tags.Length - 1 do
                  yield (tags.[i], tags.[j]) ]

    /// All cross-format rules generated from pairwise combinations.
    let private allPairwiseRules: ValidationRule list =
        formatPairs
        |> List.collect (fun (a, b) -> [ stateNameAgreement a b; eventNameAgreement a b; transitionTargetAgreement a b ])

    /// All cross-format rules for all applicable format pairs, plus the universal near-match rule.
    let rules: ValidationRule list = allPairwiseRules @ [ nearMatchRule ]

/// Validation orchestrator.
module Validator =

    /// Validate statechart artifacts against registered rules.
    /// Collects all results without aborting on first failure.
    /// Catches exceptions from rules and reports them as failures.
    let validate (rules: ValidationRule list) (artifacts: FormatArtifact list) : ValidationReport =
        let availableTags =
            artifacts |> List.map _.Format |> Set.ofList

        let allChecks, allFailures =
            rules
            |> List.fold
                (fun (checks, failures) rule ->
                    if rule.RequiredFormats <> Set.empty
                       && not (Set.isSubset rule.RequiredFormats availableTags)
                    then
                        let missingFormats =
                            Set.difference rule.RequiredFormats availableTags
                            |> Set.toList
                            |> List.map (sprintf "%A")
                            |> String.concat ", "

                        let skipCheck =
                            { Name = rule.Name
                              Status = Skip
                              Reason = Some(sprintf "Missing formats: %s" missingFormats) }

                        (skipCheck :: checks, failures)
                    else
                        try
                            let ruleChecks, ruleFailures = rule.Check artifacts
                            (ruleChecks @ checks, ruleFailures @ failures)
                        with ex ->
                            let failCheck =
                                { Name = rule.Name
                                  Status = Fail
                                  Reason = Some(sprintf "Exception: %s" ex.Message) }

                            let failEntry =
                                { Formats = []
                                  EntityType = "validation"
                                  Expected = "rule execution without exception"
                                  Actual = sprintf "exception thrown: %s" ex.Message
                                  Description =
                                    sprintf "Rule '%s' threw %s: %s" rule.Name (ex.GetType().Name) ex.Message }

                            (failCheck :: checks, failEntry :: failures))
                ([], [])

        let checks = allChecks |> List.rev
        let failures = allFailures |> List.rev

        let totalChecks =
            checks
            |> List.filter (fun c -> c.Status = Pass || c.Status = Fail)
            |> List.length

        let totalSkipped =
            checks
            |> List.filter (fun c -> c.Status = Skip)
            |> List.length

        let totalFailures = failures |> List.length

        { TotalChecks = totalChecks
          TotalSkipped = totalSkipped
          TotalFailures = totalFailures
          Checks = checks
          Failures = failures }
