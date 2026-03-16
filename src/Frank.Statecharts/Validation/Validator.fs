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
        allStates doc |> List.map _.Identifier |> Set.ofList

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
                            let ruleChecks = rule.Check artifacts

                            let ruleFailures =
                                ruleChecks
                                |> List.choose (fun c ->
                                    if c.Status = Fail then
                                        Some
                                            { Formats = []
                                              EntityType = "validation"
                                              Expected = "pass"
                                              Actual = "fail"
                                              Description =
                                                match c.Reason with
                                                | Some reason ->
                                                    sprintf "Rule '%s' failed: %s" rule.Name reason
                                                | None -> sprintf "Rule '%s' failed" rule.Name }
                                    else
                                        None)

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
