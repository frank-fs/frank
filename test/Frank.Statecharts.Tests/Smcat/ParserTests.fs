module Frank.Statecharts.Tests.Smcat.ParserTests

open Expecto
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
        | StateDeclaration s -> Some s
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
              Expect.equal ts[0].Target "b" "target"
              Expect.equal ts[0].Label None "no label"
              Expect.isEmpty result.Errors "no errors"

          testCase "transition with event label"
          <| fun _ ->
              let result = parseSmcat "a => b: event;"
              let ts = transitions result
              Expect.equal ts.Length 1 "one transition"
              Expect.isSome ts[0].Label "has label"
              Expect.equal ts[0].Label.Value.Event (Some "event") "event"
              Expect.equal ts[0].Label.Value.Guard None "no guard"
              Expect.equal ts[0].Label.Value.Action None "no action"

          testCase "transition with full label"
          <| fun _ ->
              let result = parseSmcat "a => b: event [guard] / action;"
              let ts = transitions result
              Expect.equal ts.Length 1 "one transition"
              let label = ts[0].Label.Value
              Expect.equal label.Event (Some "event") "event"
              Expect.equal label.Guard (Some "guard") "guard"
              Expect.equal label.Action (Some "action") "action"

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
              Expect.equal ts[0].Target "target state" "quoted target" ]

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
              Expect.equal ts[0].Target "home" "t1 target"
              Expect.equal ts[0].Label.Value.Event (Some "start") "t1 event"
              Expect.equal ts[0].Label.Value.Guard None "t1 no guard"
              Expect.equal ts[0].Label.Value.Action None "t1 no action"

              // Transition 2: home => WIP: begin
              Expect.equal ts[1].Source "home" "t2 source"
              Expect.equal ts[1].Target "WIP" "t2 target"
              Expect.equal ts[1].Label.Value.Event (Some "begin") "t2 event"

              // Transition 3: WIP => customerData: collectCustomerData [isValid] / logAction
              Expect.equal ts[2].Source "WIP" "t3 source"
              Expect.equal ts[2].Target "customerData" "t3 target"
              Expect.equal ts[2].Label.Value.Event (Some "collectCustomerData") "t3 event"
              Expect.equal ts[2].Label.Value.Guard (Some "isValid") "t3 guard"
              Expect.equal ts[2].Label.Value.Action (Some "logAction") "t3 action"

              // Transition 4: customerData => final: complete
              Expect.equal ts[3].Source "customerData" "t4 source"
              Expect.equal ts[3].Target "final" "t4 target"
              Expect.equal ts[3].Label.Value.Event (Some "complete") "t4 event" ]

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
              Expect.equal ts[0].Target "final" "target name is final"

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
              Expect.equal ss[0].StateType Initial "initial state type"

          testCase "state declaration with final type"
          <| fun _ ->
              let result = parseSmcat "final;"
              let ss = states result
              Expect.equal ss.Length 1 "one state"
              Expect.equal ss[0].StateType Final "final state type"

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
              Expect.equal ss[0].Name "idle" "state name"
              Expect.equal ss[0].StateType Regular "regular type"

          testCase "comma-separated states"
          <| fun _ ->
              let result = parseSmcat "idle, running, stopped;"
              let ss = states result
              Expect.equal ss.Length 3 "three states"
              Expect.equal ss[0].Name "idle" "first state"
              Expect.equal ss[1].Name "running" "second state"
              Expect.equal ss[2].Name "stopped" "third state"

          testCase "state with attributes"
          <| fun _ ->
              let result = parseSmcat "on [label=\"Lamp on\" color=\"#008800\"];"
              let ss = states result
              Expect.equal ss.Length 1 "one state"
              Expect.equal ss[0].Attributes.Length 2 "two attributes"
              Expect.equal ss[0].Attributes[0].Key "label" "first attr key"
              Expect.equal ss[0].Attributes[0].Value "Lamp on" "first attr value"
              Expect.equal ss[0].Attributes[1].Key "color" "second attr key"
              Expect.equal ss[0].Attributes[1].Value "#008800" "second attr value"

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
              Expect.equal ss[0].Activities.Value.Entry (Some "start") "entry activity"

          testCase "state with exit activity"
          <| fun _ ->
              let result = parseSmcat "active: exit/ stop;"
              let ss = states result
              Expect.isSome ss[0].Activities "has activities"
              Expect.equal ss[0].Activities.Value.Exit (Some "stop") "exit activity"

          testCase "state with multiple activities"
          <| fun _ ->
              let result = parseSmcat "active: entry/ start exit/ stop;"
              let ss = states result
              Expect.isSome ss[0].Activities "has activities"
              Expect.equal ss[0].Activities.Value.Entry (Some "start") "entry"
              Expect.equal ss[0].Activities.Value.Exit (Some "stop") "exit" ]

[<Tests>]
let compositeStateTests =
    testList
        "Smcat.Parser.CompositeStates"
        [ testCase "simple composite state"
          <| fun _ ->
              let result = parseSmcat "parent {\n  child1 => child2;\n};"
              let ss = states result
              Expect.equal ss.Length 1 "one state"
              Expect.equal ss[0].Name "parent" "parent name"
              Expect.isSome ss[0].Children "has children"
              let children = ss[0].Children.Value
              let childTs = children.Elements |> List.choose (fun e -> match e with TransitionElement t -> Some t | _ -> None)
              Expect.equal childTs.Length 1 "one child transition"
              Expect.equal childTs[0].Source "child1" "child source"
              Expect.equal childTs[0].Target "child2" "child target"

          testCase "nested composite state (2 levels)"
          <| fun _ ->
              let result = parseSmcat "parent {\n  child {\n    grandchild1 => grandchild2;\n  };\n};"
              let ss = states result
              Expect.equal ss[0].Name "parent" "parent name"
              Expect.isSome ss[0].Children "has children"
              let childSs =
                  ss[0].Children.Value.Elements
                  |> List.choose (fun e -> match e with StateDeclaration s -> Some s | _ -> None)
              Expect.equal childSs.Length 1 "one child state"
              Expect.equal childSs[0].Name "child" "child name"
              Expect.isSome childSs[0].Children "child has children"

          testCase "composite state with transition and state"
          <| fun _ ->
              let source = "parent {\n  child1 => child2: event;\n  child2;\n};"
              let result = parseSmcat source
              Expect.isEmpty result.Errors "no errors"
              Expect.isSome (states result).[0].Children "has children"

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
                  let childStates =
                      current.Value.Children
                      |> Option.map (fun doc ->
                          doc.Elements |> List.choose (fun e -> match e with StateDeclaration s -> Some s | _ -> None))
                      |> Option.defaultValue []
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
              Expect.equal ts[0].Attributes.Length 1 "one attribute"
              Expect.equal ts[0].Attributes[0].Key "color" "attr key"
              Expect.equal ts[0].Attributes[0].Value "red" "attr value"

          testCase "state with type attribute overrides naming"
          <| fun _ ->
              let result = parseSmcat "myState [type=initial];"
              let ss = states result
              Expect.equal ss[0].StateType Initial "type attribute overrides" ]

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

          testCase "transition without label has None"
          <| fun _ ->
              let result = parseSmcat "a => b;"
              let ts = transitions result
              Expect.equal ts[0].Label None "no label"

          testCase "multiple statements on one line with commas"
          <| fun _ ->
              let result = parseSmcat "a => b, c => d;"
              let ts = transitions result
              Expect.equal ts.Length 2 "two transitions" ]
