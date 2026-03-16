module Frank.Statecharts.Tests.Wsd.GroupingTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Wsd.Parser

let private groups (result: ParseResult) =
    result.Document.Elements
    |> List.choose (function
        | GroupElement g -> Some g
        | _ -> None)

let private transitions (result: ParseResult) =
    result.Document.Elements
    |> List.choose (function
        | TransitionElement t -> Some t
        | _ -> None)

/// Extract groups from a branch's elements
let private branchGroups (branch: GroupBranch) =
    branch.Elements
    |> List.choose (function
        | GroupElement g -> Some g
        | _ -> None)

/// Extract transitions from a branch's elements
let private branchTransitions (branch: GroupBranch) =
    branch.Elements
    |> List.choose (function
        | TransitionElement t -> Some t
        | _ -> None)

/// Extract notes from a branch's elements
let private branchNotes (branch: GroupBranch) =
    branch.Elements
    |> List.choose (function
        | NoteElement n -> Some n
        | _ -> None)

[<Tests>]
let groupingTests =
    testList
        "Grouping"
        [
          // === 1. Each of the 7 block kinds with simple body ===
          testCase "alt block with simple body"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt success
  A->B: request
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.hasLength gs 1 "one group"
              Expect.equal gs.[0].Kind GroupKind.Alt "alt"
              Expect.hasLength gs.[0].Branches 1 "one branch"
              let edges = branchTransitions gs.[0].Branches.[0]
              Expect.hasLength edges 1 "one message in branch"

          testCase "opt block with simple body"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
opt optional path
  A->B: maybe
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.equal gs.[0].Kind GroupKind.Opt "opt"
              Expect.equal gs.[0].Branches.[0].Condition (Some "optional path") "condition"

          testCase "loop block with simple body"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
loop retry 3 times
  A->B: attempt
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.equal gs.[0].Kind GroupKind.Loop "loop"
              Expect.equal gs.[0].Branches.[0].Condition (Some "retry 3 times") "condition"

          testCase "par block with simple body"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
par parallel
  A->B: first
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.equal gs.[0].Kind GroupKind.Par "par"

          testCase "break block with simple body"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
break error occurred
  A->B: abort
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.equal gs.[0].Kind GroupKind.Break "break"
              Expect.equal gs.[0].Branches.[0].Condition (Some "error occurred") "condition"

          testCase "critical block with simple body"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
critical must succeed
  A->B: important
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.equal gs.[0].Kind GroupKind.Critical "critical"

          testCase "ref block with simple body"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
ref see other diagram
  A->B: external
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.equal gs.[0].Kind GroupKind.Ref "ref"
              Expect.equal gs.[0].Branches.[0].Condition (Some "see other diagram") "condition"

          // === 8. Condition text on blocks ===
          testCase "condition text preserved on group"
          <| fun _ ->
              let result = parseWsd "alt user is authenticated\nend\n"
              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.equal gs.[0].Branches.[0].Condition (Some "user is authenticated") "condition text"

          // === 9. par with no condition ===
          testCase "par with no condition"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
par
  A->B: first
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.equal gs.[0].Kind GroupKind.Par "par"
              Expect.isNone gs.[0].Branches.[0].Condition "no condition"

          // === 10. alt with 2 branches (initial + else) ===
          testCase "alt with two branches"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt success
  A->B: ok
else failure
  A->B: error
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.hasLength gs.[0].Branches 2 "two branches"
              Expect.equal gs.[0].Branches.[0].Condition (Some "success") "first condition"
              Expect.equal gs.[0].Branches.[1].Condition (Some "failure") "second condition"
              let edges0 = branchTransitions gs.[0].Branches.[0]
              let edges1 = branchTransitions gs.[0].Branches.[1]
              Expect.hasLength edges0 1 "one message in first branch"
              Expect.hasLength edges1 1 "one message in second branch"
              Expect.equal edges0.[0].Event (Some "ok") "first branch label"
              Expect.equal edges1.[0].Event (Some "error") "second branch label"

          // === 11. alt with 3 branches ===
          testCase "alt with three branches"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt case 1
  A->B: first
else case 2
  A->B: second
else case 3
  A->B: third
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.hasLength gs.[0].Branches 3 "three branches"
              Expect.equal gs.[0].Branches.[0].Condition (Some "case 1") "cond 1"
              Expect.equal gs.[0].Branches.[1].Condition (Some "case 2") "cond 2"
              Expect.equal gs.[0].Branches.[2].Condition (Some "case 3") "cond 3"

          // === 12. else with condition text ===
          testCase "else with condition text"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt primary
  A->B: primary
else fallback path
  A->B: fallback
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.equal gs.[0].Branches.[1].Condition (Some "fallback path") "else condition text"

          // === 13. bare else (no condition) ===
          testCase "bare else with no condition"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt success
  A->B: ok
else
  A->B: default
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.hasLength gs.[0].Branches 2 "two branches"
              Expect.isNone gs.[0].Branches.[1].Condition "else has no condition"

          // === 14. par with 3 parallel branches ===
          testCase "par with three parallel branches"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
participant C
par
  A->B: task1
else
  A->C: task2
else
  B->C: task3
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.equal gs.[0].Kind GroupKind.Par "par"
              Expect.hasLength gs.[0].Branches 3 "three branches"

          // === 15. opt with single branch (no else) ===
          testCase "opt with single branch no else"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
opt cache hit
  A->B: cached
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.hasLength gs.[0].Branches 1 "single branch"
              Expect.equal gs.[0].Branches.[0].Condition (Some "cache hit") "condition"

          // === 16. 2-level nesting (alt containing opt) ===
          testCase "two-level nesting: alt containing opt"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt outer
  opt inner
    A->B: nested
  end
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.hasLength gs 1 "one top-level group"
              Expect.equal gs.[0].Kind GroupKind.Alt "outer is alt"
              let innerGroups = branchGroups gs.[0].Branches.[0]
              Expect.hasLength innerGroups 1 "one nested group"
              Expect.equal innerGroups.[0].Kind GroupKind.Opt "inner is opt"

          // === 17. 3-level nesting ===
          testCase "three-level nesting"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt level1
  loop level2
    opt level3
      A->B: deep
    end
  end
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              let level1 = gs.[0]
              Expect.equal level1.Kind GroupKind.Alt "level 1 is alt"
              let level2Groups = branchGroups level1.Branches.[0]
              Expect.hasLength level2Groups 1 "one level-2 group"
              let level2 = level2Groups.[0]
              Expect.equal level2.Kind GroupKind.Loop "level 2 is loop"
              let level3Groups = branchGroups level2.Branches.[0]
              Expect.hasLength level3Groups 1 "one level-3 group"
              Expect.equal level3Groups.[0].Kind GroupKind.Opt "level 3 is opt"

          // === 18. 5-level nesting (SC-004) ===
          testCase "five-level nesting"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt L1
  loop L2
    opt L3
      par L4
        critical L5
          A->B: deepest
        end
      end
    end
  end
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              let l1 = gs.[0]
              Expect.equal l1.Kind GroupKind.Alt "L1"
              let l2 = (branchGroups l1.Branches.[0]).[0]
              Expect.equal l2.Kind GroupKind.Loop "L2"
              let l3 = (branchGroups l2.Branches.[0]).[0]
              Expect.equal l3.Kind GroupKind.Opt "L3"
              let l4 = (branchGroups l3.Branches.[0]).[0]
              Expect.equal l4.Kind GroupKind.Par "L4"
              let l5 = (branchGroups l4.Branches.[0]).[0]
              Expect.equal l5.Kind GroupKind.Critical "L5"
              let deepEdges = branchTransitions l5.Branches.[0]
              Expect.hasLength deepEdges 1 "one message at deepest level"
              Expect.equal deepEdges.[0].Event (Some "deepest") "deepest label"

          // === 19. Nested groups in different branches ===
          testCase "nested groups in different branches"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt main
  opt branch1-inner
    A->B: inner1
  end
else other
  loop branch2-inner
    A->B: inner2
  end
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              let alt = gs.[0]
              Expect.hasLength alt.Branches 2 "two branches"
              let branch1Groups = branchGroups alt.Branches.[0]
              Expect.hasLength branch1Groups 1 "one group in branch 1"
              Expect.equal branch1Groups.[0].Kind GroupKind.Opt "branch 1 has opt"
              let branch2Groups = branchGroups alt.Branches.[1]
              Expect.hasLength branch2Groups 1 "one group in branch 2"
              Expect.equal branch2Groups.[0].Kind GroupKind.Loop "branch 2 has loop"

          // === 20. Messages and notes interleaved with groups ===
          testCase "messages and notes interleaved with groups"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
A->B: before
alt condition
  A->B: inside
  note over A: a note inside
end
A->B: after
"""

              Expect.isEmpty result.Errors "no errors"
              // Top-level elements: message, group, message
              let topElements = result.Document.Elements
              // Filter non-state-decl elements
              let nonDecl =
                  topElements
                  |> List.filter (function
                      | StateDecl _ -> false
                      | _ -> true)

              Expect.hasLength nonDecl 3 "three non-decl elements"

              match nonDecl.[0] with
              | TransitionElement t -> Expect.equal t.Event (Some "before") "before message"
              | _ -> failtest "expected message"

              match nonDecl.[1] with
              | GroupElement g ->
                  Expect.equal g.Kind GroupKind.Alt "alt group"
                  let brEdges = branchTransitions g.Branches.[0]
                  let brNotes = branchNotes g.Branches.[0]
                  Expect.hasLength brEdges 1 "one message in branch"
                  Expect.hasLength brNotes 1 "one note in branch"
              | _ -> failtest "expected group"

              match nonDecl.[2] with
              | TransitionElement t -> Expect.equal t.Event (Some "after") "after message"
              | _ -> failtest "expected message"

          // === 21. Empty branch (no elements between else/end) ===
          testCase "empty branch between else and end"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
alt something
  A->B: action
else nothing
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.hasLength gs.[0].Branches 2 "two branches"
              Expect.isEmpty gs.[0].Branches.[1].Elements "second branch is empty"
              Expect.equal gs.[0].Branches.[1].Condition (Some "nothing") "else condition"

          // === 22. Unclosed block -> error with opening line reference ===
          testCase "unclosed block produces error referencing opening line"
          <| fun _ ->
              let result = parseWsd "alt some condition\n  A->B: msg\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"

              let unclosedError =
                  result.Errors |> List.tryFind (fun e -> e.Description.Contains("Unclosed"))

              Expect.isSome unclosedError "has unclosed error"
              Expect.isTrue (unclosedError.Value.Description.Contains("line 1")) "references opening line"

          // === 23. Empty body (keyword + end immediately) ===
          testCase "empty body: keyword followed immediately by end"
          <| fun _ ->
              let result = parseWsd "opt guard\nend\n"
              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              Expect.hasLength gs 1 "one group"
              Expect.equal gs.[0].Kind GroupKind.Opt "opt"
              Expect.hasLength gs.[0].Branches 1 "one branch"
              Expect.isEmpty gs.[0].Branches.[0].Elements "empty body"
              Expect.equal gs.[0].Branches.[0].Condition (Some "guard") "condition"

          // === 24. Block with only messages inside ===
          testCase "block with only messages inside"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
loop repeat
  A->B: first
  B->A: second
  A->B: third
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              let edges = branchTransitions gs.[0].Branches.[0]
              Expect.hasLength edges 3 "three messages"
              Expect.equal edges.[0].Event (Some "first") "msg 1"
              Expect.equal edges.[1].Event (Some "second") "msg 2"
              Expect.equal edges.[2].Event (Some "third") "msg 3"

          // === 25. Mixed content: messages, notes, groups within branches ===
          testCase "mixed content in branches"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant Client
participant Server
participant DB
alt authenticated
  Client->Server: getData
  note over Server: validate token
  opt cache available
    Server->Client: cached response
  end
  Server->DB: query
  DB->Server: results
  Server->Client: response
else unauthorized
  Server->Client: 401
end
"""

              Expect.isEmpty result.Errors "no errors"
              let gs = groups result
              let alt = gs.[0]
              Expect.equal alt.Kind GroupKind.Alt "alt"
              Expect.hasLength alt.Branches 2 "two branches"

              // First branch: message, note, opt group, 3 messages
              let b0 = alt.Branches.[0]
              Expect.equal b0.Condition (Some "authenticated") "first branch condition"
              let b0edges = branchTransitions b0
              let b0notes = branchNotes b0
              let b0groups = branchGroups b0
              Expect.hasLength b0edges 4 "four messages in first branch"
              Expect.hasLength b0notes 1 "one note in first branch"
              Expect.hasLength b0groups 1 "one nested group in first branch"
              Expect.equal b0groups.[0].Kind GroupKind.Opt "nested opt"

              // Second branch: one message
              let b1 = alt.Branches.[1]
              Expect.equal b1.Condition (Some "unauthorized") "second branch condition"
              let b1edges = branchTransitions b1
              Expect.hasLength b1edges 1 "one message in second branch"
              Expect.equal b1edges.[0].Event (Some "401") "unauthorized response"

          // === 26. Extra 'end' with no open group ===
          testCase "extra end with no open group produces error"
          <| fun _ ->
              let result = parseWsd "participant A\nparticipant B\nA->B: hello\nend\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"

              let endError =
                  result.Errors
                  |> List.tryFind (fun e -> e.Description.Contains("'end' without matching grouping block"))

              Expect.isSome endError "has mismatched end error" ]
