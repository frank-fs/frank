namespace Frank.Statecharts.Validation

open Frank.Statecharts.Ast

// ============================================================================
// AST Helpers & Validator Orchestrator (Spec 021 - WP02)
// Cross-Format Validation Rules (Spec 021 - WP04)
// ============================================================================

/// Shared AST traversal functions for extracting elements from
/// StatechartDocument. Used by validation rules to avoid duplicating
/// traversal logic.
module AstHelpers =

    /// Extract all StateNode values from a document, recursively including
    /// children and states nested in GroupBlock branches.
    let allStates (doc: StatechartDocument) : StateNode list =
        let rec collectFromElements (elements: StatechartElement list) =
            elements
            |> List.collect (fun elem ->
                match elem with
                | StateDecl node ->
                    node :: collectChildStates node
                | GroupElement group ->
                    group.Branches
                    |> List.collect (fun branch -> collectFromElements branch.Elements)
                | TransitionElement _
                | NoteElement _
                | DirectiveElement _ -> [])

        and collectChildStates (node: StateNode) =
            node.Children
            |> List.collect (fun child -> child :: collectChildStates child)

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
                | StateDecl _
                | NoteElement _
                | DirectiveElement _ -> [])

        collectFromElements doc.Elements

    /// Extract the set of all state identifiers from a document.
    let stateIdentifiers (doc: StatechartDocument) : string Set =
        allStates doc
        |> List.map (fun s -> s.Identifier)
        |> Set.ofList

    /// Extract the set of all event names from transitions in a document.
    /// Filters out None events.
    let eventNames (doc: StatechartDocument) : string Set =
        allTransitions doc
        |> List.choose (fun t -> t.Event)
        |> Set.ofList

    /// Extract the set of all transition target identifiers from a document.
    /// Filters out None (internal/completion) targets.
    let transitionTargets (doc: StatechartDocument) : string Set =
        allTransitions doc
        |> List.choose (fun t -> t.Target)
        |> Set.ofList

/// Validation orchestrator (FR-006, FR-007, FR-008, FR-009, FR-013).
module Validator =

    /// Validate statechart artifacts against registered rules.
    /// Collects all results without aborting on first failure.
    /// Catches exceptions from rules and reports them as failures.
    let validate (rules: ValidationRule list) (artifacts: FormatArtifact list) : ValidationReport =
        let availableTags =
            artifacts |> List.map (fun a -> a.Format) |> Set.ofList

        let allChecks, allFailures =
            rules
            |> List.fold (fun (checksAcc, failuresAcc) rule ->
                if not (Set.isSubset rule.RequiredFormats availableTags) then
                    let missingFormats =
                        rule.RequiredFormats - availableTags
                        |> Set.toList
                        |> List.map (sprintf "%A")
                        |> String.concat ", "
                    let skipCheck =
                        { Name = rule.Name
                          Status = Skip
                          Reason = Some (sprintf "Missing formats: %s" missingFormats) }
                    (skipCheck :: checksAcc, failuresAcc)
                else
                    try
                        let checks = rule.Check artifacts
                        let newFailures =
                            checks
                            |> List.choose (fun c ->
                                match c.Status with
                                | Fail ->
                                    Some
                                        { Formats =
                                            rule.RequiredFormats
                                            |> Set.toList
                                          EntityType = "validation check"
                                          Expected = "pass"
                                          Actual = "fail"
                                          Description =
                                            match c.Reason with
                                            | Some r -> sprintf "%s: %s" c.Name r
                                            | None -> c.Name }
                                | _ -> None)
                        (List.rev checks @ checksAcc, List.rev newFailures @ failuresAcc)
                    with ex ->
                        let failCheck =
                            { Name = rule.Name
                              Status = Fail
                              Reason = Some (sprintf "Exception: %s" ex.Message) }
                        let failure =
                            { Formats =
                                rule.RequiredFormats
                                |> Set.toList
                              EntityType = "rule execution"
                              Expected = "successful execution"
                              Actual = sprintf "exception: %s" ex.Message
                              Description = sprintf "Rule '%s' threw an exception: %s" rule.Name ex.Message }
                        (failCheck :: checksAcc, failure :: failuresAcc)
            ) ([], [])

        let checks = List.rev allChecks
        let failures = List.rev allFailures

        let totalChecks =
            checks
            |> List.filter (fun c -> c.Status = Pass || c.Status = Fail)
            |> List.length

        let totalSkipped =
            checks
            |> List.filter (fun c -> c.Status = Skip)
            |> List.length

        { TotalChecks = totalChecks
          TotalSkipped = totalSkipped
          TotalFailures = failures.Length
          Checks = checks
          Failures = failures }

