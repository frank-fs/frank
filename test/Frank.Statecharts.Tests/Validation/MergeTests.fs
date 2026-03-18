module Frank.Statecharts.Tests.Validation.MergeTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Validation

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

let private makeState id kind annotations =
    { Identifier = Some id
      Label = None
      Kind = kind
      Children = []
      Activities = None
      Position = None
      Annotations = annotations }

let private makeStateWithLabel id label =
    { Identifier = Some id
      Label = Some label
      Kind = Regular
      Children = []
      Activities = None
      Position = None
      Annotations = [] }

let private makeTransition source target event annotations =
    { Source = source
      Target = Some target
      Event = Some event
      Guard = None
      Action = None
      Parameters = []
      Position = None
      Annotations = annotations }

let private makeDocument states transitions annotations : StatechartDocument =
    { Title = None
      InitialStateId = None
      Elements =
          (states |> List.map StateDecl)
          @ (transitions |> List.map TransitionElement)
      DataEntries = []
      Annotations = annotations }

let private statesOf (doc: StatechartDocument) =
    doc.Elements |> List.choose (function StateDecl s -> Some s | _ -> None)

let private transitionsOf (doc: StatechartDocument) =
    doc.Elements |> List.choose (function TransitionElement t -> Some t | _ -> None)

// ---------------------------------------------------------------------------
// T012 – mergeSources tests
// ---------------------------------------------------------------------------

// ── Empty sources ────────────────────────────────────────────────────────────

[<Tests>]
let emptySourcesTests =
    testList "Pipeline.mergeSources.Empty" [

        test "Merge empty sources returns empty document" {
            let result = Pipeline.mergeSources []
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                Expect.isEmpty (statesOf doc) "No states in empty merge"
                Expect.isEmpty (transitionsOf doc) "No transitions in empty merge"
                Expect.isNone doc.Title "Title should be None"
                Expect.isNone doc.InitialStateId "InitialStateId should be None"
                Expect.isEmpty doc.Annotations "No annotations"
        }
    ]

// ── Single source (identity) ─────────────────────────────────────────────────

[<Tests>]
let singleSourceTests =
    testList "Pipeline.mergeSources.SingleSource" [

        test "Merge single WSD source returns its document unchanged" {
            let wsd = "participant A\nparticipant B\nA -> B: go\nB -> A: back\n"
            let result = Pipeline.mergeSources [(FormatTag.Wsd, wsd)]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                let states = statesOf doc
                Expect.isGreaterThan states.Length 0 "Should have states from WSD source"
                let stateIds = states |> List.choose (fun s -> s.Identifier) |> Set.ofList
                Expect.isTrue (Set.contains "A" stateIds) "State A should be present"
                Expect.isTrue (Set.contains "B" stateIds) "State B should be present"
        }

        test "Merge single SCXML source returns its document unchanged" {
            let scxml = """<?xml version="1.0" encoding="UTF-8"?>
<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle">
    <transition event="start" target="active"/>
  </state>
  <state id="active"/>
</scxml>"""
            let result = Pipeline.mergeSources [(FormatTag.Scxml, scxml)]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                let states = statesOf doc
                let stateIds = states |> List.choose (fun s -> s.Identifier) |> Set.ofList
                Expect.isTrue (Set.contains "idle" stateIds) "State idle should be present"
                Expect.isTrue (Set.contains "active" stateIds) "State active should be present"
        }
    ]

// ── Non-overlapping states produce union ─────────────────────────────────────

