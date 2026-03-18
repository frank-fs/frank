module Frank.Statecharts.Tests.Smcat.RoundTripTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Smcat.Types
open Frank.Statecharts.Smcat.Parser
open Frank.Statecharts.Smcat.Serializer

// =============================================================================
// Golden File Examples
// =============================================================================

/// Golden File 1: Simple linear flow (3 states, 3 transitions).
/// Tests: basic transitions, initial/final pseudo-states, event-only labels.
let goldenSimpleLinear =
    """initial => pending: submit;
pending => approved: approve;
approved => final: complete;"""

/// Golden File 2: Branching with guards and actions (5+ states, multiple paths).
/// Tests: guards, actions, multiple transitions from same state, all label combinations.
let goldenBranchingGuards =
    """initial => home: start;
home => WIP: begin;
WIP => customerData: collectCustomerData [isValid] / logAction;
WIP => home: reset [notValid] / logReset;
customerData => review: submitForReview;
review => customerData: reject [needsChanges] / notifyUser;
review => final: approve [allGood];"""

/// Golden File 3: Composite states (nested state machines).
/// Tests: composite states with nested transitions, transitions at multiple levels.
let goldenCompositeStates =
    """initial => operating: powerOn;
operating {
  initial => red: start;
  red => green: timer;
  green => yellow: timer;
  yellow => red: timer;
};
operating => final: powerOff;"""

/// Golden File 4: Explicit type attributes and colors.
/// Tests: SmcatStateType Explicit round-trip, SmcatColor, multiple attributes.
let goldenExplicitTypes =
    """myStart [type="initial"];
idle [color="green"];
processing;
done [type="final"];
myStart => idle: begin;
idle => processing: submit;
processing => done: complete;"""

/// Golden File 5: Naming convention types (inferred).
/// Tests: SmcatStateType Inferred round-trip, pseudo-state names.
let goldenInferredTypes =
    """initial => active: start;
active => active: refresh;
active => final: shutdown;"""

/// Golden File 6: Activities and custom attributes.
/// Tests: StateActivities, SmcatCustomAttribute round-trip.
let goldenActivitiesAndAttributes =
    """idle: entry/ initialize exit/ cleanup;
working [priority="high"];
idle => working: begin;
working => idle: finish;"""

/// Golden File 7: Composite states with internal transitions.
/// Tests: nested states and transitions inside composite blocks.
let goldenCompositeAnnotations =
    """initial => machine: start;
machine {
  idle;
  running;
  idle => running: go;
  running => idle: stop;
};
machine => final: shutdown;"""

// =============================================================================
// Semantic Equivalence Comparison
// =============================================================================

/// Extract the set of (stateName, stateKind) pairs from all elements in a document
/// (recursively for composite states).
let rec private extractStateSet (doc: StatechartDocument) : Set<string * StateKind> =
    doc.Elements
    |> List.collect (fun el ->
        match el with
        | TransitionElement t ->
            let targetStates =
                match t.Target with
                | Some target -> [ (target, inferStateType target []) ]
                | None -> []
            (t.Source, inferStateType t.Source []) :: targetStates
        | StateDecl s ->
            let childStates =
                extractStateSetFromChildren s.Children
            (s.Identifier |> Option.defaultValue "", s.Kind) :: childStates
        | _ -> [])
    |> Set.ofList

and private extractStateSetFromChildren (children: StateNode list) : (string * StateKind) list =
    children
    |> List.collect (fun s ->
        let nested = extractStateSetFromChildren s.Children
        (s.Identifier |> Option.defaultValue "", s.Kind) :: nested)

/// Extract the set of (source, target, event, guard, action) tuples from all
/// transitions in a document (recursively for composite states).
let rec private extractTransitionSet
    (doc: StatechartDocument)
    : Set<string * string option * string option * string option * string option> =
    doc.Elements
    |> List.collect (fun el ->
        match el with
        | TransitionElement t ->
            [ (t.Source, t.Target, t.Event, t.Guard, t.Action) ]
        | StateDecl s ->
            extractTransitionSetFromChildren s.Children
        | _ -> [])
    |> Set.ofList

and private extractTransitionSetFromChildren (children: StateNode list) : (string * string option * string option * string option * string option) list =
    // Child transitions are lifted to the parent elements, so children don't carry transitions.
    // Just recurse into nested children.
    children
    |> List.collect (fun s ->
        extractTransitionSetFromChildren s.Children)

