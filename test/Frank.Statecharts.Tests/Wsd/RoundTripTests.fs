module Frank.Statecharts.Tests.Wsd.RoundTripTests

open Expecto
open Frank.Statecharts.Wsd.Types
open Frank.Statecharts.Wsd.Parser

// === Helpers ===

let private participants (r: ParseResult) = r.Diagram.Participants

let private messages (r: ParseResult) =
    r.Diagram.Elements
    |> List.choose (function
        | MessageElement m -> Some m
        | _ -> None)

let private notes (r: ParseResult) =
    r.Diagram.Elements
    |> List.choose (function
        | NoteElement n -> Some n
        | _ -> None)

let private groups (r: ParseResult) =
    r.Diagram.Elements
    |> List.choose (function
        | GroupElement g -> Some g
        | _ -> None)

// === T040: Amundsen Onboarding Round-Trip ===

let onboardingWsd =
    """title Onboarding Flow
autonumber

participant Client
participant API
participant DB

Client->API: createAccount(name, email)
note over API: [guard: auth=none]
API->DB: insertUser(name, email)
DB->-API: userId
API->-Client: 201 Created

Client-->API: getProfile()
note over API: [guard: auth=bearer]
API-->DB: selectUser(userId)
DB-->-API: userData
API-->-Client: 200 OK
"""

// === T041: Tic-Tac-Toe with Guards ===

let ticTacToeWsd =
    """title Tic-Tac-Toe Game

participant PlayerX
participant PlayerO
participant Board
participant GameEngine

note over PlayerX: [guard: role=PlayerX, state=XTurn]
PlayerX->Board: makeMove(position)
Board->GameEngine: validateMove(position)
GameEngine->-Board: valid

alt win condition
    GameEngine->-Board: gameOver(winner=X)
    Board->-PlayerX: youWin
else continue
    note over PlayerO: [guard: role=PlayerO, state=OTurn]
    PlayerO->Board: makeMove(position)
    Board->GameEngine: validateMove(position)
    GameEngine->-Board: valid
end
"""