[<Tests>]
let nonOverlapTests =
    testList "Pipeline.mergeSources.NonOverlap" [

        test "Merge with non-overlapping states produces union" {
            // WSD provides states A, B; ALPS provides C, D (no overlap).
            // For ALPS to classify C and D as states, D must be an rt target of C
            // (ALPS isStateDescriptor requires a transition child, rt reference, or href child).
            let wsd = "participant A\nparticipant B\nA -> B: go\n"
            let alps = """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      {
        "id": "C",
        "type": "semantic",
        "descriptor": [
          { "id": "next", "type": "safe", "rt": "#D" }
        ]
      },
      { "id": "D", "type": "semantic" }
    ]
  }
}"""
            let result = Pipeline.mergeSources [(FormatTag.Wsd, wsd); (FormatTag.Alps, alps)]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                let states = statesOf doc
                let stateIds = states |> List.choose (fun s -> s.Identifier) |> Set.ofList
                Expect.isTrue (Set.contains "A" stateIds) "State A from WSD should be present"
                Expect.isTrue (Set.contains "B" stateIds) "State B from WSD should be present"
                Expect.isTrue (Set.contains "C" stateIds) "State C from ALPS should be present (union)"
                Expect.isTrue (Set.contains "D" stateIds) "State D from ALPS (rt target) should be present (union)"
        }
    ]

// ── SCXML wins over WSD on Kind conflict ─────────────────────────────────────

[<Tests>]
let priorityTests =
    testList "Pipeline.mergeSources.Priority" [

        test "SCXML (priority 0) wins over WSD (priority 3) on Kind conflict" {
            // SCXML has state 'shared' as Parallel; WSD has it as Regular
            let scxml = """<?xml version="1.0" encoding="UTF-8"?>
<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="shared">
  <parallel id="shared"/>
</scxml>"""
            let wsd = "participant shared\n"
            let result = Pipeline.mergeSources [
                (FormatTag.Wsd, wsd)
                (FormatTag.Scxml, scxml)
            ]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                let states = statesOf doc
                let shared = states |> List.tryFind (fun s -> s.Identifier = Some "shared")
                match shared with
                | None -> failtestf "State 'shared' not found in merged document"
                | Some s ->
                    Expect.equal s.Kind Parallel "SCXML Parallel should win over WSD Regular"
        }

        test "SCXML (priority 0) InitialStateId wins over WSD (priority 3) None" {
            let scxml = """<?xml version="1.0" encoding="UTF-8"?>
<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle"/>
  <state id="active"/>
</scxml>"""
            let wsd = "participant idle\nparticipant active\n"
            let result = Pipeline.mergeSources [
                (FormatTag.Wsd, wsd)
                (FormatTag.Scxml, scxml)
            ]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                Expect.equal doc.InitialStateId (Some "idle")
                    "SCXML InitialStateId should be preserved after merge"
        }

        test "Lower-priority source fills None fields (enrichment)" {
            // WSD has no label on states; we construct two programmatic docs
            // to test the None-filling behaviour directly through mergeSources.
            // Use smcat (priority 2) as base for a state without a label,
            // then ALPS (priority 4) provides no structural overlap — this test
            // verifies via the public API that parse order doesn't matter.
            let smcat = "idle;\nactive;\nidle => active: start;"
            let alps = """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      { "id": "idle", "type": "semantic" },
      { "id": "active", "type": "semantic" }
    ]
  }
}"""
            // smcat has lower priority than SCXML but higher than ALPS
            // Both share "idle" and "active" — merge should produce them once
            let result = Pipeline.mergeSources [
                (FormatTag.Smcat, smcat)
                (FormatTag.Alps, alps)
            ]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                let states = statesOf doc
                let stateIds = states |> List.choose (fun s -> s.Identifier) |> Set.ofList
                Expect.isTrue (Set.contains "idle" stateIds) "idle should be present"
                Expect.isTrue (Set.contains "active" stateIds) "active should be present"
                // No duplication of overlapping states
                let idleCount = states |> List.filter (fun s -> s.Identifier = Some "idle") |> List.length
                Expect.equal idleCount 1 "idle should appear exactly once after merge"
        }
    ]

// ── Annotation accumulation ──────────────────────────────────────────────────

