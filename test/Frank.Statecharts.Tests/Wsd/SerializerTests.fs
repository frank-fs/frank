module Frank.Statecharts.Tests.Wsd.SerializerTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Wsd.Serializer
open Frank.Statecharts.Wsd.Parser

/// Synthetic position for generated AST nodes.
let private pos = { Line = 0; Column = 0 }

/// Helper to create a StateNode (was Participant).
let private mkParticipant name alias =
    { Identifier = Some name
      Label = alias
      Kind = Regular
      Children = []
      Activities = None
      Position = Some pos
      Annotations = [] }

/// Helper to create a TransitionEdge (was Message).
let private mkMessage sender receiver style dir label parameters =
    { Source = sender
      Target = Some receiver
      Event = Some label
      Guard = None
      GuardHref = None
      Action = None
      Parameters = parameters
      Position = Some pos
      Annotations = [ WsdAnnotation(WsdTransitionStyle { ArrowStyle = style; Direction = dir }) ] }

/// Helper to create a NoteContent (was Note).
let private mkNote position target content guardPairs =
    { Target = target
      Content = content
      Position = Some pos
      Annotations =
        [ WsdAnnotation(WsdNotePosition position) ]
        @ (match guardPairs with
           | Some pairs -> [ WsdAnnotation(WsdGuardData pairs) ]
           | None -> []) }

/// Helper to create guard pairs (was GuardAnnotation).
let private mkGuard pairs = pairs

/// Helper to create a simple StatechartDocument (was Diagram).
let private mkDiagram title autoNumber elements =
    let directiveElements =
        if autoNumber then [ DirectiveElement(AutoNumberDirective(Some pos)) ] else []

    { Title = title
      InitialStateId = None
      Elements = directiveElements @ elements
      DataEntries = []
      Annotations = [] }

/// Extract ArrowStyle from a TransitionEdge's annotations.
let private extractArrowStyle (t: TransitionEdge) =
    t.Annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdTransitionStyle s) -> Some s.ArrowStyle
        | _ -> None)
    |> Option.defaultValue Solid

/// Extract Direction from a TransitionEdge's annotations.
let private extractDirection (t: TransitionEdge) =
    t.Annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdTransitionStyle s) -> Some s.Direction
        | _ -> None)
    |> Option.defaultValue Forward

/// Extract WsdGuardData pairs from a NoteContent's annotations.
let private extractGuardPairs (n: NoteContent) =
    n.Annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdGuardData pairs) -> Some pairs
        | _ -> None)

