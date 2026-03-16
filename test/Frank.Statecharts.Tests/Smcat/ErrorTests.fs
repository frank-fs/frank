module Frank.Statecharts.Tests.Smcat.ErrorTests

open Expecto
open Frank.Statecharts.Smcat.Types
open Frank.Statecharts.Smcat.Parser
open Frank.Statecharts.Smcat.Lexer

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

// T024: Acceptance scenario tests from User Story 5

[<Tests>]
let missingColonTests =
    testList
        "Smcat.Error.MissingColon"
        [ testCase "missing colon before label produces structured error"
          <| fun _ ->
              // US5 Acceptance Scenario 1: "on => off switch flicked;"
              let result = parseSmcat "on => off switch flicked;"
              Expect.isNonEmpty result.Errors "should have errors"
              let err = result.Errors[0]
              // Verify line/column present
              Expect.isGreaterThan err.Position.Line 0 "has line"
              Expect.isGreaterThan err.Position.Column 0 "has column"
              // Verify expected mentions colon or semicolon
              Expect.stringContains err.Expected "':'" "expected mentions colon"
              // Verify corrective example shows correct syntax
              Expect.stringContains err.CorrectiveExample "=>" "corrective example has arrow"
              Expect.stringContains err.CorrectiveExample ":" "corrective example has colon"

          testCase "missing colon error still produces partial transition"
          <| fun _ ->
              let result = parseSmcat "on => off switch flicked;"
              let ts = transitions result
              Expect.equal ts.Length 1 "partial transition emitted"
              Expect.equal ts[0].Source "on" "source preserved"
              Expect.equal ts[0].Target "off" "target preserved" ]

[<Tests>]
let invalidArrowTests =
    testList
        "Smcat.Error.InvalidArrow"
        [ testCase "invalid arrow syntax ==> produces error"
          <| fun _ ->
              // US5 Acceptance Scenario 2: "on ==> off;"
              let result = parseSmcat "on ==> off;"
              Expect.isNonEmpty result.Errors "should have errors"
              let err = result.Errors[0]
              // Verify position
              Expect.isGreaterThan err.Position.Line 0 "has line"
              Expect.isGreaterThan err.Position.Column 0 "has column"
              // Verify error mentions arrow
              Expect.stringContains err.Description "==>" "mentions invalid arrow"
              Expect.stringContains err.Description "=>" "mentions correct arrow"
              // Verify corrective example
              Expect.stringContains err.CorrectiveExample "=>" "corrective example has correct arrow"

          testCase "invalid arrow still produces partial transition"
          <| fun _ ->
              let result = parseSmcat "on ==> off;"
              let ts = transitions result
              Expect.equal ts.Length 1 "partial transition emitted"
              Expect.equal ts[0].Source "on" "source preserved"
              Expect.equal ts[0].Target "off" "target preserved" ]

[<Tests>]
let unclosedBracketTests =
    testList
        "Smcat.Error.UnclosedBracket"
        [ testCase "unclosed bracket in label produces error with position"
          <| fun _ ->
              // US5 Acceptance Scenario 3: "on => off: start [guard;"
              let result = parseSmcat "on => off: start [guard;"
              Expect.isNonEmpty result.Errors "should have errors"
              let err = result.Errors[0]
              // Verify position
              Expect.isGreaterThan err.Position.Line 0 "has line"
              Expect.isGreaterThan err.Position.Column 0 "has column"
              // Verify error mentions unclosed bracket
              Expect.stringContains err.Description "Unclosed bracket" "mentions unclosed bracket"
              // Verify corrective example
              Expect.stringContains err.CorrectiveExample "[guard]" "corrective example shows closed bracket"

          testCase "unclosed bracket at EOF"
          <| fun _ ->
              let result = parseSmcat "on => off: start [guard"
              Expect.isNonEmpty result.Errors "should have errors"
              let err = result.Errors[0]
              Expect.stringContains err.Description "Unclosed bracket" "mentions unclosed bracket"
              Expect.stringContains err.Found "end of input" "found is end of input" ]