/// Universal self-consistency validation rules that check structural
/// integrity of individual format artifacts. All rules use
/// RequiredFormats = Set.empty (universal -- run on every artifact).
module SelfConsistencyRules =

    /// Check that all transition targets reference existing states within each artifact.
    /// Orphan targets indicate broken transitions that reference non-existent states.
    let orphanTransitionTargets: ValidationRule =
        { Name = "Orphan transition targets"
          RequiredFormats = Set.empty
          Check = fun artifacts ->
              artifacts
              |> List.collect (fun artifact ->
                  let stateIds = AstHelpers.stateIdentifiers artifact.Document
                  let targets = AstHelpers.transitionTargets artifact.Document
                  let orphans = targets - stateIds
                  if Set.isEmpty orphans then
                      [ { Name = sprintf "Orphan transition targets (%A)" artifact.Format
                          Status = Pass
                          Reason = None } ]
                  else
                      orphans
                      |> Set.toList
                      |> List.map (fun orphan ->
                          { Name = sprintf "Orphan transition target '%s' (%A)" orphan artifact.Format
                            Status = Fail
                            Reason = Some (sprintf "Transition target '%s' does not reference any state in %A artifact. Available states: %s" orphan artifact.Format (stateIds |> Set.toList |> String.concat ", ")) })) }

    /// Check that all state identifiers are unique within each artifact.
    /// Duplicate identifiers cause ambiguity in transition resolution.
    let duplicateStateIdentifiers: ValidationRule =
        { Name = "Duplicate state identifiers"
          RequiredFormats = Set.empty
          Check = fun artifacts ->
              artifacts
              |> List.collect (fun artifact ->
                  let allStates = AstHelpers.allStates artifact.Document
                  let ids = allStates |> List.map (fun s -> s.Identifier)
                  let duplicates =
                      ids
                      |> List.groupBy id
                      |> List.filter (fun (_, group) -> group.Length > 1)
                      |> List.map fst
                  if List.isEmpty duplicates then
                      [ { Name = sprintf "Duplicate state identifiers (%A)" artifact.Format
                          Status = Pass
                          Reason = None } ]
                  else
                      duplicates
                      |> List.map (fun dup ->
                          { Name = sprintf "Duplicate state identifier '%s' (%A)" dup artifact.Format
                            Status = Fail
                            Reason = Some (sprintf "State identifier '%s' appears multiple times in %A artifact" dup artifact.Format) })) }

    /// Check that required AST fields are populated in each artifact.
    /// State identifiers must be non-empty; transition sources must be non-empty.
    let requiredAstFields: ValidationRule =
        { Name = "Required AST fields"
          RequiredFormats = Set.empty
          Check = fun artifacts ->
              artifacts
              |> List.collect (fun artifact ->
                  let states = AstHelpers.allStates artifact.Document
                  let transitions = AstHelpers.allTransitions artifact.Document

                  let emptyStateIds =
                      states
                      |> List.filter (fun s -> System.String.IsNullOrWhiteSpace s.Identifier)
                      |> List.mapi (fun i _ ->
                          { Name = sprintf "Empty state identifier #%d (%A)" (i + 1) artifact.Format
                            Status = Fail
                            Reason = Some (sprintf "State at index %d has empty identifier in %A artifact" i artifact.Format) })

                  let emptyTransitionSources =
                      transitions
                      |> List.filter (fun t -> System.String.IsNullOrWhiteSpace t.Source)
                      |> List.mapi (fun i _ ->
                          { Name = sprintf "Empty transition source #%d (%A)" (i + 1) artifact.Format
                            Status = Fail
                            Reason = Some (sprintf "Transition at index %d has empty source in %A artifact" i artifact.Format) })

                  let allIssues = emptyStateIds @ emptyTransitionSources
                  if List.isEmpty allIssues then
                      [ { Name = sprintf "Required AST fields (%A)" artifact.Format
                          Status = Pass
                          Reason = None } ]
                  else
                      allIssues) }

    /// Warn about states with no incoming or outgoing transitions.
    /// Isolated states may be intentional, so this is a warning (Pass with reason), not a failure.
    let isolatedStates: ValidationRule =
        { Name = "Isolated states"
          RequiredFormats = Set.empty
          Check = fun artifacts ->
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
                            Status = Pass  // WARNING, not failure
                            Reason = Some (sprintf "State '%s' has no incoming or outgoing transitions in %A artifact (may be intentional)" stateId artifact.Format) })) }

    /// Warn about artifacts with empty state machines (no states, no transitions).
    /// An empty statechart passes structural validation but the warning alerts the developer.
    let emptyStatechart: ValidationRule =
        { Name = "Empty statechart"
          RequiredFormats = Set.empty
          Check = fun artifacts ->
              artifacts
              |> List.collect (fun artifact ->
                  let states = AstHelpers.allStates artifact.Document
                  let transitions = AstHelpers.allTransitions artifact.Document
                  if List.isEmpty states && List.isEmpty transitions then
                      [ { Name = sprintf "Empty statechart (%A)" artifact.Format
                          Status = Pass  // WARNING, not failure
                          Reason = Some (sprintf "%A artifact contains no states and no transitions" artifact.Format) } ]
                  else
                      [ { Name = sprintf "Empty statechart (%A)" artifact.Format
                          Status = Pass
                          Reason = None } ]) }

    /// All universal self-consistency rules.
    let rules: ValidationRule list =
        [ orphanTransitionTargets
          duplicateStateIdentifiers
          requiredAstFields
          isolatedStates
          emptyStatechart ]