[<Tests>]
let serializerTests =
    testList
        "Serializer"
        [
          // === needsQuoting ===
          testList
              "needsQuoting"
              [ test "simple identifier does not need quoting" {
                    Expect.isFalse (needsQuoting "Locked") "no quoting needed"
                }

                test "name with space needs quoting" {
                    Expect.isTrue (needsQuoting "my state") "space needs quoting"
                }

                test "name with hyphen does not need quoting" {
                    Expect.isFalse (needsQuoting "state-1") "hyphen is OK"
                }

                test "name with underscore does not need quoting" {
                    Expect.isFalse (needsQuoting "state_1") "underscore is OK"
                }

                test "name with dot needs quoting" {
                    Expect.isTrue (needsQuoting "state.name") "dot needs quoting"
                }

                test "empty string does not need quoting" {
                    Expect.isFalse (needsQuoting "") "empty is false"
                }

                test "name with colon needs quoting" {
                    Expect.isTrue (needsQuoting "key:value") "colon needs quoting"
                } ]

          // === quoteName ===
          testList
              "quoteName"
              [ test "simple name unchanged" {
                    Expect.equal (quoteName "Locked") "Locked" "no change"
                }

                test "name with space gets quoted" {
                    Expect.equal (quoteName "my state") "\"my state\"" "quoted"
                }

                test "name with internal quotes gets escaped" {
                    Expect.equal (quoteName "say \"hello\"") "\"say \\\"hello\\\"\"" "escaped quotes"
                } ]

          // === title emission ===
          testList
              "title"
              [ test "title emission" {
                    let d = mkDiagram (Some "Test") false []
                    let result = serialize d
                    Expect.stringStarts result "title Test\n" "starts with title"
                }

                test "no title" {
                    let d = mkDiagram None false [ StateDecl(mkParticipant "A" None) ]
                    let result = serialize d
                    Expect.isFalse (result.Contains("title")) "no title in output"
                } ]

          // === participant declarations ===
          testList
              "participants"
              [ test "participant declarations" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ StateDecl(mkParticipant "Client" None)
                              StateDecl(mkParticipant "Server" None) ]

                    let result = serialize d
                    Expect.stringContains result "participant Client\n" "Client declared"
                    Expect.stringContains result "participant Server\n" "Server declared"
                }

                test "participant ordering preserved" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ StateDecl(mkParticipant "First" None)
                              StateDecl(mkParticipant "Second" None)
                              StateDecl(mkParticipant "Third" None) ]

                    let result = serialize d
                    let firstIdx = result.IndexOf("participant First")
                    let secondIdx = result.IndexOf("participant Second")
                    let thirdIdx = result.IndexOf("participant Third")
                    Expect.isTrue (firstIdx < secondIdx) "First before Second"
                    Expect.isTrue (secondIdx < thirdIdx) "Second before Third"
                }

                test "participant with alias" {
                    let d =
                        mkDiagram None false [ StateDecl(mkParticipant "API" (Some "RestAPI")) ]

                    let result = serialize d
                    Expect.stringContains result "participant API as RestAPI\n" "alias present"
                }

                test "quoted participant name" {
                    let d =
                        mkDiagram None false [ StateDecl(mkParticipant "my state" None) ]

                    let result = serialize d
                    Expect.stringContains result "participant \"my state\"\n" "quoted name"
                } ]

          // === arrow styles ===
          testList
              "arrows"
              [ test "solid forward arrow" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ TransitionElement(mkMessage "A" "B" Solid Forward "label" []) ]

                    let result = serialize d
                    Expect.stringContains result "A->B: label\n" "solid forward"
                }

                test "dashed forward arrow" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ TransitionElement(mkMessage "A" "B" Dashed Forward "label" []) ]

                    let result = serialize d
                    Expect.stringContains result "A-->B: label\n" "dashed forward"
                }

                test "solid deactivating arrow" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ TransitionElement(mkMessage "A" "B" Solid Deactivating "label" []) ]

                    let result = serialize d
                    Expect.stringContains result "A->-B: label\n" "solid deactivating"
                }

                test "dashed deactivating arrow" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ TransitionElement(mkMessage "A" "B" Dashed Deactivating "label" []) ]

                    let result = serialize d
                    Expect.stringContains result "A-->-B: label\n" "dashed deactivating"
                } ]

          // === message content ===
          testList
              "messages"
              [ test "message with parameters" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ TransitionElement(mkMessage "A" "B" Solid Forward "method" [ "p1"; "p2" ]) ]

                    let result = serialize d
                    Expect.stringContains result "A->B: method(p1, p2)\n" "parameters present"
                }

                test "message with empty label" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ TransitionElement(mkMessage "A" "B" Solid Forward "" []) ]

                    let result = serialize d
                    Expect.stringContains result "A->B\n" "no colon for empty label"
                    Expect.isFalse (result.Contains(":")) "no colon"
                }

                test "self-message" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ TransitionElement(mkMessage "X" "X" Solid Forward "label" []) ]

                    let result = serialize d
                    Expect.stringContains result "X->X: label\n" "self-message"
                } ]

          // === notes ===
          testList
              "notes"
              [ test "note over" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ NoteElement(mkNote Over "X" "text" None) ]

                    let result = serialize d
                    Expect.stringContains result "note over X: text\n" "note over"
                }

                test "note left of" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ NoteElement(mkNote LeftOf "X" "text" None) ]

                    let result = serialize d
                    Expect.stringContains result "note left of X: text\n" "note left of"
                }

                test "note right of" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ NoteElement(mkNote RightOf "X" "text" None) ]

                    let result = serialize d
                    Expect.stringContains result "note right of X: text\n" "note right of"
                }

                test "note with guard" {
                    let guard = mkGuard [ ("role", "admin") ]

                    let d =
                        mkDiagram
                            None
                            false
                            [ NoteElement(mkNote Over "X" "" (Some guard)) ]

                    let result = serialize d
                    Expect.stringContains result "note over X: [guard: role=admin]\n" "guard annotation"
                }

                test "note with multiple guards" {
                    let guard = mkGuard [ ("role", "admin"); ("auth", "bearer") ]

                    let d =
                        mkDiagram
                            None
                            false
                            [ NoteElement(mkNote Over "X" "" (Some guard)) ]

                    let result = serialize d

                    Expect.stringContains
                        result
                        "note over X: [guard: role=admin, auth=bearer]\n"
                        "multiple guard pairs"
                }

                test "guard plus content" {
                    let guard = mkGuard [ ("role", "admin") ]

                    let d =
                        mkDiagram
                            None
                            false
                            [ NoteElement(mkNote Over "X" "extra text" (Some guard)) ]

                    let result = serialize d

                    Expect.stringContains
                        result
                        "note over X: [guard: role=admin] extra text\n"
                        "guard plus content"
                } ]

          // === directives ===
          testList
              "directives"
              [ test "autonumber directive" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ DirectiveElement(AutoNumberDirective(Some pos)) ]

                    let result = serialize d
                    Expect.stringContains result "autonumber\n" "autonumber"
                } ]

          // === groups ===
          testList
              "groups"
              [ test "simple alt group" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ GroupElement
                                  { Kind = GroupKind.Alt
                                    Branches =
                                      [ { Condition = Some "condition1"
                                          Elements = [ TransitionElement(mkMessage "A" "B" Solid Forward "msg1" []) ] }
                                        { Condition = Some "condition2"
                                          Elements = [ TransitionElement(mkMessage "B" "A" Solid Forward "msg2" []) ] } ]
                                    Position = Some pos } ]

                    let result = serialize d
                    Expect.stringContains result "alt condition1\n" "alt with condition"
                    Expect.stringContains result "A->B: msg1\n" "first branch message"
                    Expect.stringContains result "else condition2\n" "else with condition"
                    Expect.stringContains result "B->A: msg2\n" "second branch message"
                    Expect.stringContains result "end\n" "end"
                } ]

          // === edge cases ===
          testList
              "edge cases"
              [ test "empty diagram" {
                    let d = mkDiagram None false []
                    let result = serialize d
                    Expect.equal result "" "empty string"
                }

                test "single participant no messages" {
                    let d =
                        mkDiagram
                            (Some "Test")
                            false
                            [ StateDecl(mkParticipant "X" None) ]

                    let result = serialize d
                    Expect.stringContains result "title Test\n" "has title"
                    Expect.stringContains result "participant X\n" "has participant"
                }

                test "diagram with only title" {
                    let d = mkDiagram (Some "Only Title") false []
                    let result = serialize d
                    Expect.equal result "title Only Title\n" "just title"
                }

                test "Unix line endings only" {
                    let d =
                        mkDiagram
                            (Some "Test")
                            false
                            [ StateDecl(mkParticipant "A" None)
                              StateDecl(mkParticipant "B" None)
                              TransitionElement(mkMessage "A" "B" Solid Forward "msg" []) ]

                    let result = serialize d
                    Expect.isFalse (result.Contains("\r\n")) "no Windows line endings"
                    Expect.isTrue (result.Contains("\n")) "has Unix line endings"
                } ]

          // === roundtrip validation ===
          testList
              "roundtrip"
              [ test "roundtrip: simple diagram parses back without errors" {
                    let d =
                        mkDiagram
                            (Some "Roundtrip Test")
                            false
                            [ StateDecl(mkParticipant "Client" None)
                              StateDecl(mkParticipant "Server" None)
                              TransitionElement(mkMessage "Client" "Server" Solid Forward "hello" [])
                              TransitionElement(mkMessage "Server" "Client" Dashed Forward "world" []) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)
                    Expect.equal result.Document.Title (Some "Roundtrip Test") "title preserved"

                    let msgs =
                        result.Document.Elements
                        |> List.choose (function
                            | TransitionElement t -> Some t
                            | _ -> None)

                    Expect.equal msgs.Length 2 "two messages"
                    Expect.equal msgs.[0].Source "Client" "first sender"
                    Expect.equal msgs.[0].Target (Some "Server") "first receiver"
                    Expect.equal msgs.[1].Source "Server" "second sender"
                    Expect.equal msgs.[1].Target (Some "Client") "second receiver"
                }

                test "roundtrip: diagram with guard parses back" {
                    let guard = mkGuard [ ("role", "admin") ]

                    let d =
                        mkDiagram
                            (Some "Guard Test")
                            false
                            [ StateDecl(mkParticipant "Client" None)
                              StateDecl(mkParticipant "Server" None)
                              NoteElement(mkNote Over "Client" "" (Some guard))
                              TransitionElement(mkMessage "Client" "Server" Solid Forward "action" []) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)

                    let notes =
                        result.Document.Elements
                        |> List.choose (function
                            | NoteElement n -> Some n
                            | _ -> None)

                    Expect.equal notes.Length 1 "one note"
                    let guardPairs = extractGuardPairs notes.[0]
                    Expect.isSome guardPairs "has guard"
                    Expect.equal guardPairs.Value [ ("role", "admin") ] "guard preserved"
                }

                test "roundtrip: all arrow styles parse back" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ StateDecl(mkParticipant "A" None)
                              StateDecl(mkParticipant "B" None)
                              TransitionElement(mkMessage "A" "B" Solid Forward "solid" [])
                              TransitionElement(mkMessage "A" "B" Dashed Forward "dashed" [])
                              TransitionElement(mkMessage "A" "B" Solid Deactivating "solidDeact" [])
                              TransitionElement(mkMessage "A" "B" Dashed Deactivating "dashedDeact" []) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)

                    let msgs =
                        result.Document.Elements
                        |> List.choose (function
                            | TransitionElement t -> Some t
                            | _ -> None)

                    Expect.equal msgs.Length 4 "four messages"
                    Expect.equal (extractArrowStyle msgs.[0]) Solid "solid"
                    Expect.equal (extractDirection msgs.[0]) Forward "forward"
                    Expect.equal (extractArrowStyle msgs.[1]) Dashed "dashed"
                    Expect.equal (extractDirection msgs.[1]) Forward "forward"
                    Expect.equal (extractArrowStyle msgs.[2]) Solid "solid deact"
                    Expect.equal (extractDirection msgs.[2]) Deactivating "deactivating"
                    Expect.equal (extractArrowStyle msgs.[3]) Dashed "dashed deact"
                    Expect.equal (extractDirection msgs.[3]) Deactivating "deactivating"
                }

                test "roundtrip: message with parameters" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ StateDecl(mkParticipant "A" None)
                              StateDecl(mkParticipant "B" None)
                              TransitionElement(mkMessage "A" "B" Solid Forward "getData" [ "x"; "y"; "z" ]) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)

                    let msgs =
                        result.Document.Elements
                        |> List.choose (function
                            | TransitionElement t -> Some t
                            | _ -> None)

                    Expect.equal msgs.[0].Event (Some "getData") "label preserved"
                    Expect.equal msgs.[0].Parameters [ "x"; "y"; "z" ] "parameters preserved"
                }

                test "roundtrip: autonumber" {
                    let d =
                        mkDiagram
                            (Some "Auto")
                            true
                            [ StateDecl(mkParticipant "A" None)
                              StateDecl(mkParticipant "B" None)
                              TransitionElement(mkMessage "A" "B" Solid Forward "msg" []) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)

                    let hasAutoNumber =
                        result.Document.Elements
                        |> List.exists (function
                            | DirectiveElement(AutoNumberDirective _) -> true
                            | _ -> false)

                    Expect.isTrue hasAutoNumber "autonumber preserved"
                }

                test "roundtrip: multiple guard pairs" {
                    let guard = mkGuard [ ("role", "admin"); ("auth", "bearer") ]

                    let d =
                        mkDiagram
                            None
                            false
                            [ StateDecl(mkParticipant "A" None)
                              NoteElement(mkNote Over "A" "" (Some guard)) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)

                    let notes =
                        result.Document.Elements
                        |> List.choose (function
                            | NoteElement n -> Some n
                            | _ -> None)

                    let guardPairs = extractGuardPairs notes.[0]
                    Expect.isSome guardPairs "has guard"

                    Expect.equal
                        guardPairs.Value
                        [ ("role", "admin"); ("auth", "bearer") ]
                        "guard pairs preserved"
                } ] ]
