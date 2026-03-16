module Frank.Statecharts.Tests.Wsd.SerializerTests

open Expecto
open Frank.Statecharts.Wsd.Types
open Frank.Statecharts.Wsd.Serializer
open Frank.Statecharts.Wsd.Parser

/// Synthetic position for generated AST nodes.
let private pos = { Line = 0; Column = 0 }

/// Helper to create a Participant.
let private mkParticipant name alias =
    { Name = name
      Alias = alias
      Explicit = true
      Position = pos }

/// Helper to create a Message.
let private mkMessage sender receiver style dir label parameters =
    { Sender = sender
      Receiver = receiver
      ArrowStyle = style
      Direction = dir
      Label = label
      Parameters = parameters
      Position = pos }

/// Helper to create a Note.
let private mkNote position target content guard =
    { NotePosition = position
      Target = target
      Content = content
      Guard = guard
      Position = pos }

/// Helper to create a GuardAnnotation.
let private mkGuard pairs =
    { Pairs = pairs
      Position = pos }

/// Helper to create a simple Diagram.
let private mkDiagram title autoNumber elements =
    { Title = title
      AutoNumber = autoNumber
      Participants = []
      Elements = elements }

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
                    let d = mkDiagram None false [ ParticipantDecl(mkParticipant "A" None) ]
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
                            [ ParticipantDecl(mkParticipant "Client" None)
                              ParticipantDecl(mkParticipant "Server" None) ]

                    let result = serialize d
                    Expect.stringContains result "participant Client\n" "Client declared"
                    Expect.stringContains result "participant Server\n" "Server declared"
                }

                test "participant ordering preserved" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ ParticipantDecl(mkParticipant "First" None)
                              ParticipantDecl(mkParticipant "Second" None)
                              ParticipantDecl(mkParticipant "Third" None) ]

                    let result = serialize d
                    let firstIdx = result.IndexOf("participant First")
                    let secondIdx = result.IndexOf("participant Second")
                    let thirdIdx = result.IndexOf("participant Third")
                    Expect.isTrue (firstIdx < secondIdx) "First before Second"
                    Expect.isTrue (secondIdx < thirdIdx) "Second before Third"
                }

                test "participant with alias" {
                    let d =
                        mkDiagram None false [ ParticipantDecl(mkParticipant "API" (Some "RestAPI")) ]

                    let result = serialize d
                    Expect.stringContains result "participant API as RestAPI\n" "alias present"
                }

                test "quoted participant name" {
                    let d =
                        mkDiagram None false [ ParticipantDecl(mkParticipant "my state" None) ]

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
                            [ MessageElement(mkMessage "A" "B" Solid Forward "label" []) ]

                    let result = serialize d
                    Expect.stringContains result "A->B: label\n" "solid forward"
                }

                test "dashed forward arrow" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ MessageElement(mkMessage "A" "B" Dashed Forward "label" []) ]

                    let result = serialize d
                    Expect.stringContains result "A-->B: label\n" "dashed forward"
                }

                test "solid deactivating arrow" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ MessageElement(mkMessage "A" "B" Solid Deactivating "label" []) ]

                    let result = serialize d
                    Expect.stringContains result "A->-B: label\n" "solid deactivating"
                }

                test "dashed deactivating arrow" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ MessageElement(mkMessage "A" "B" Dashed Deactivating "label" []) ]

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
                            [ MessageElement(mkMessage "A" "B" Solid Forward "method" [ "p1"; "p2" ]) ]

                    let result = serialize d
                    Expect.stringContains result "A->B: method(p1, p2)\n" "parameters present"
                }

                test "message with empty label" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ MessageElement(mkMessage "A" "B" Solid Forward "" []) ]

                    let result = serialize d
                    Expect.stringContains result "A->B\n" "no colon for empty label"
                    Expect.isFalse (result.Contains(":")) "no colon"
                }

                test "self-message" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ MessageElement(mkMessage "X" "X" Solid Forward "label" []) ]

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
                            [ NoteElement(mkNote NotePosition.Over "X" "text" None) ]

                    let result = serialize d
                    Expect.stringContains result "note over X: text\n" "note over"
                }

                test "note left of" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ NoteElement(mkNote NotePosition.LeftOf "X" "text" None) ]

                    let result = serialize d
                    Expect.stringContains result "note left of X: text\n" "note left of"
                }

                test "note right of" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ NoteElement(mkNote NotePosition.RightOf "X" "text" None) ]

                    let result = serialize d
                    Expect.stringContains result "note right of X: text\n" "note right of"
                }

                test "note with guard" {
                    let guard = mkGuard [ ("role", "admin") ]

                    let d =
                        mkDiagram
                            None
                            false
                            [ NoteElement(mkNote NotePosition.Over "X" "" (Some guard)) ]

                    let result = serialize d
                    Expect.stringContains result "note over X: [guard: role=admin]\n" "guard annotation"
                }

                test "note with multiple guards" {
                    let guard = mkGuard [ ("role", "admin"); ("auth", "bearer") ]

                    let d =
                        mkDiagram
                            None
                            false
                            [ NoteElement(mkNote NotePosition.Over "X" "" (Some guard)) ]

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
                            [ NoteElement(mkNote NotePosition.Over "X" "extra text" (Some guard)) ]

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
                            [ AutoNumberDirective pos ]

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
                                          Elements = [ MessageElement(mkMessage "A" "B" Solid Forward "msg1" []) ] }
                                        { Condition = Some "condition2"
                                          Elements = [ MessageElement(mkMessage "B" "A" Solid Forward "msg2" []) ] } ]
                                    Position = pos } ]

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
                            [ ParticipantDecl(mkParticipant "X" None) ]

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
                            [ ParticipantDecl(mkParticipant "A" None)
                              ParticipantDecl(mkParticipant "B" None)
                              MessageElement(mkMessage "A" "B" Solid Forward "msg" []) ]

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
                            [ ParticipantDecl(mkParticipant "Client" None)
                              ParticipantDecl(mkParticipant "Server" None)
                              MessageElement(mkMessage "Client" "Server" Solid Forward "hello" [])
                              MessageElement(mkMessage "Server" "Client" Dashed Forward "world" []) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)
                    Expect.equal result.Diagram.Title (Some "Roundtrip Test") "title preserved"

                    let msgs =
                        result.Diagram.Elements
                        |> List.choose (function
                            | MessageElement m -> Some m
                            | _ -> None)

                    Expect.equal msgs.Length 2 "two messages"
                    Expect.equal msgs.[0].Sender "Client" "first sender"
                    Expect.equal msgs.[0].Receiver "Server" "first receiver"
                    Expect.equal msgs.[1].Sender "Server" "second sender"
                    Expect.equal msgs.[1].Receiver "Client" "second receiver"
                }

                test "roundtrip: diagram with guard parses back" {
                    let guard = mkGuard [ ("role", "admin") ]

                    let d =
                        mkDiagram
                            (Some "Guard Test")
                            false
                            [ ParticipantDecl(mkParticipant "Client" None)
                              ParticipantDecl(mkParticipant "Server" None)
                              NoteElement(mkNote NotePosition.Over "Client" "" (Some guard))
                              MessageElement(mkMessage "Client" "Server" Solid Forward "action" []) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)

                    let notes =
                        result.Diagram.Elements
                        |> List.choose (function
                            | NoteElement n -> Some n
                            | _ -> None)

                    Expect.equal notes.Length 1 "one note"
                    Expect.isSome notes.[0].Guard "has guard"
                    Expect.equal notes.[0].Guard.Value.Pairs [ ("role", "admin") ] "guard preserved"
                }

                test "roundtrip: all arrow styles parse back" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ ParticipantDecl(mkParticipant "A" None)
                              ParticipantDecl(mkParticipant "B" None)
                              MessageElement(mkMessage "A" "B" Solid Forward "solid" [])
                              MessageElement(mkMessage "A" "B" Dashed Forward "dashed" [])
                              MessageElement(mkMessage "A" "B" Solid Deactivating "solidDeact" [])
                              MessageElement(mkMessage "A" "B" Dashed Deactivating "dashedDeact" []) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)

                    let msgs =
                        result.Diagram.Elements
                        |> List.choose (function
                            | MessageElement m -> Some m
                            | _ -> None)

                    Expect.equal msgs.Length 4 "four messages"
                    Expect.equal msgs.[0].ArrowStyle Solid "solid"
                    Expect.equal msgs.[0].Direction Forward "forward"
                    Expect.equal msgs.[1].ArrowStyle Dashed "dashed"
                    Expect.equal msgs.[1].Direction Forward "forward"
                    Expect.equal msgs.[2].ArrowStyle Solid "solid deact"
                    Expect.equal msgs.[2].Direction Deactivating "deactivating"
                    Expect.equal msgs.[3].ArrowStyle Dashed "dashed deact"
                    Expect.equal msgs.[3].Direction Deactivating "deactivating"
                }

                test "roundtrip: message with parameters" {
                    let d =
                        mkDiagram
                            None
                            false
                            [ ParticipantDecl(mkParticipant "A" None)
                              ParticipantDecl(mkParticipant "B" None)
                              MessageElement(mkMessage "A" "B" Solid Forward "getData" [ "x"; "y"; "z" ]) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)

                    let msgs =
                        result.Diagram.Elements
                        |> List.choose (function
                            | MessageElement m -> Some m
                            | _ -> None)

                    Expect.equal msgs.[0].Label "getData" "label preserved"
                    Expect.equal msgs.[0].Parameters [ "x"; "y"; "z" ] "parameters preserved"
                }

                test "roundtrip: autonumber" {
                    let d =
                        mkDiagram
                            (Some "Auto")
                            true
                            [ AutoNumberDirective pos
                              ParticipantDecl(mkParticipant "A" None)
                              ParticipantDecl(mkParticipant "B" None)
                              MessageElement(mkMessage "A" "B" Solid Forward "msg" []) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)
                    Expect.isTrue result.Diagram.AutoNumber "autonumber preserved"
                }

                test "roundtrip: multiple guard pairs" {
                    let guard = mkGuard [ ("role", "admin"); ("auth", "bearer") ]

                    let d =
                        mkDiagram
                            None
                            false
                            [ ParticipantDecl(mkParticipant "A" None)
                              NoteElement(mkNote NotePosition.Over "A" "" (Some guard)) ]

                    let wsd = serialize d
                    let result = parseWsd wsd
                    Expect.isEmpty result.Errors (sprintf "no parse errors, output was:\n%s" wsd)

                    let notes =
                        result.Diagram.Elements
                        |> List.choose (function
                            | NoteElement n -> Some n
                            | _ -> None)

                    Expect.isSome notes.[0].Guard "has guard"

                    Expect.equal
                        notes.[0].Guard.Value.Pairs
                        [ ("role", "admin"); ("auth", "bearer") ]
                        "guard pairs preserved"
                } ] ]