[<Tests>]
let multipleErrorTests =
    testList
        "Smcat.Error.MultipleErrors"
        [ testCase "multiple errors are all collected"
          <| fun _ ->
              // US5 Acceptance Scenario 4: multiple errors in same input
              let input = "on => off switch flicked;\na ==> b;\nc => d: event [guard;"
              let result = parseSmcat input
              Expect.isGreaterThanOrEqual result.Errors.Length 3 "at least 3 errors collected"

              // Verify each error has all required fields
              for err in result.Errors do
                  Expect.isGreaterThan err.Position.Line 0 "has line"
                  Expect.isGreaterThan err.Position.Column 0 "has column"
                  Expect.isNotEmpty err.Description "has description"
                  Expect.isNotEmpty err.CorrectiveExample "has corrective example"

          testCase "multiple errors across separate lines"
          <| fun _ ->
              // Use tokens that produce parser errors: => without source, { without state
              let input = "=> target;\n{ };\n=> another;"
              let result = parseSmcat input
              Expect.isGreaterThanOrEqual result.Errors.Length 2 "multiple errors"

              // Different lines
              let lines = result.Errors |> List.map (fun e -> e.Position.Line) |> List.distinct
              Expect.isGreaterThan lines.Length 1 "errors on different lines" ]

[<Tests>]
let errorRecoveryTests =
    testList
        "Smcat.Error.Recovery"
        [ testCase "error followed by valid statement - valid statement is parsed"
          <| fun _ ->
              // Error on first transition (missing target), valid second transition
              let result = parseSmcat "a => ; b => c: event;"
              Expect.isNonEmpty result.Errors "has errors"
              // The valid transition should still be parsed
              let ts = transitions result
              Expect.isGreaterThanOrEqual ts.Length 1 "at least one transition parsed"
              let validT = ts |> List.tryFind (fun t -> t.Source = "b" && t.Target = "c")
              Expect.isSome validT "valid transition parsed after error"

          testCase "multiple valid statements around error"
          <| fun _ ->
              // Use => without source as the error (it's a valid token but invalid parser syntax)
              let result = parseSmcat "a => b;\n=> ;\nc => d;"
              Expect.isNonEmpty result.Errors "has errors from => without source"
              let ts = transitions result
              // Both valid transitions should be present
              Expect.isGreaterThanOrEqual ts.Length 2 "both valid transitions parsed"
              Expect.equal ts[0].Source "a" "first transition source"
              Expect.equal ts[1].Source "c" "second transition source"

          testCase "valid state declarations survive error between them"
          <| fun _ ->
              // { at top level without state name is an error
              let result = parseSmcat "idle;\n{ };\nrunning;"
              Expect.isNonEmpty result.Errors "has errors"
              let ss = states result
              Expect.isGreaterThanOrEqual ss.Length 2 "both states parsed"

          testCase "best-effort partial result contains all successful elements"
          <| fun _ ->
              let result = parseSmcat "a => b;\nc => ;\nd => e;"
              // c => ; is error (missing target)
              // a => b and d => e should be in the document
              let ts = transitions result
              let validSources = ts |> List.map (fun t -> t.Source) |> Set.ofList
              Expect.isTrue (validSources.Contains "a") "first transition parsed"
              Expect.isTrue (validSources.Contains "d") "third transition parsed" ]

[<Tests>]
let errorLimitTests =
    testList
        "Smcat.Error.Limit"
        [ testCase "error limit stops at configured max"
          <| fun _ ->
              // Create input with many parser errors -- use => without target
              let errors = [ for _ in 1..20 -> "a => ;" ]
              let input = System.String.Join("\n", errors)
              let tokens = tokenize input
              let result = parse tokens 5
              // Should have at most 5 errors (the limit)
              Expect.isLessThanOrEqual result.Errors.Length 5 "at most 5 errors"
              Expect.isGreaterThanOrEqual result.Errors.Length 5 "at least 5 errors"

          testCase "fewer errors than limit collects all"
          <| fun _ ->
              // Use inputs that produce actual parser errors (missing target after =>)
              let result = parseSmcat "a => ;\nb => ;\nc => ;"
              Expect.isGreaterThanOrEqual result.Errors.Length 3 "3 errors collected"

          testCase "no errors produces empty list"
          <| fun _ ->
              let result = parseSmcat "a => b;"
              Expect.isEmpty result.Errors "no errors"

          testCase "parseSmcat uses default limit of 50"
          <| fun _ ->
              // parseSmcat should pass maxErrors=50
              let errors = [ for _ in 1..100 -> "x => ;" ]
              let input = System.String.Join("\n", errors)
              let result = parseSmcat input
              Expect.isLessThanOrEqual result.Errors.Length 50 "at most 50 errors" ]

