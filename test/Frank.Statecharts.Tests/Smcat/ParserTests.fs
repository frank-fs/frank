module Frank.Statecharts.Tests.Smcat.ParserTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Smcat.Types
open Frank.Statecharts.Smcat.Parser

/// Helper to extract transitions from a ParseResult.
let private transitions (result: ParseResult) =
    result.Document.Elements
    |> List.choose (fun e ->
        match e with
        | TransitionElement t -> Some t
        | _ -> None)

/// Helper to extract state declarations from a ParseResult.
let private states (result: ParseResult) =
    result.Document.Elements
    |> List.choose (fun e ->
        match e with
        | StateDecl s -> Some s
        | _ -> None)

[<Tests>]
let emptyInputTests =
    testList
        "Smcat.Parser.EmptyInput"
        [ testCase "empty string"
          <| fun _ ->
              let result = parseSmcat ""
              Expect.isEmpty result.Document.Elements "empty document"
              Expect.isEmpty result.Errors "no errors"

          testCase "whitespace only"
          <| fun _ ->
              let result = parseSmcat "   \n  "
              Expect.isEmpty result.Document.Elements "empty document"
              Expect.isEmpty result.Errors "no errors"

          testCase "comment only"
          <| fun _ ->
              let result = parseSmcat "# just a comment"
              Expect.isEmpty result.Document.Elements "empty document"
              Expect.isEmpty result.Errors "no errors"

          testCase "multiple comments and whitespace"
          <| fun _ ->
              let result = parseSmcat "# comment 1\n# comment 2\n   "
              Expect.isEmpty result.Document.Elements "empty document"
              Expect.isEmpty result.Errors "no errors" ]

[<Tests>]
let transitionTests =
    testList
        "Smcat.Parser.Transitions"
        [ testCase "simple transition without label"
          <| fun _ ->
              let result = parseSmcat "a => b;"
              let ts = transitions result
              Expect.equal ts.Length 1 "one transition"
              Expect.equal ts[0].Source "a" "source"
              Expect.equal ts[0].Target (Some "b") "target"
              Expect.equal ts[0].Event None "no event"
              Expect.equal ts[0].Guard None "no guard"
              Expect.equal ts[0].Action None "no action"
              Expect.isEmpty result.Errors "no errors"

          testCase "transition with event label"
          <| fun _ ->
              let result = parseSmcat "a => b: event;"
              let ts = transitions result
              Expect.equal ts.Length 1 "one transition"
              Expect.equal ts[0].Event (Some "event") "event"
              Expect.equal ts[0].Guard None "no guard"
              Expect.equal ts[0].Action None "no action"

          testCase "transition with full label"
          <| fun _ ->
              let result = parseSmcat "a => b: event [guard] / action;"
              let ts = transitions result
              Expect.equal ts.Length 1 "one transition"
              Expect.equal ts[0].Event (Some "event") "event"
              Expect.equal ts[0].Guard (Some "guard") "guard"
              Expect.equal ts[0].Action (Some "action") "action"

          testCase "multiple transitions"
          <| fun _ ->
              let result = parseSmcat "a => b;\nc => d;"
              let ts = transitions result
              Expect.equal ts.Length 2 "two transitions"
              Expect.equal ts[0].Source "a" "first source"
              Expect.equal ts[1].Source "c" "second source"

          testCase "transition with comma terminator"
          <| fun _ ->
              let result = parseSmcat "a => b, c => d;"
              let ts = transitions result
              Expect.equal ts.Length 2 "two transitions with comma"

          testCase "transition with newline terminator"
          <| fun _ ->
              let result = parseSmcat "a => b\nc => d"
              let ts = transitions result
              Expect.equal ts.Length 2 "two transitions with newline"

          testCase "transition with quoted string target"
          <| fun _ ->
              let result = parseSmcat "a => \"target state\";"
              let ts = transitions result
              Expect.equal ts[0].Target (Some "target state") "quoted target" ]

