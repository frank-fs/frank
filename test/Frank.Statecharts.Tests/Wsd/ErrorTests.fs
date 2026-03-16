module Frank.Statecharts.Tests.Wsd.ErrorTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Wsd.Lexer
open Frank.Statecharts.Wsd.Parser

let private transitions (result: ParseResult) =
    result.Document.Elements
    |> List.choose (function
        | TransitionElement t -> Some t
        | _ -> None)

let private notes (result: ParseResult) =
    result.Document.Elements
    |> List.choose (function
        | NoteElement n -> Some n
        | _ -> None)

let private stateDecls (result: ParseResult) =
    result.Document.Elements
    |> List.choose (function
        | StateDecl s -> Some s
        | _ -> None)

/// Extract guard pairs from a NoteContent's annotations
let private noteGuard (note: NoteContent) =
    note.Annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdGuardData pairs) -> Some pairs
        | _ -> None)

[<Tests>]
let errorTests =
    testList
        "Error Recovery"
        [
          // === 1. Skip-to-newline recovery ===
          testCase "skip-to-newline recovery: error on one line, valid on next"
          <| fun _ ->
              let result = parseWsd "@@invalid\nparticipant Client\nClient->Client: hello\n"

              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error from invalid line"
              let decls = stateDecls result
              Expect.hasLength decls 1 "participant still parsed"
              Expect.equal decls.[0].Identifier "Client" "correct participant"
              let edges = transitions result
              Expect.hasLength edges 1 "message still parsed after error"
              Expect.equal edges.[0].Event (Some "hello") "correct message label"

          // === 2. Multiple errors collected ===
          testCase "multiple errors collected (3+)"
          <| fun _ ->
              let result = parseWsd "@@err1\n@@err2\n@@err3\nparticipant OK\n"

              Expect.isGreaterThanOrEqual result.Errors.Length 3 "at least three errors"
              let decls = stateDecls result
              Expect.hasLength decls 1 "valid participant still parsed"

          // === 3. Error limit (60 errors, limit 50) ===
          testCase "error limit: 60 error lines with limit 50 produces 51 errors"
          <| fun _ ->
              let lines =
                  [ for i in 1..60 do
                        yield sprintf "@@err%d" i ]

              let source = System.String.Join("\n", lines) + "\n"
              let tokens = tokenize source
              let result = parse tokens 50
              // Should have 50 real errors + 1 "Error limit reached" = 51
              Expect.equal result.Errors.Length 51 "50 errors + 1 limit message"

              let limitMsg =
                  result.Errors
                  |> List.tryFind (fun e -> e.Description.Contains("Error limit reached"))

              Expect.isSome limitMsg "has error limit message"

          // === 4. Error limit of 1 ===
          testCase "error limit of 1 produces 2 errors (1 real + limit message)"
          <| fun _ ->
              let tokens = tokenize "@@err1\n@@err2\n@@err3\n"
              let result = parse tokens 1
              Expect.equal result.Errors.Length 2 "1 real error + limit message"
              Expect.stringContains result.Errors.[0].Description "Unexpected" "first is real error"
              Expect.isTrue (result.Errors.[1].Description.Contains("Error limit reached")) "second is limit"

          // === 5. Structured failure fields populated ===
          testCase "structured failure fields: all 5 fields populated"
          <| fun _ ->
              let result = parseWsd "participant\n"
              Expect.hasLength result.Errors 1 "one error"
              let err = result.Errors.[0]
              Expect.isGreaterThan err.Position.Value.Line 0 "position has line"
              Expect.isGreaterThan err.Position.Value.Column 0 "position has column"
              Expect.isNonEmpty err.Description "description non-empty"
              Expect.isNonEmpty err.Expected "expected non-empty"
              Expect.isNonEmpty err.Found "found non-empty"
              Expect.isNonEmpty err.CorrectiveExample "corrective example non-empty"

          // === 6. Corrective example for missing receiver ===
          testCase "corrective example for missing receiver after arrow"
          <| fun _ ->
              let result = parseWsd "Client->\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"
              let err = result.Errors.[0]
              Expect.isNonEmpty err.CorrectiveExample "corrective example provided"
              Expect.isTrue (err.CorrectiveExample.Contains("->")) "corrective example contains arrow"

          // === 7. Corrective example for unexpected identifier (unrecognized arrow) ===
          testCase "corrective examples catalog: unexpected identifier includes arrow forms"
          <| fun _ ->
              // A bare identifier with no arrow after it
              let result = parseWsd "participant A\nA\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"
              let err = result.Errors.[0]
              Expect.isTrue (err.CorrectiveExample.Contains("->")) "corrective example mentions ->"
              Expect.isTrue (err.CorrectiveExample.Contains("-->")) "corrective example mentions -->"

          // === 8. Implicit participant warnings - emitted once per name ===
          testCase "implicit participant warning emitted once per name"
          <| fun _ ->
              let result = parseWsd "Foo->Bar: m1\nFoo->Bar: m2\nFoo->Bar: m3\n"

              // Foo and Bar each get one warning, not three
              let fooWarnings =
                  result.Warnings |> List.filter (fun w -> w.Description.Contains("'Foo'"))

              let barWarnings =
                  result.Warnings |> List.filter (fun w -> w.Description.Contains("'Bar'"))

              Expect.equal fooWarnings.Length 1 "Foo warned once"
              Expect.equal barWarnings.Length 1 "Bar warned once"

          // === 9. Implicit participant warning has suggestion ===
          testCase "implicit participant warning has corrective suggestion"
          <| fun _ ->
              let result = parseWsd "X->Y: hello\n"

              let xWarning = result.Warnings |> List.find (fun w -> w.Description.Contains("'X'"))

              Expect.isSome xWarning.Suggestion "suggestion present"
              Expect.isTrue (xWarning.Suggestion.Value.Contains("participant X")) "suggests declaring participant"

          // === 10. Unsupported construct: activate ===
          testCase "unsupported construct 'activate' produces warning and skips"
          <| fun _ ->
              let result = parseWsd "participant A\nparticipant B\nactivate A\nA->B: hello\n"

              Expect.isEmpty result.Errors "no errors"
              let edges = transitions result
              Expect.hasLength edges 1 "message after activate still parsed"

              let activateWarning =
                  result.Warnings |> List.tryFind (fun w -> w.Description.Contains("activate"))

              Expect.isSome activateWarning "has activate warning"

          // === 11. Unsupported construct: deactivate ===
          testCase "unsupported construct 'deactivate' produces warning and skips"
          <| fun _ ->
              let result =
                  parseWsd "participant A\nparticipant B\nA->B: hello\ndeactivate A\nA->B: bye\n"

              Expect.isEmpty result.Errors "no errors"
              let edges = transitions result
              Expect.hasLength edges 2 "both messages parsed"

              let deactivateWarning =
                  result.Warnings |> List.tryFind (fun w -> w.Description.Contains("deactivate"))

              Expect.isSome deactivateWarning "has deactivate warning"

          // === 12. Unsupported construct: destroy ===
          testCase "unsupported construct 'destroy' produces warning"
          <| fun _ ->
              let result = parseWsd "participant A\ndestroy A\n"
              Expect.isEmpty result.Errors "no errors"

              let warning =
                  result.Warnings |> List.tryFind (fun w -> w.Description.Contains("destroy"))

              Expect.isSome warning "has destroy warning"

          // === 13. Unsupported construct: box ===
          testCase "unsupported construct 'box' produces warning"
          <| fun _ ->
              let result = parseWsd "participant A\nbox MyGroup\n"
              Expect.isEmpty result.Errors "no errors"

              let warning =
                  result.Warnings |> List.tryFind (fun w -> w.Description.Contains("box"))

              Expect.isSome warning "has box warning"

          // === 14. Partial AST with errors: valid elements before/after errors preserved ===
          testCase "partial AST: valid elements before and after error preserved"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
A->B: before
@@invalid
B->A: after
"""

              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"
              let edges = transitions result
              Expect.hasLength edges 2 "both messages preserved"
              Expect.equal edges.[0].Event (Some "before") "before message"
              Expect.equal edges.[1].Event (Some "after") "after message"

          // === 15. Guard integration in notes ===
          testCase "guard integration: note with guard annotation is parsed"
          <| fun _ ->
              let result =
                  parseWsd "participant Client\nnote over Client: [guard: role=admin] Must be admin\n"

              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.hasLength ns 1 "one note"
              Expect.isSome (noteGuard ns.[0]) "guard parsed"
              Expect.equal (noteGuard ns.[0]).Value [ ("role", "admin") ] "guard pairs"
              Expect.equal ns.[0].Content "Must be admin" "remaining content preserved"

          // === 16. Guard integration: note with guard only (no remaining text) ===
          testCase "guard integration: note with guard only"
          <| fun _ ->
              let result =
                  parseWsd "participant Client\nnote over Client: [guard: state=Active]\n"

              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.isSome (noteGuard ns.[0]) "guard parsed"
              Expect.equal ns.[0].Content "" "no remaining content"

          // === 17. Guard errors propagated ===
          testCase "guard errors propagated to parse result"
          <| fun _ ->
              let result = parseWsd "participant Client\nnote over Client: [guard: malformed\n"

              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"

              let guardError =
                  result.Errors
                  |> List.tryFind (fun e -> e.Description.Contains("Unclosed guard"))

              Expect.isSome guardError "has guard error"

          // === 18. Guard warnings propagated ===
          testCase "guard warnings propagated to parse result"
          <| fun _ ->
              let result = parseWsd "participant Client\nnote over Client: [guard: ]\n"

              let guardWarning =
                  result.Warnings |> List.tryFind (fun w -> w.Description.Contains("Empty guard"))

              Expect.isSome guardWarning "has empty guard warning"

          // === 19. US4: unrecognized arrow ===
          testCase "US4-S1: unrecognized arrow produces structured error"
          <| fun _ ->
              let result = parseWsd "participant A\nA\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"
              let err = result.Errors.[0]
              Expect.equal err.Description "Unexpected identifier" "description"
              Expect.isNonEmpty err.Expected "expected non-empty"
              Expect.isNonEmpty err.Found "found non-empty"

          // === 20. US4: undeclared participant ===
          testCase "US4-S2: undeclared participant produces warning"
          <| fun _ ->
              let result = parseWsd "Unknown->Other: hello\n"

              let unknownWarning =
                  result.Warnings |> List.tryFind (fun w -> w.Description.Contains("'Unknown'"))

              Expect.isSome unknownWarning "has undeclared participant warning"
              Expect.isSome unknownWarning.Value.Suggestion "has suggestion"

          // === 21. US4: empty input ===
          testCase "US4-S3: empty input produces no errors"
          <| fun _ ->
              let result = parseWsd ""
              Expect.isEmpty result.Errors "no errors"
              Expect.isEmpty result.Warnings "no warnings"
              Expect.isEmpty result.Document.Elements "no elements"

          // === 22. US4: multiple errors in one input ===
          testCase "US4-S4: multiple errors collected from one input"
          <| fun _ ->
              let result = parseWsd "participant\nnote\nClient->\n"

              Expect.isGreaterThanOrEqual result.Errors.Length 3 "at least three errors"

          // === 23. Note with no guard text passes through ===
          testCase "note with no guard text: content unchanged, guard is None"
          <| fun _ ->
              let result = parseWsd "participant Server\nnote over Server: Validate credentials\n"

              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.equal ns.[0].Content "Validate credentials" "content unchanged"
              Expect.isNone (noteGuard ns.[0]) "no guard"

          // === 24. Multiple unsupported constructs ===
          testCase "multiple unsupported constructs all produce warnings"
          <| fun _ ->
              let result = parseWsd "participant A\nactivate A\ndeactivate A\ndestroy A\n"

              Expect.isEmpty result.Errors "no errors"

              let unsupportedWarnings =
                  result.Warnings
                  |> List.filter (fun w -> w.Description.Contains("Unsupported construct"))

              Expect.isGreaterThanOrEqual unsupportedWarnings.Length 3 "at least 3 unsupported warnings"

          // === 25. Error recovery after missing participant name ===
          testCase "error recovery: parse continues after missing participant name"
          <| fun _ ->
              let result = parseWsd "participant\nparticipant B\nB->B: self\n"

              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"
              let decls = stateDecls result
              Expect.isGreaterThanOrEqual decls.Length 1 "at least one participant parsed"
              let edges = transitions result
              Expect.hasLength edges 1 "message still parsed"

          // === 26. Error recovery inside group: error then valid line ===
          testCase "error recovery inside group body"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt condition
  @@invalid
  A->B: valid
end
"""

              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error from invalid line"

              let gs =
                  result.Document.Elements
                  |> List.choose (function
                      | GroupElement g -> Some g
                      | _ -> None)

              Expect.hasLength gs 1 "group still parsed"

              let branchEdges =
                  gs.[0].Branches.[0].Elements
                  |> List.choose (function
                      | TransitionElement t -> Some t
                      | _ -> None)

              Expect.hasLength branchEdges 1 "message in branch still parsed"

          // === 27. Error position is accurate ===
          testCase "error position line and column are accurate"
          <| fun _ ->
              let result = parseWsd "participant A\n@@invalid\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"
              let err = result.Errors.[0]
              // The @@ chars are on line 2
              Expect.equal err.Position.Value.Line 2 "error on line 2"

          // === 28. Corrective example for missing note position ===
          testCase "corrective example for missing note position"
          <| fun _ ->
              let result = parseWsd "note Client: text\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"
              let err = result.Errors.[0]
              Expect.isTrue (err.CorrectiveExample.Contains("note over")) "corrective example mentions 'note over'"

          // === 29. Unsupported construct 'skinparam' ===
          testCase "unsupported construct 'skinparam' produces warning"
          <| fun _ ->
              let result = parseWsd "skinparam monochrome true\n"
              Expect.isEmpty result.Errors "no errors"

              let warning =
                  result.Warnings |> List.tryFind (fun w -> w.Description.Contains("skinparam"))

              Expect.isSome warning "has skinparam warning"

          // === 30. Unsupported construct 'theme' ===
          testCase "unsupported construct 'theme' produces warning"
          <| fun _ ->
              let result = parseWsd "theme cerulean\n"
              Expect.isEmpty result.Errors "no errors"

              let warning =
                  result.Warnings |> List.tryFind (fun w -> w.Description.Contains("theme"))

              Expect.isSome warning "has theme warning" ]