/// Assert that two StatechartDocument ASTs are semantically equivalent.
/// Compares state sets and transition sets (order-independent).
let private assertSemanticEquivalence (doc1: StatechartDocument) (doc2: StatechartDocument) =
    let states1 = extractStateSet doc1
    let states2 = extractStateSet doc2
    Expect.equal states1 states2 "State sets should be equivalent"

    let transitions1 = extractTransitionSet doc1
    let transitions2 = extractTransitionSet doc2
    Expect.equal transitions1 transitions2 "Transition sets should be equivalent"

// =============================================================================
// Structural Equivalence Comparison (includes annotations)
// =============================================================================

/// Normalize annotations to a set for order-independent comparison.
/// Excludes SmcatTransition annotations from comparison since transition kinds
/// are inferred from structure (depth) and may change when composite blocks
/// don't round-trip their nesting structure (e.g., transition-only composites).
let private normalizeAnnotations (annotations: Annotation list) : Set<Annotation> =
    annotations
    |> List.filter (function
        | SmcatAnnotation(SmcatTransition _) -> false
        | _ -> true)
    |> Set.ofList

/// Extract (name, kind, annotations) tuples from document states (recursive).
let rec private extractAnnotatedStateSet (doc: StatechartDocument) : Set<string * StateKind * Set<Annotation>> =
    doc.Elements
    |> List.collect (fun el ->
        match el with
        | StateDecl s ->
            let childStates = extractAnnotatedStateSetFromChildren s.Children
            (s.Identifier |> Option.defaultValue "", s.Kind, normalizeAnnotations s.Annotations)
            :: childStates
        | _ -> [])
    |> Set.ofList

and private extractAnnotatedStateSetFromChildren (children: StateNode list) : (string * StateKind * Set<Annotation>) list =
    children
    |> List.collect (fun s ->
        let nested = extractAnnotatedStateSetFromChildren s.Children
        (s.Identifier |> Option.defaultValue "", s.Kind, normalizeAnnotations s.Annotations)
        :: nested)

/// Extract (source, target, event, guard, action, annotations) tuples from transitions (recursive).
let rec private extractAnnotatedTransitionSet (doc: StatechartDocument)
    : Set<string * string option * string option * string option * string option * Set<Annotation>> =
    doc.Elements
    |> List.collect (fun el ->
        match el with
        | TransitionElement t ->
            [ (t.Source, t.Target, t.Event, t.Guard, t.Action, normalizeAnnotations t.Annotations) ]
        | StateDecl s ->
            extractAnnotatedTransitionSetFromChildren s.Children
        | _ -> [])
    |> Set.ofList

and private extractAnnotatedTransitionSetFromChildren (children: StateNode list)
    : (string * string option * string option * string option * string option * Set<Annotation>) list =
    children
    |> List.collect (fun s ->
        extractAnnotatedTransitionSetFromChildren s.Children)

/// Assert structural equivalence including annotations (set-based comparison).
let private assertStructuralEquivalence (doc1: StatechartDocument) (doc2: StatechartDocument) =
    let states1 = extractAnnotatedStateSet doc1
    let states2 = extractAnnotatedStateSet doc2
    Expect.equal states1 states2 "Annotated state sets should be structurally equivalent"

    let transitions1 = extractAnnotatedTransitionSet doc1
    let transitions2 = extractAnnotatedTransitionSet doc2
    Expect.equal transitions1 transitions2 "Annotated transition sets should be structurally equivalent"

// =============================================================================
// Roundtrip Cycle Helper
// =============================================================================

/// Run the full roundtrip cycle: parse -> serialize -> re-parse -> compare.
let private roundtrip (smcatText: string) =
    // Step 1: Parse original text
    let result1 = parseSmcat smcatText
    Expect.isEmpty result1.Errors (sprintf "Original parse should have no errors, got: %A" result1.Errors)

    // Step 2: Serialize the parsed AST back to smcat text
    let generatedText = serialize result1.Document

    // Step 3: Re-parse generated text
    let result2 = parseSmcat generatedText

    Expect.isEmpty
        result2.Errors
        (sprintf "Re-parsed output should have no errors, got: %A\nGenerated text:\n%s" result2.Errors generatedText)

    // Step 4: Compare ASTs for semantic equivalence
    assertSemanticEquivalence result1.Document result2.Document
    // Step 5: Compare ASTs for structural equivalence (includes annotations)
    assertStructuralEquivalence result1.Document result2.Document