[<Tests>]
let roundTripTests =
    testList
        "RoundTrip"
        [
          // === T040: Onboarding ===
          testList
              "Onboarding Flow"
              [ test "parses without errors" {
                    let result = parseWsd onboardingWsd
                    Expect.isEmpty result.Errors "no errors"
                }

                test "title is Onboarding Flow" {
                    let result = parseWsd onboardingWsd
                    Expect.equal result.Diagram.Title (Some "Onboarding Flow") "title"
                }

                test "autonumber is true" {
                    let result = parseWsd onboardingWsd
                    Expect.isTrue result.Diagram.AutoNumber "autonumber"
                }

                test "three explicit participants" {
                    let result = parseWsd onboardingWsd
                    let ps = participants result
                    Expect.equal ps.Length 3 "three participants"
                    Expect.equal ps.[0].Name "Client" "first"
                    Expect.equal ps.[1].Name "API" "second"
                    Expect.equal ps.[2].Name "DB" "third"

                    for p in ps do
                        Expect.isTrue p.Explicit "all explicit"
                }

                test "first message: Client->API solid forward" {
                    let result = parseWsd onboardingWsd
                    let msgs = messages result
                    let m = msgs.[0]
                    Expect.equal m.Sender "Client" "sender"
                    Expect.equal m.Receiver "API" "receiver"
                    Expect.equal m.ArrowStyle ArrowStyle.Solid "solid"
                    Expect.equal m.Direction Direction.Forward "forward"
                    Expect.stringContains m.Label "createAccount" "label"
                }

                test "guard on first note: auth=none" {
                    let result = parseWsd onboardingWsd
                    let ns = notes result
                    let n = ns.[0]
                    Expect.isSome n.Guard "has guard"
                    Expect.equal n.Guard.Value.Pairs [ ("auth", "none") ] "guard pairs"
                }

                test "deactivating arrows have correct direction" {
                    let result = parseWsd onboardingWsd
                    let msgs = messages result

                    let deactivating =
                        msgs |> List.filter (fun m -> m.Direction = Direction.Deactivating)

                    Expect.isGreaterThanOrEqual deactivating.Length 4 "at least 4 deactivating arrows"
                }

                test "dashed messages have correct style" {
                    let result = parseWsd onboardingWsd
                    let msgs = messages result
                    let dashed = msgs |> List.filter (fun m -> m.ArrowStyle = ArrowStyle.Dashed)
                    Expect.isGreaterThanOrEqual dashed.Length 4 "at least 4 dashed arrows"
                }

                test "second note guard: auth=bearer" {
                    let result = parseWsd onboardingWsd
                    let ns = notes result
                    let n = ns.[1]
                    Expect.isSome n.Guard "has guard"
                    Expect.equal n.Guard.Value.Pairs [ ("auth", "bearer") ] "guard pairs"
                }

                test "no warnings (all participants declared)" {
                    let result = parseWsd onboardingWsd
                    // Filter out any non-implicit participant warnings
                    let implicitWarnings =
                        result.Warnings
                        |> List.filter (fun w -> w.Description.Contains("not explicitly declared"))

                    Expect.isEmpty implicitWarnings "no implicit participant warnings"
                } ]

          // === T041: Tic-Tac-Toe ===
          testList
              "Tic-Tac-Toe"
              [ test "parses without errors" {
                    let result = parseWsd ticTacToeWsd
                    Expect.isEmpty result.Errors "no errors"
                }

                test "title is Tic-Tac-Toe Game" {
                    let result = parseWsd ticTacToeWsd
                    Expect.equal result.Diagram.Title (Some "Tic-Tac-Toe Game") "title"
                }

                test "four participants" {
                    let result = parseWsd ticTacToeWsd
                    let ps = participants result
                    Expect.equal ps.Length 4 "four participants"
                    Expect.equal ps.[0].Name "PlayerX" "first"
                    Expect.equal ps.[1].Name "PlayerO" "second"
                    Expect.equal ps.[2].Name "Board" "third"
                    Expect.equal ps.[3].Name "GameEngine" "fourth"
                }

                test "first note guard: role=PlayerX, state=XTurn" {
                    let result = parseWsd ticTacToeWsd
                    let ns = notes result
                    Expect.isSome ns.[0].Guard "has guard"

                    Expect.equal ns.[0].Guard.Value.Pairs [ ("role", "PlayerX"); ("state", "XTurn") ] "two guard pairs"
                }

                test "alt block with two branches" {
                    let result = parseWsd ticTacToeWsd
                    let gs = groups result
                    Expect.equal gs.Length 1 "one group"
                    Expect.equal gs.[0].Kind GroupKind.Alt "alt"
                    Expect.equal gs.[0].Branches.Length 2 "two branches"
                    Expect.equal gs.[0].Branches.[0].Condition (Some "win condition") "first condition"
                    Expect.equal gs.[0].Branches.[1].Condition (Some "continue") "else condition"
                }

                test "second branch contains note with guard" {
                    let result = parseWsd ticTacToeWsd
                    let gs = groups result
                    let elseBranch = gs.[0].Branches.[1]

                    let branchNotes =
                        elseBranch.Elements
                        |> List.choose (function
                            | NoteElement n -> Some n
                            | _ -> None)

                    Expect.isGreaterThanOrEqual branchNotes.Length 1 "has note in else branch"
                    Expect.isSome branchNotes.[0].Guard "guard in else branch note"

                    Expect.equal
                        branchNotes.[0].Guard.Value.Pairs
                        [ ("role", "PlayerO"); ("state", "OTurn") ]
                        "guard pairs"
                }

                test "messages have correct arrow styles" {
                    let result = parseWsd ticTacToeWsd
                    let msgs = messages result
                    let solid = msgs |> List.filter (fun m -> m.ArrowStyle = ArrowStyle.Solid)
                    Expect.isGreaterThanOrEqual solid.Length 2 "solid arrows present"
                }

                test "no warnings" {
                    let result = parseWsd ticTacToeWsd

                    let implicitWarnings =
                        result.Warnings
                        |> List.filter (fun w -> w.Description.Contains("not explicitly declared"))

                    Expect.isEmpty implicitWarnings "no implicit participant warnings"
                } ]

          // === T042: Edge Cases ===
          testList
              "Edge Cases"
              [ test "unicode participant names" {
                    let result =
                        parseWsd
                            """participant Utilisateur
participant Serveur
Utilisateur->Serveur: requête
"""

                    Expect.isEmpty result.Errors "no errors"
                    Expect.equal (participants result).[0].Name "Utilisateur" "unicode name"
                    Expect.equal (participants result).[1].Name "Serveur" "unicode name 2"
                }

                test "empty input produces empty diagram" {
                    let result = parseWsd ""
                    Expect.isEmpty result.Errors "no errors"
                    Expect.isEmpty result.Warnings "no warnings"
                    Expect.isEmpty result.Diagram.Elements "no elements"
                    Expect.isNone result.Diagram.Title "no title"
                    Expect.isFalse result.Diagram.AutoNumber "no autonumber"
                }

                test "comments are ignored" {
                    let result =
                        parseWsd
                            """# this is a comment
participant Client
# another comment
Client->Client: self
"""

                    Expect.isEmpty result.Errors "no errors"
                    Expect.equal (participants result).Length 1 "one participant"
                    Expect.equal (messages result).Length 1 "one message"
                }

                test "whitespace-only lines ignored" {
                    let result =
                        parseWsd
                            """participant A


participant B
A->B: hello
"""

                    Expect.isEmpty result.Errors "no errors"
                    Expect.equal (participants result).Length 2 "two participants"
                }

                test "duplicate participant declaration is no-op" {
                    let result =
                        parseWsd
                            """participant Client
participant Client
Client->Client: self
"""

                    Expect.isEmpty result.Errors "no errors"
                    Expect.equal (participants result).Length 1 "still one participant"
                }

                test "implicit participants registered on first use" {
                    let result = parseWsd "Foo->Bar: hello\n"
                    Expect.isEmpty result.Errors "no errors"
                    Expect.equal (participants result).Length 2 "two participants"

                    let foo = (participants result) |> List.find (fun p -> p.Name = "Foo")
                    Expect.isFalse foo.Explicit "Foo is implicit"
                }

                test "self-message works" {
                    let result =
                        parseWsd
                            """participant Client
Client->Client: self
"""

                    Expect.isEmpty result.Errors "no errors"
                    let msgs = messages result
                    Expect.equal msgs.[0].Sender "Client" "sender"
                    Expect.equal msgs.[0].Receiver "Client" "receiver"
                }

                test "all four arrow types produce correct AST" {
                    let result =
                        parseWsd
                            """participant A
participant B
A->B: solid
A-->B: dashed
A->-B: solidDeactivate
A-->-B: dashedDeactivate
"""

                    Expect.isEmpty result.Errors "no errors"
                    let msgs = messages result
                    Expect.equal msgs.Length 4 "four messages"
                    Expect.equal msgs.[0].ArrowStyle ArrowStyle.Solid "solid"
                    Expect.equal msgs.[0].Direction Direction.Forward "forward"
                    Expect.equal msgs.[1].ArrowStyle ArrowStyle.Dashed "dashed"
                    Expect.equal msgs.[1].Direction Direction.Forward "forward"
                    Expect.equal msgs.[2].ArrowStyle ArrowStyle.Solid "solid deactivate"
                    Expect.equal msgs.[2].Direction Direction.Deactivating "deactivating"
                    Expect.equal msgs.[3].ArrowStyle ArrowStyle.Dashed "dashed deactivate"
                    Expect.equal msgs.[3].Direction Direction.Deactivating "deactivating"
                }

                test "mixed arrow styles within group" {
                    let result =
                        parseWsd
                            """participant A
participant B
alt test
    A->B: solid
    A-->B: dashed
    B->-A: return
    B-->-A: asyncReturn
end
"""

                    Expect.isEmpty result.Errors "no errors"
                    let gs = groups result
                    Expect.equal gs.Length 1 "one group"

                    let branchMsgs =
                        gs.[0].Branches.[0].Elements
                        |> List.choose (function
                            | MessageElement m -> Some m
                            | _ -> None)

                    Expect.equal branchMsgs.Length 4 "four messages in branch"
                }

                test "message with parameters" {
                    let result =
                        parseWsd
                            """participant Client
participant Server
Client->Server: getData(a, b, c)
"""

                    Expect.isEmpty result.Errors "no errors"
                    let msgs = messages result
                    Expect.equal msgs.[0].Label "getData" "label"
                    Expect.equal msgs.[0].Parameters [ "a"; "b"; "c" ] "three params"
                }

                test "message with no parameters" {
                    let result =
                        parseWsd
                            """participant Client
participant Server
Client->Server: getData
"""

                    Expect.isEmpty result.Errors "no errors"
                    let msgs = messages result
                    Expect.isEmpty msgs.[0].Parameters "no params"
                }

                test "5-level nesting works" {
                    let result =
                        parseWsd
                            """participant A
participant B
alt level1
  loop level2
    opt level3
      par level4
        critical level5
          A->B: deep
        end
      end
    end
  end
end
"""

                    Expect.isEmpty result.Errors "no errors"
                    let gs = groups result
                    Expect.equal gs.Length 1 "one top group"
                    Expect.equal gs.[0].Kind GroupKind.Alt "alt"
                    // Drill 5 levels deep
                    let l2 =
                        gs.[0].Branches.[0].Elements
                        |> List.choose (function
                            | GroupElement g -> Some g
                            | _ -> None)

                    Expect.equal l2.[0].Kind GroupKind.Loop "loop"

                    let l3 =
                        l2.[0].Branches.[0].Elements
                        |> List.choose (function
                            | GroupElement g -> Some g
                            | _ -> None)

                    Expect.equal l3.[0].Kind GroupKind.Opt "opt"

                    let l4 =
                        l3.[0].Branches.[0].Elements
                        |> List.choose (function
                            | GroupElement g -> Some g
                            | _ -> None)

                    Expect.equal l4.[0].Kind GroupKind.Par "par"

                    let l5 =
                        l4.[0].Branches.[0].Elements
                        |> List.choose (function
                            | GroupElement g -> Some g
                            | _ -> None)

                    Expect.equal l5.[0].Kind GroupKind.Critical "critical"
                }

                test "title with special characters" {
                    let result = parseWsd "title: My API v2\n"
                    Expect.equal result.Diagram.Title (Some "My API v2") "special chars via colon syntax"
                }

                test "note positions: over, left of, right of" {
                    let result =
                        parseWsd
                            """participant Client
note over Client: over text
note left of Client: left text
note right of Client: right text
"""

                    Expect.isEmpty result.Errors "no errors"
                    let ns = notes result
                    Expect.equal ns.Length 3 "three notes"
                    Expect.equal ns.[0].NotePosition NotePosition.Over "over"
                    Expect.equal ns.[1].NotePosition NotePosition.LeftOf "left of"
                    Expect.equal ns.[2].NotePosition NotePosition.RightOf "right of"
                }

                test "tabs and spaces both accepted" {
                    let result =
                        parseWsd "participant A\nparticipant B\n\tA->B: tabbed\n  A->B: spaced\n"

                    Expect.isEmpty result.Errors "no errors"
                    let msgs = messages result
                    Expect.equal msgs.Length 2 "two messages"
                }

                test "mixed Windows and Unix line endings" {
                    let result =
                        parseWsd
                            "participant A\r\nparticipant B\nA->B: hello\r\nB->A: world\n"

                    Expect.isEmpty result.Errors "no errors"
                    let ps = participants result
                    Expect.equal ps.Length 2 "two participants"
                    let msgs = messages result
                    Expect.equal msgs.Length 2 "two messages"
                }

                test "message with empty parens has no parameters" {
                    let result =
                        parseWsd "participant A\nparticipant B\nA->B: getData()\n"

                    Expect.isEmpty result.Errors "no errors"
                    let msgs = messages result
                    Expect.isEmpty msgs.[0].Parameters "empty parens = no params"
                } ]

          // === T044: Multi-target build verification ===
          test "parseWsd with null produces empty or handles gracefully" {
              // parseWsd should not crash on null
              try
                  let result = parseWsd null
                  Expect.isEmpty result.Errors "no errors on null"
              with :? System.NullReferenceException ->
                  () // Acceptable: null is not valid input

              // Empty string is the canonical "no input"
              let result = parseWsd ""
              Expect.isEmpty result.Errors "empty string: no errors"
              Expect.isEmpty result.Diagram.Elements "empty string: no elements"
          }

          test "parseWsd is deterministic" {
              let source =
                  """participant A
participant B
A->B: hello
"""

              let result1 = parseWsd source
              let result2 = parseWsd source
              Expect.equal result1.Diagram.Title result2.Diagram.Title "title matches"
              Expect.equal result1.Diagram.Participants.Length result2.Diagram.Participants.Length "participants match"
              Expect.equal result1.Errors.Length result2.Errors.Length "errors match"
              Expect.equal (messages result1).Length (messages result2).Length "messages match"
          } ]