[<Tests>]
let onboardingExampleTests =
    testList
        "Smcat.Parser.OnboardingExample"
        [ testCase "quickstart example"
          <| fun _ ->
              let source =
                  "# Simple onboarding state machine\ninitial => home: start;\nhome => WIP: begin;\nWIP => customerData: collectCustomerData [isValid] / logAction;\ncustomerData => final: complete;"

              let result = parseSmcat source
              Expect.isEmpty result.Errors "no errors"
              let ts = transitions result
              Expect.equal ts.Length 4 "four transitions"

              // Transition 1: initial => home: start
              Expect.equal ts[0].Source "initial" "t1 source"
              Expect.equal ts[0].Target (Some "home") "t1 target"
              Expect.equal ts[0].Event (Some "start") "t1 event"
              Expect.equal ts[0].Guard None "t1 no guard"
              Expect.equal ts[0].Action None "t1 no action"

              // Transition 2: home => WIP: begin
              Expect.equal ts[1].Source "home" "t2 source"
              Expect.equal ts[1].Target (Some "WIP") "t2 target"
              Expect.equal ts[1].Event (Some "begin") "t2 event"

              // Transition 3: WIP => customerData: collectCustomerData [isValid] / logAction
              Expect.equal ts[2].Source "WIP" "t3 source"
              Expect.equal ts[2].Target (Some "customerData") "t3 target"
              Expect.equal ts[2].Event (Some "collectCustomerData") "t3 event"
              Expect.equal ts[2].Guard (Some "isValid") "t3 guard"
              Expect.equal ts[2].Action (Some "logAction") "t3 action"

              // Transition 4: customerData => final: complete
              Expect.equal ts[3].Source "customerData" "t4 source"
              Expect.equal ts[3].Target (Some "final") "t4 target"
              Expect.equal ts[3].Event (Some "complete") "t4 event" ]

[<Tests>]
let pseudoStateTests =
    testList
        "Smcat.Parser.PseudoStates"
        [ testCase "initial pseudo-state from transition source"
          <| fun _ ->
              let result = parseSmcat "initial => home: start;"
              let ts = transitions result
              Expect.equal ts[0].Source "initial" "source name is initial"

          testCase "final pseudo-state from transition target"
          <| fun _ ->
              let result = parseSmcat "home => final;"
              let ts = transitions result
              Expect.equal ts[0].Target (Some "final") "target name is final"

          testCase "choice pseudo-state"
          <| fun _ ->
              let result = parseSmcat "^choice => a;"
              let ts = transitions result
              Expect.equal ts[0].Source "^choice" "source is ^choice"

          testCase "deep history pseudo-state"
          <| fun _ ->
              let result = parseSmcat "deep.history => a;"
              let ts = transitions result
              Expect.equal ts[0].Source "deep.history" "source is deep.history"

          testCase "state declaration with initial type"
          <| fun _ ->
              let result = parseSmcat "initial;"
              let ss = states result
              Expect.equal ss.Length 1 "one state"
              Expect.equal ss[0].Kind StateKind.Initial "initial state type"

          testCase "state declaration with final type"
          <| fun _ ->
              let result = parseSmcat "final;"
              let ss = states result
              Expect.equal ss.Length 1 "one state"
              Expect.equal ss[0].Kind StateKind.Final "final state type"

          testCase "forkjoin pseudo-state"
          <| fun _ ->
              let result = parseSmcat "]forkjoin => a;"
              let ts = transitions result
              Expect.equal ts[0].Source "]forkjoin" "source is ]forkjoin" ]

[<Tests>]
let stateDeclarationTests =
    testList
        "Smcat.Parser.StateDeclarations"
        [ testCase "single state declaration"
          <| fun _ ->
              let result = parseSmcat "idle;"
              let ss = states result
              Expect.equal ss.Length 1 "one state"
              Expect.equal ss[0].Identifier (Some "idle") "state name"
              Expect.equal ss[0].Kind StateKind.Regular "regular type"

          testCase "comma-separated states"
          <| fun _ ->
              let result = parseSmcat "idle, running, stopped;"
              let ss = states result
              Expect.equal ss.Length 3 "three states"
              Expect.equal ss[0].Identifier (Some "idle") "first state"
              Expect.equal ss[1].Identifier (Some "running") "second state"
              Expect.equal ss[2].Identifier (Some "stopped") "third state"

          testCase "state with attributes"
          <| fun _ ->
              let result = parseSmcat "on [label=\"Lamp on\" color=\"#008800\"];"
              let ss = states result
              Expect.equal ss.Length 1 "one state"
              // Annotations now hold SmcatAnnotation values (label and color)
              // label is extracted to ss[0].Label; color becomes SmcatAnnotation(SmcatColor ...)
              Expect.equal ss[0].Label (Some "Lamp on") "label extracted from attributes"
              let colorAnnotation =
                  ss[0].Annotations
                  |> List.tryPick (function
                      | SmcatAnnotation(SmcatColor c) -> Some c
                      | _ -> None)
              Expect.equal colorAnnotation (Some "#008800") "color annotation"

          testCase "state label from attribute"
          <| fun _ ->
              let result = parseSmcat "on [label=\"Lamp on\"];"
              let ss = states result
              Expect.equal ss[0].Label (Some "Lamp on") "label extracted from attributes" ]