/// Cross-format pairwise validation rules (Spec 021 - WP04).
/// Implements state name agreement, event name agreement, and transition
/// target agreement rules for all 10 pairwise format combinations (5C2).
module CrossFormatRules =

    // T025: Casing mismatch detection helper.
    // Must be defined before rule factory functions that use it.

    /// Check if a value has a case-insensitive match in a set but not an exact match.
    /// Returns a descriptive note about the casing difference, or empty string if no near-match.
    let private describeCasingMismatch (value: string) (candidates: string Set) : string =
        let nearMatch =
            candidates
            |> Set.toList
            |> List.tryFind (fun c ->
                System.String.Equals(value, c, System.StringComparison.OrdinalIgnoreCase)
                && value <> c)
        match nearMatch with
        | Some found ->
            sprintf " Note: case-insensitive match found: '%s' vs '%s' (casing differs)" value found
        | None -> ""

    // T022: State name agreement rule.

    /// Create a state name agreement rule for a specific format pair (FR-012).
    /// Checks that state identifiers agree between two format artifacts.
    /// Symmetric: checks both directions (A missing from B and B missing from A).
    let stateNameAgreement (formatA: FormatTag) (formatB: FormatTag) : ValidationRule =
        { Name = sprintf "%A-%A state name agreement" formatA formatB
          RequiredFormats = set [ formatA; formatB ]
          Check = fun artifacts ->
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
                    { Name = sprintf "State '%s' missing from %A" stateId formatB
                      Status = Fail
                      Reason = Some (sprintf "State '%s' exists in %A but not in %A.%s" stateId formatA formatB casingNote) })

            let failuresFromA =
                missingFromA
                |> Set.toList
                |> List.map (fun stateId ->
                    let casingNote = describeCasingMismatch stateId statesA
                    { Name = sprintf "State '%s' missing from %A" stateId formatA
                      Status = Fail
                      Reason = Some (sprintf "State '%s' exists in %A but not in %A.%s" stateId formatB formatA casingNote) })

            let allFailures = failuresFromB @ failuresFromA
            if List.isEmpty allFailures then
                [ { Name = sprintf "%A-%A state name agreement" formatA formatB
                    Status = Pass
                    Reason = None } ]
            else
                allFailures }

    // T023: Event name agreement rule.

    /// Create an event name agreement rule for a specific format pair (FR-012).
    /// Checks that event names agree between two format artifacts.
    /// Symmetric: checks both directions.
    let eventNameAgreement (formatA: FormatTag) (formatB: FormatTag) : ValidationRule =
        { Name = sprintf "%A-%A event name agreement" formatA formatB
          RequiredFormats = set [ formatA; formatB ]
          Check = fun artifacts ->
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
                    { Name = sprintf "Event '%s' missing from %A" eventName formatB
                      Status = Fail
                      Reason = Some (sprintf "Event '%s' exists in %A but not in %A.%s" eventName formatA formatB casingNote) })

            let failuresFromA =
                missingFromA
                |> Set.toList
                |> List.map (fun eventName ->
                    let casingNote = describeCasingMismatch eventName eventsA
                    { Name = sprintf "Event '%s' missing from %A" eventName formatA
                      Status = Fail
                      Reason = Some (sprintf "Event '%s' exists in %A but not in %A.%s" eventName formatB formatA casingNote) })

            let allFailures = failuresFromB @ failuresFromA
            if List.isEmpty allFailures then
                [ { Name = sprintf "%A-%A event name agreement" formatA formatB
                    Status = Pass
                    Reason = None } ]
            else
                allFailures }

    // T024: Transition target agreement rule.

    /// Create a transition target agreement rule for a specific format pair.
    /// Checks that transition targets in format A reference states that exist
    /// in format B, and vice versa.
    let transitionTargetAgreement (formatA: FormatTag) (formatB: FormatTag) : ValidationRule =
        { Name = sprintf "%A-%A transition target agreement" formatA formatB
          RequiredFormats = set [ formatA; formatB ]
          Check = fun artifacts ->
            let artA = artifacts |> List.find (fun a -> a.Format = formatA)
            let artB = artifacts |> List.find (fun a -> a.Format = formatB)
            let targetsA = AstHelpers.transitionTargets artA.Document
            let statesB = AstHelpers.stateIdentifiers artB.Document
            let targetsB = AstHelpers.transitionTargets artB.Document
            let statesA = AstHelpers.stateIdentifiers artA.Document

            // Targets in A should reference states in B
            let missingInB = targetsA - statesB
            // Targets in B should reference states in A
            let missingInA = targetsB - statesA

            let failuresAtoB =
                missingInB
                |> Set.toList
                |> List.map (fun target ->
                    let casingNote = describeCasingMismatch target statesB
                    { Name = sprintf "Transition target '%s' from %A missing in %A states" target formatA formatB
                      Status = Fail
                      Reason = Some (sprintf "Transition target '%s' in %A does not correspond to any state in %A.%s" target formatA formatB casingNote) })

            let failuresBtoA =
                missingInA
                |> Set.toList
                |> List.map (fun target ->
                    let casingNote = describeCasingMismatch target statesA
                    { Name = sprintf "Transition target '%s' from %A missing in %A states" target formatB formatA
                      Status = Fail
                      Reason = Some (sprintf "Transition target '%s' in %A does not correspond to any state in %A.%s" target formatB formatA casingNote) })

            let allFailures = failuresAtoB @ failuresBtoA
            if List.isEmpty allFailures then
                [ { Name = sprintf "%A-%A transition target agreement" formatA formatB
                    Status = Pass
                    Reason = None } ]
            else
                allFailures }

    // T027: Rule generation for all 10 pairwise combinations.

    /// All unique pairs of format tags (5 choose 2 = 10 pairs).
    let private formatPairs : (FormatTag * FormatTag) list =
        let tags = [ Wsd; Alps; Scxml; Smcat; XState ]
        [ for i in 0 .. tags.Length - 2 do
            for j in i + 1 .. tags.Length - 1 do
                yield (tags.[i], tags.[j]) ]

    /// All cross-format rules generated from pairwise combinations.
    /// 30 rules total (10 pairs x 3 check types: state name, event name, transition target).
    let private allPairwiseRules : ValidationRule list =
        formatPairs
        |> List.collect (fun (a, b) ->
            [ stateNameAgreement a b
              eventNameAgreement a b
              transitionTargetAgreement a b ])

    // T026: CrossFormatRules module aggregate.

    /// All cross-format rules for all applicable format pairs.
    /// Contains 30 rules (10 pairs x 3 check types).
    let rules : ValidationRule list =
        allPairwiseRules