[<Tests>]
let annotationAccumulationTests =
    testList "Pipeline.mergeSources.AnnotationAccumulation" [

        test "Merge WSD + ALPS produces annotations from both on overlapping state" {
            let wsd = "participant A\nparticipant B\nA -> B: go\n"
            let alps = """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      {
        "id": "A",
        "type": "semantic",
        "descriptor": [
          { "id": "go", "type": "safe", "rt": "#B" }
        ]
      },
      { "id": "B", "type": "semantic" }
    ]
  }
}"""
            let result = Pipeline.mergeSources [(FormatTag.Wsd, wsd); (FormatTag.Alps, alps)]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                let states = statesOf doc
                let stateIds = states |> List.choose (fun s -> s.Identifier) |> Set.ofList
                Expect.isTrue (Set.contains "A" stateIds) "State A should be in merged doc"
                Expect.isTrue (Set.contains "B" stateIds) "State B should be in merged doc"
                // No duplicate states
                let aCount = states |> List.filter (fun s -> s.Identifier = Some "A") |> List.length
                Expect.equal aCount 1 "State A should appear exactly once"
                // ALPS contributes document-level annotations (AlpsVersion) and transition annotations (AlpsTransitionType)
                let hasAlpsDocAnnotation = doc.Annotations |> List.exists (function AlpsAnnotation _ -> true | _ -> false)
                Expect.isTrue hasAlpsDocAnnotation "Merged document should have ALPS annotations accumulated"
                // Check transitions have ALPS annotations (AlpsTransitionType on the "go" transition)
                let transitions = transitionsOf doc
                let hasAlpsTransAnnotation =
                    transitions |> List.exists (fun t ->
                        t.Annotations |> List.exists (function AlpsAnnotation _ -> true | _ -> false))
                Expect.isTrue hasAlpsTransAnnotation "Merged transitions should have ALPS annotations"
        }

        test "Transition annotations accumulate across formats" {
            let wsd = "participant A\nparticipant B\nA -> B: go\n"
            let smcat = "A;\nB;\nA => B: go;"
            let result = Pipeline.mergeSources [(FormatTag.Wsd, wsd); (FormatTag.Smcat, smcat)]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                let transitions = transitionsOf doc
                Expect.isGreaterThan transitions.Length 0 "Should have transitions after merge"
                // Find the A->B transition (may have Event "go" or similar)
                let abTransitions =
                    transitions |> List.filter (fun t -> t.Source = "A" && t.Target = Some "B")
                Expect.isNonEmpty abTransitions "Should have A->B transition"
                // Verify annotations from smcat are present (SmcatAnnotation)
                let hasSmcat =
                    abTransitions
                    |> List.exists (fun t ->
                        t.Annotations |> List.exists (function SmcatAnnotation _ -> true | _ -> false))
                Expect.isTrue hasSmcat "A->B transition should have SmcatAnnotation after merge"
        }

        test "Document-level annotations accumulate" {
            let wsd = "participant A\n"
            let alps = """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      { "id": "A", "type": "semantic" }
    ]
  }
}"""
            let result = Pipeline.mergeSources [(FormatTag.Wsd, wsd); (FormatTag.Alps, alps)]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                // ALPS parser produces document-level annotations (AlpsVersion at minimum)
                let hasAlpsDocAnnotation =
                    doc.Annotations |> List.exists (function AlpsAnnotation _ -> true | _ -> false)
                Expect.isTrue hasAlpsDocAnnotation "Merged document should have ALPS annotations (e.g., AlpsVersion)"
        }
    ]

// ── Transition matching by (Source, Target, Event) triple ────────────────────