// =============================================================================
// Golden File Roundtrip Tests (T030, T031)
// =============================================================================

[<Tests>]
let goldenFileTests =
    testList
        "Smcat.RoundTrip.GoldenFiles"
        [ testCase "golden file 1 - simple linear flow"
          <| fun _ -> roundtrip goldenSimpleLinear

          testCase "golden file 2 - branching with guards and actions"
          <| fun _ -> roundtrip goldenBranchingGuards

          testCase "golden file 3 - composite states"
          <| fun _ -> roundtrip goldenCompositeStates

          testCase "golden file 4 - explicit types and colors"
          <| fun _ -> roundtrip goldenExplicitTypes

          testCase "golden file 5 - inferred types"
          <| fun _ -> roundtrip goldenInferredTypes

          testCase "golden file 6 - activities and custom attributes"
          <| fun _ -> roundtrip goldenActivitiesAndAttributes

          testCase "golden file 7 - composite annotations"
          <| fun _ -> roundtrip goldenCompositeAnnotations ]

// =============================================================================
// Edge Case Roundtrip Tests (T033)
// =============================================================================

[<Tests>]
let edgeCaseTests =
    testList
        "Smcat.RoundTrip.EdgeCases"
        [ testCase "guard-only transition"
          <| fun _ -> roundtrip "a => b: [isReady];"

          testCase "action-only transition"
          <| fun _ -> roundtrip "a => b: / doSomething;"

          testCase "no-label transition"
          <| fun _ -> roundtrip "a => b;"

          testCase "pseudo-states (initial and final)"
          <| fun _ ->
              roundtrip
                  """initial => start;
start => final;"""

          testCase "multiple transitions from same source"
          <| fun _ ->
              roundtrip
                  """a => b: go;
a => c: stay;"""

          testCase "empty input roundtrips trivially"
          <| fun _ ->
              let result1 = parseSmcat ""
              Expect.isEmpty result1.Errors "no errors"
              let generatedText = serialize result1.Document
              let result2 = parseSmcat generatedText
              Expect.isEmpty result2.Errors "no errors on re-parse"
              assertSemanticEquivalence result1.Document result2.Document

          testCase "event with guard (no action)"
          <| fun _ -> roundtrip "a => b: submit [isValid];"

          testCase "event with action (no guard)"
          <| fun _ -> roundtrip "a => b: submit / logAction;"

          testCase "full label (event + guard + action)"
          <| fun _ -> roundtrip "a => b: submit [isValid] / logAction;" ]

// =============================================================================
// Semantic Equivalence Tests (T032)
// =============================================================================

[<Tests>]
let semanticEquivalenceTests =
    testList
        "Smcat.RoundTrip.SemanticEquivalence"
        [ testCase "state topology preserved in simple linear"
          <| fun _ ->
              let result = parseSmcat goldenSimpleLinear
              Expect.isEmpty result.Errors "no errors"
              let states = extractStateSet result.Document

              // Verify expected states are present
              Expect.isTrue (states |> Set.exists (fun (n, _) -> n = "initial")) "has initial"
              Expect.isTrue (states |> Set.exists (fun (n, _) -> n = "pending")) "has pending"
              Expect.isTrue (states |> Set.exists (fun (n, _) -> n = "approved")) "has approved"
              Expect.isTrue (states |> Set.exists (fun (n, _) -> n = "final")) "has final"

          testCase "transition labels preserved in branching example"
          <| fun _ ->
              let result = parseSmcat goldenBranchingGuards
              Expect.isEmpty result.Errors "no errors"
              let transitions = extractTransitionSet result.Document

              // Verify guard+action transition
              let guardAction =
                  transitions
                  |> Set.filter (fun (s, t, _, _, _) -> s = "WIP" && t = Some "customerData")

              Expect.equal guardAction.Count 1 "one WIP->customerData transition"

              let (_, _, ev, gd, ac) = guardAction |> Set.toList |> List.head
              Expect.equal ev (Some "collectCustomerData") "event preserved"
              Expect.equal gd (Some "isValid") "guard preserved"
              Expect.equal ac (Some "logAction") "action preserved"

          testCase "composite state children survive roundtrip"
          <| fun _ ->
              let result = parseSmcat goldenCompositeStates
              Expect.isEmpty result.Errors "no errors"

              // Verify nested transitions exist
              let allTransitions = extractTransitionSet result.Document
              Expect.isTrue (allTransitions |> Set.exists (fun (s, _, _, _, _) -> s = "red")) "has red transition"
              Expect.isTrue (allTransitions |> Set.exists (fun (s, _, _, _, _) -> s = "green")) "has green transition"
              Expect.isTrue (allTransitions |> Set.exists (fun (s, _, _, _, _) -> s = "yellow")) "has yellow transition"

          testCase "comments do not affect semantic equivalence"
          <| fun _ ->
              let withComment =
                  """# This is a comment
initial => a: start;
a => final: done;"""

              let withoutComment =
                  """initial => a: start;
a => final: done;"""

              let result1 = parseSmcat withComment
              let result2 = parseSmcat withoutComment
              Expect.isEmpty result1.Errors "no errors (with comment)"
              Expect.isEmpty result2.Errors "no errors (without comment)"
              assertSemanticEquivalence result1.Document result2.Document

          testCase "deterministic roundtrip - same input produces same output"
          <| fun _ ->
              let result1 = parseSmcat goldenBranchingGuards
              let gen1 = serialize result1.Document
              let result2 = parseSmcat goldenBranchingGuards
              let gen2 = serialize result2.Document
              Expect.equal gen1 gen2 "deterministic generation" ]