[<Tests>]
let activityTests =
    testList
        "Smcat.Parser.Activities"
        [ testCase "state with entry activity"
          <| fun _ ->
              let result = parseSmcat "active: entry/ start;"
              let ss = states result
              Expect.equal ss.Length 1 "one state"
              Expect.isSome ss[0].Activities "has activities"
              Expect.equal ss[0].Activities.Value.Entry [ "start" ] "entry activity"

          testCase "state with exit activity"
          <| fun _ ->
              let result = parseSmcat "active: exit/ stop;"
              let ss = states result
              Expect.isSome ss[0].Activities "has activities"
              Expect.equal ss[0].Activities.Value.Exit [ "stop" ] "exit activity"

          testCase "state with multiple activities"
          <| fun _ ->
              let result = parseSmcat "active: entry/ start exit/ stop;"
              let ss = states result
              Expect.isSome ss[0].Activities "has activities"
              Expect.equal ss[0].Activities.Value.Entry [ "start" ] "entry"
              Expect.equal ss[0].Activities.Value.Exit [ "stop" ] "exit" ]

[<Tests>]
let compositeStateTests =
    testList
        "Smcat.Parser.CompositeStates"
        [ testCase "simple composite state"
          <| fun _ ->
              let result = parseSmcat "parent {\n  child1 => child2;\n};"
              let ss = states result
              Expect.equal ss.Length 1 "one state"
              Expect.equal ss[0].Identifier (Some "parent") "parent name"
              // child1 => child2 is a transition only (no state declarations inside composite)
              // Transitions are lifted to the parent elements list; Children only holds StateDecl nodes
              let allTransitions = transitions result
              let childTs = allTransitions |> List.filter (fun t -> t.Source = "child1")
              Expect.equal childTs.Length 1 "one child transition"
              Expect.equal childTs[0].Source "child1" "child source"
              Expect.equal childTs[0].Target (Some "child2") "child target"

          testCase "nested composite state (2 levels)"
          <| fun _ ->
              let result = parseSmcat "parent {\n  child {\n    grandchild1 => grandchild2;\n  };\n};"
              let ss = states result
              Expect.equal ss[0].Identifier (Some "parent") "parent name"
              Expect.isNonEmpty ss[0].Children "has children"
              let childSs = ss[0].Children
              Expect.equal childSs.Length 1 "one child state"
              Expect.equal childSs[0].Identifier (Some "child") "child name"
              // grandchild1 => grandchild2 is a transition (no state declarations inside child)
              // Transitions are lifted; child.Children only holds StateDecl nodes

          testCase "composite state with transition and state"
          <| fun _ ->
              let source = "parent {\n  child1 => child2: event;\n  child2;\n};"
              let result = parseSmcat source
              Expect.isEmpty result.Errors "no errors"
              Expect.isNonEmpty (states result).[0].Children "has children"

          testCase "deeply nested composites (5 levels)"
          <| fun _ ->
              let source =
                  "a { b { c { d { e { f => g; }; }; }; }; };"
              let result = parseSmcat source
              Expect.isEmpty result.Errors "no errors"
              // Verify nesting depth
              let mutable depth = 0
              let mutable current = Some (states result).[0]
              while current.IsSome do
                  depth <- depth + 1
                  let childStates = current.Value.Children
                  current <-
                      if childStates.Length > 0 then Some childStates[0]
                      else None
              Expect.isGreaterThanOrEqual depth 5 "at least 5 levels deep" ]

[<Tests>]
let attributeTests =
    testList
        "Smcat.Parser.Attributes"
        [ testCase "transition with attributes"
          <| fun _ ->
              let result = parseSmcat "a => b [color=\"red\"];"
              let ts = transitions result
              // Transition annotations are now Annotation list
              let colorAnnotation =
                  ts[0].Annotations
                  |> List.tryPick (function
                      | SmcatAnnotation(SmcatColor c) -> Some c
                      | _ -> None)
              Expect.equal colorAnnotation (Some "red") "color annotation"

          testCase "state with type attribute overrides naming"
          <| fun _ ->
              let result = parseSmcat "myState [type=initial];"
              let ss = states result
              Expect.equal ss[0].Kind StateKind.Initial "type attribute overrides" ]

[<Tests>]
let edgeCaseTests =
    testList
        "Smcat.Parser.EdgeCases"
        [ testCase "semicolon-only input"
          <| fun _ ->
              let result = parseSmcat ";"
              Expect.isEmpty result.Errors "no errors from empty statement"

          testCase "comma-only input"
          <| fun _ ->
              let result = parseSmcat ","
              Expect.isEmpty result.Errors "no errors from empty statement"

          testCase "transition without label has no event/guard/action"
          <| fun _ ->
              let result = parseSmcat "a => b;"
              let ts = transitions result
              Expect.equal ts[0].Event None "no event"
              Expect.equal ts[0].Guard None "no guard"
              Expect.equal ts[0].Action None "no action"

          testCase "multiple statements on one line with commas"
          <| fun _ ->
              let result = parseSmcat "a => b, c => d;"
              let ts = transitions result
              Expect.equal ts.Length 2 "two transitions" ]