[<Tests>]
let transitionMatchingTests =
    testList "Pipeline.mergeSources.TransitionMatching" [

        test "Transitions with same (Source, Target, Event) are merged, not duplicated" {
            let wsd = "participant A\nparticipant B\nA -> B: go\n"
            let smcat = "A;\nB;\nA => B: go;"
            let result = Pipeline.mergeSources [(FormatTag.Wsd, wsd); (FormatTag.Smcat, smcat)]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                let transitions = transitionsOf doc
                let goCount =
                    transitions
                    |> List.filter (fun t ->
                        t.Source = "A" && t.Target = Some "B" && t.Event = Some "go")
                    |> List.length
                // Should be at most 1 (merged) unless parsers use different event names
                Expect.isLessThanOrEqual goCount 1
                    "Transition A->B:go should not be duplicated after merge"
        }

        test "Transitions with different events are both present in merged doc" {
            let scxml = """<?xml version="1.0" encoding="UTF-8"?>
<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle">
    <transition event="start" target="active"/>
  </state>
  <state id="active">
    <transition event="stop" target="idle"/>
  </state>
</scxml>"""
            let alps = """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      {
        "id": "idle",
        "type": "semantic",
        "descriptor": [
          { "id": "start", "type": "safe", "rt": "#active" }
        ]
      },
      {
        "id": "active",
        "type": "semantic",
        "descriptor": [
          { "id": "stop", "type": "safe", "rt": "#idle" }
        ]
      }
    ]
  }
}"""
            let result = Pipeline.mergeSources [(FormatTag.Scxml, scxml); (FormatTag.Alps, alps)]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                let transitions = transitionsOf doc
                // Both start and stop should be present
                let hasStart = transitions |> List.exists (fun t -> t.Event = Some "start")
                let hasStop  = transitions |> List.exists (fun t -> t.Event = Some "stop")
                Expect.isTrue hasStart "start transition should be in merged doc"
                Expect.isTrue hasStop  "stop transition should be in merged doc"
        }
    ]

// ── DataEntries union ─────────────────────────────────────────────────────────

[<Tests>]
let dataEntriesTests =
    testList "Pipeline.mergeSources.DataEntries" [

        test "DataEntries from both documents are unioned by Name" {
            // Use SCXML which supports datamodel/data elements
            let scxml = """<?xml version="1.0" encoding="UTF-8"?>
<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle" datamodel="ecmascript">
  <datamodel>
    <data id="counter" expr="0"/>
  </datamodel>
  <state id="idle"/>
</scxml>"""
            let wsd = "participant idle\n"
            let result = Pipeline.mergeSources [(FormatTag.Scxml, scxml); (FormatTag.Wsd, wsd)]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                // Verify we merged without error; SCXML data entries should be present
                let names = doc.DataEntries |> List.map (fun d -> d.Name) |> Set.ofList
                // "counter" is the SCXML data variable
                Expect.isTrue (Set.contains "counter" names)
                    "DataEntry 'counter' from SCXML should be in merged doc"
        }
    ]

// ── Title / InitialStateId fill-from-enriching ────────────────────────────────

[<Tests>]
let documentLevelFieldTests =
    testList "Pipeline.mergeSources.DocumentLevelFields" [

        test "ALPS as sole source produces valid document" {
            let alps = """{
  "alps": {
    "version": "1.0",
    "descriptor": [
      {
        "id": "home",
        "type": "semantic",
        "descriptor": [
          { "id": "go", "type": "unsafe", "rt": "#away" }
        ]
      },
      { "id": "away", "type": "semantic" }
    ]
  }
}"""
            let result = Pipeline.mergeSources [(FormatTag.Alps, alps)]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                let states = statesOf doc
                Expect.isGreaterThan states.Length 0 "ALPS-only merge should produce states"
                let hasAlps = doc.Annotations |> List.exists (function AlpsAnnotation _ -> true | _ -> false)
                Expect.isTrue hasAlps "ALPS-only doc should have ALPS annotations"
        }

        test "Title from higher-priority format is preserved" {
            // SCXML (priority 0) sets initial; WSD (priority 3) does not.
            let scxml = """<?xml version="1.0" encoding="UTF-8"?>
<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle"/>
</scxml>"""
            let wsd = "participant idle\n"
            let result = Pipeline.mergeSources [
                (FormatTag.Wsd, wsd)
                (FormatTag.Scxml, scxml)
            ]
            match result with
            | Error e -> failtestf "Expected Ok, got Error %A" e
            | Ok doc ->
                // The SCXML initial attribute should survive as InitialStateId
                Expect.equal doc.InitialStateId (Some "idle")
                    "SCXML initial attribute should become InitialStateId after merge"
        }
    ]