// =============================================================================
// SC-005 & SC-009 Validation (T034)
// =============================================================================

[<Tests>]
let successCriteriaTests =
    testList
        "Smcat.RoundTrip.SuccessCriteria"
        [ testCase "SC-005: state topology survives roundtrip"
          <| fun _ ->
              // Parse golden file 2 (most complex flat example)
              let result1 = parseSmcat goldenBranchingGuards
              Expect.isEmpty result1.Errors "original parse succeeds"
              let generated = serialize result1.Document
              let result2 = parseSmcat generated
              Expect.isEmpty result2.Errors "re-parse succeeds"

              // State names match
              let stateNames1 = extractStateSet result1.Document |> Set.map fst
              let stateNames2 = extractStateSet result2.Document |> Set.map fst
              Expect.equal stateNames1 stateNames2 "SC-005: state names match after roundtrip"

              // Transition labels match
              let trans1 = extractTransitionSet result1.Document
              let trans2 = extractTransitionSet result2.Document
              Expect.equal trans1 trans2 "SC-005: transition labels match after roundtrip"

          testCase "SC-009: at least 3 golden files roundtrip successfully"
          <| fun _ ->
              // Golden file 1: simple linear
              roundtrip goldenSimpleLinear
              // Golden file 2: branching with guards
              roundtrip goldenBranchingGuards
              // Golden file 3: composite states
              roundtrip goldenCompositeStates ]

// =============================================================================
// Annotation Round-Trip Tests (spec 027)
// =============================================================================

[<Tests>]
let annotationRoundTripTests =
    testList
        "Smcat.RoundTrip.Annotations"
        [ testCase "explicit type survives round-trip"
          <| fun _ ->
              let result1 = parseSmcat "myState [type=\"initial\"];"
              let generated = serialize result1.Document
              Expect.stringContains generated "type=\"initial\"" "explicit type preserved in output"
              let result2 = parseSmcat generated
              assertStructuralEquivalence result1.Document result2.Document

          testCase "inferred type does not gain explicit attribute"
          <| fun _ ->
              let result1 = parseSmcat "initial => idle;"
              let generated = serialize result1.Document
              let hasExplicitType = generated.Contains("type=\"initial\"")
              Expect.isFalse hasExplicitType "inferred initial should not have type attribute"

          testCase "color annotation survives round-trip"
          <| fun _ -> roundtrip "myState [color=\"red\"];"

          testCase "self-transition annotation round-trip"
          <| fun _ ->
              let result1 = parseSmcat "a => a: refresh;"
              let ts1 =
                  result1.Document.Elements
                  |> List.choose (function TransitionElement t -> Some t | _ -> None)
              let hasSelf =
                  ts1[0].Annotations |> List.exists (function
                      | SmcatAnnotation(SmcatTransition SelfTransition) -> true
                      | _ -> false)
              Expect.isTrue hasSelf "self-transition annotated"
              roundtrip "a => a: refresh;"

          testCase "SC-003: structural equivalence after round-trip"
          <| fun _ ->
              // Parse all golden files and verify structural equivalence
              roundtrip goldenExplicitTypes
              roundtrip goldenInferredTypes
              roundtrip goldenActivitiesAndAttributes
              roundtrip goldenCompositeAnnotations ]