[<Tests>]
let warningTests =
    testList
        "Smcat.Error.Warnings"
        [ testCase "pseudo-state name with explicit type attribute produces warning"
          <| fun _ ->
              // State name "initialPhase" matches initial naming convention
              // but explicit [type=regular] overrides
              let result = parseSmcat "initialPhase [type=regular];"
              Expect.isNonEmpty result.Warnings "should have warnings"
              let w = result.Warnings[0]
              Expect.stringContains w.Description "initialPhase" "mentions state name"
              Expect.stringContains w.Description "Initial" "mentions inferred type"
              Expect.isSome w.Suggestion "has suggestion"

          testCase "duplicate state declaration produces warning"
          <| fun _ ->
              let result = parseSmcat "idle;\nidle;"
              Expect.isNonEmpty result.Warnings "should have warnings"
              let w = result.Warnings[0]
              Expect.stringContains w.Description "idle" "mentions state name"
              Expect.stringContains w.Description "multiple times" "mentions duplication"
              Expect.isSome w.Suggestion "has suggestion"

          testCase "unclosed bracket in label propagates warning from LabelParser"
          <| fun _ ->
              // When LabelParser detects unclosed bracket, it produces a warning
              // But Parser also detects it as an error for statement-ending brackets
              // Test that at least a warning or error is emitted
              let result = parseSmcat "on => off: event [guard"
              let hasUnclosedMessage =
                  result.Warnings |> List.exists (fun w ->
                      w.Description.Contains("Unclosed") || w.Description.Contains("bracket"))
                  || result.Errors |> List.exists (fun e ->
                      e.Description.Contains("Unclosed") || e.Description.Contains("bracket"))
              Expect.isTrue hasUnclosedMessage "unclosed bracket reported" ]

[<Tests>]
let edgeCaseTests =
    testList
        "Smcat.Error.EdgeCases"
        [ testCase "unknown lexer chars are silently skipped - no crash"
          <| fun _ ->
              // @ and other unrecognized chars are skipped by the lexer
              // # starts a comment (rest of line skipped)
              // This should not crash, just produce empty output
              let result = parseSmcat "@#$%^&*"
              // No exception thrown -- that's the main assertion
              ()

          testCase "parser errors from invalid token sequences do not crash"
          <| fun _ ->
              // Produce many parser errors with valid tokens in invalid positions
              let result = parseSmcat "=> => => => =>"
              Expect.isNonEmpty result.Errors "has errors"
              // No crash -- that's the key assertion

          testCase "very long identifier input does not crash"
          <| fun _ ->
              let junk = System.String('x', 500)
              let result = parseSmcat junk
              // Should parse as a single state declaration, no crash
              let ss = states result
              Expect.equal ss.Length 1 "single long identifier is valid state"

          testCase "single TransitionArrow followed by EOF produces error"
          <| fun _ ->
              let result = parseSmcat "=>"
              Expect.isNonEmpty result.Errors "has errors for bare arrow"

          testCase "empty state name error"
          <| fun _ ->
              let result = parseSmcat "=> target;"
              Expect.isNonEmpty result.Errors "has errors for missing source"

          testCase "unclosed composite block"
          <| fun _ ->
              let result = parseSmcat "parent { child1 => child2;"
              Expect.isNonEmpty result.Errors "has errors for unclosed brace"
              let err = result.Errors |> List.tryFind (fun e ->
                  e.Description.Contains("closing '}'") || e.Description.Contains("}")
                  || e.Expected.Contains("}"))
              Expect.isSome err "error mentions closing brace"

          testCase "missing target state name"
          <| fun _ ->
              let result = parseSmcat "a => ;"
              Expect.isNonEmpty result.Errors "has errors"
              let err = result.Errors[0]
              Expect.stringContains err.Description "target" "mentions target"
              Expect.stringContains err.CorrectiveExample "=>" "corrective example has arrow"

          testCase "forward progress guaranteed - no infinite loop on malformed input"
          <| fun _ ->
              // This is a regression test to ensure no infinite loops.
              // If the parser loops infinitely, this test will timeout.
              let result = parseSmcat "} } } } } } } }"
              // Should not hang -- produces errors and completes
              Expect.isNonEmpty result.Errors "has errors"

          testCase "all ParseFailure fields are populated"
          <| fun _ ->
              let result = parseSmcat "on => off switch flicked;"
              Expect.isNonEmpty result.Errors "has errors"
              let err = result.Errors[0]
              Expect.isGreaterThan err.Position.Line 0 "Position.Line populated"
              Expect.isGreaterThan err.Position.Column 0 "Position.Column populated"
              Expect.isNotEmpty err.Description "Description populated"
              Expect.isNotEmpty err.Expected "Expected populated"
              Expect.isNotEmpty err.Found "Found populated"
              Expect.isNotEmpty err.CorrectiveExample "CorrectiveExample populated" ]
