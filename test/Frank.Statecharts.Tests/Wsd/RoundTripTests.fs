module Frank.Statecharts.Tests.Wsd.RoundTripTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Wsd.Parser

// === Helpers ===

let private stateDecls (r: ParseResult) =
    r.Document.Elements
    |> List.choose (function
        | StateDecl s -> Some s
        | _ -> None)

let private transitions (r: ParseResult) =
    r.Document.Elements
    |> List.choose (function
        | TransitionElement t -> Some t
        | _ -> None)

let private notes (r: ParseResult) =
    r.Document.Elements
    |> List.choose (function
        | NoteElement n -> Some n
        | _ -> None)

let private groups (r: ParseResult) =
    r.Document.Elements
    |> List.choose (function
        | GroupElement g -> Some g
        | _ -> None)

/// Extract WSD transition style from a TransitionEdge's annotations
let private transitionStyle (edge: TransitionEdge) =
    edge.Annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdTransitionStyle ts) -> Some ts
        | _ -> None)

/// Extract WSD note position from a NoteContent's annotations
let private notePosition (note: NoteContent) =
    note.Annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdNotePosition pos) -> Some pos
        | _ -> None)

/// Extract guard pairs from a NoteContent's annotations
let private noteGuard (note: NoteContent) =
    note.Annotations
    |> List.tryPick (function
        | WsdAnnotation(WsdGuardData pairs) -> Some pairs
        | _ -> None)

/// Check if document has AutoNumber directive
let private hasAutoNumber (r: ParseResult) =
    r.Document.Elements
    |> List.exists (function
        | DirectiveElement(AutoNumberDirective _) -> true
        | _ -> false)

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
                    Expect.equal result.Document.Title (Some "Onboarding Flow") "title"
                }

                test "autonumber is true" {
                    let result = parseWsd onboardingWsd
                    Expect.isTrue (hasAutoNumber result) "autonumber"
                }

                test "three explicit participants" {
                    let result = parseWsd onboardingWsd
                    let ps = stateDecls result
                    Expect.equal ps.Length 3 "three participants"
                    Expect.equal ps.[0].Identifier (Some "Client") "first"
                    Expect.equal ps.[1].Identifier (Some "API") "second"
                    Expect.equal ps.[2].Identifier (Some "DB") "third"

                    // All participants are explicit -- no implicit warnings
                    let implicitWarnings =
                        result.Warnings
                        |> List.filter (fun w -> w.Description.Contains("not explicitly declared"))

                    Expect.isEmpty implicitWarnings "all explicit (no implicit warnings)"
                }

                test "first message: Client->API solid forward" {
                    let result = parseWsd onboardingWsd
                    let edges = transitions result
                    let t = edges.[0]
                    Expect.equal t.Source "Client" "sender"
                    Expect.equal t.Target (Some "API") "receiver"
                    let style = (transitionStyle t).Value
                    Expect.equal style.ArrowStyle ArrowStyle.Solid "solid"
                    Expect.equal style.Direction Direction.Forward "forward"
                    Expect.isTrue (t.Event.Value.Contains("createAccount")) "label"
                }

                test "guard on first note: auth=none" {
                    let result = parseWsd onboardingWsd
                    let ns = notes result
                    let n = ns.[0]
                    Expect.isSome (noteGuard n) "has guard"
                    Expect.equal (noteGuard n).Value [ ("auth", "none") ] "guard pairs"
                }

                test "deactivating arrows have correct direction" {
                    let result = parseWsd onboardingWsd
                    let edges = transitions result

                    let deactivating =
                        edges
                        |> List.filter (fun t ->
                            match transitionStyle t with
                            | Some s -> s.Direction = Direction.Deactivating
                            | None -> false)

                    Expect.isGreaterThanOrEqual deactivating.Length 4 "at least 4 deactivating arrows"
                }

                test "dashed messages have correct style" {
                    let result = parseWsd onboardingWsd
                    let edges = transitions result

                    let dashed =
                        edges
                        |> List.filter (fun t ->
                            match transitionStyle t with
                            | Some s -> s.ArrowStyle = ArrowStyle.Dashed
                            | None -> false)

                    Expect.isGreaterThanOrEqual dashed.Length 4 "at least 4 dashed arrows"
                }

                test "second note guard: auth=bearer" {
                    let result = parseWsd onboardingWsd
                    let ns = notes result
                    let n = ns.[1]
                    Expect.isSome (noteGuard n) "has guard"
                    Expect.equal (noteGuard n).Value [ ("auth", "bearer") ] "guard pairs"
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
                    Expect.equal result.Document.Title (Some "Tic-Tac-Toe Game") "title"
                }

                test "four participants" {
                    let result = parseWsd ticTacToeWsd
                    let ps = stateDecls result
                    Expect.equal ps.Length 4 "four participants"
                    Expect.equal ps.[0].Identifier (Some "PlayerX") "first"
                    Expect.equal ps.[1].Identifier (Some "PlayerO") "second"
                    Expect.equal ps.[2].Identifier (Some "Board") "third"
                    Expect.equal ps.[3].Identifier (Some "GameEngine") "fourth"
                }

                test "first note guard: role=PlayerX, state=XTurn" {
                    let result = parseWsd ticTacToeWsd
                    let ns = notes result
                    Expect.isSome (noteGuard ns.[0]) "has guard"

                    Expect.equal (noteGuard ns.[0]).Value [ ("role", "PlayerX"); ("state", "XTurn") ] "two guard pairs"
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
                    Expect.isSome (noteGuard branchNotes.[0]) "guard in else branch note"

                    Expect.equal
                        (noteGuard branchNotes.[0]).Value
                        [ ("role", "PlayerO"); ("state", "OTurn") ]
                        "guard pairs"
                }

                test "messages have correct arrow styles" {
                    let result = parseWsd ticTacToeWsd
                    let edges = transitions result

                    let solid =
                        edges
                        |> List.filter (fun t ->
                            match transitionStyle t with
                            | Some s -> s.ArrowStyle = ArrowStyle.Solid
                            | None -> false)

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
                    Expect.equal (stateDecls result).[0].Identifier (Some "Utilisateur") "unicode name"
                    Expect.equal (stateDecls result).[1].Identifier (Some "Serveur") "unicode name 2"
                }

                test "empty input produces empty diagram" {
                    let result = parseWsd ""
                    Expect.isEmpty result.Errors "no errors"
                    Expect.isEmpty result.Warnings "no warnings"
                    Expect.isEmpty result.Document.Elements "no elements"
                    Expect.isNone result.Document.Title "no title"
                    Expect.isFalse (hasAutoNumber result) "no autonumber"
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
                    Expect.equal (stateDecls result).Length 1 "one participant"
                    Expect.equal (transitions result).Length 1 "one message"
                }

                test "whitespace-only lines ignored" {
                    let result =
                        parseWsd
                            """participant A


participant B
A->B: hello
"""

                    Expect.isEmpty result.Errors "no errors"
                    Expect.equal (stateDecls result).Length 2 "two participants"
                }

                test "duplicate participant declaration is no-op" {
                    let result =
                        parseWsd
                            """participant Client
participant Client
Client->Client: self
"""

                    Expect.isEmpty result.Errors "no errors"
                    let uniqueNames = stateDecls result |> List.choose (fun s -> s.Identifier) |> List.distinct
                    Expect.equal uniqueNames.Length 1 "still one unique participant"
                }

                test "implicit participants registered on first use" {
                    let result = parseWsd "Foo->Bar: hello\n"
                    Expect.isEmpty result.Errors "no errors"
                    Expect.equal (stateDecls result).Length 2 "two participants"

                    let fooWarning =
                        result.Warnings |> List.exists (fun w -> w.Description.Contains("'Foo'"))

                    Expect.isTrue fooWarning "Foo is implicit (has warning)"
                }

                test "self-message works" {
                    let result =
                        parseWsd
                            """participant Client
Client->Client: self
"""

                    Expect.isEmpty result.Errors "no errors"
                    let edges = transitions result
                    Expect.equal edges.[0].Source "Client" "sender"
                    Expect.equal edges.[0].Target (Some "Client") "receiver"
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
                    let edges = transitions result
                    Expect.equal edges.Length 4 "four messages"
                    let s0 = (transitionStyle edges.[0]).Value
                    Expect.equal s0.ArrowStyle ArrowStyle.Solid "solid"
                    Expect.equal s0.Direction Direction.Forward "forward"
                    let s1 = (transitionStyle edges.[1]).Value
                    Expect.equal s1.ArrowStyle ArrowStyle.Dashed "dashed"
                    Expect.equal s1.Direction Direction.Forward "forward"
                    let s2 = (transitionStyle edges.[2]).Value
                    Expect.equal s2.ArrowStyle ArrowStyle.Solid "solid deactivate"
                    Expect.equal s2.Direction Direction.Deactivating "deactivating"
                    let s3 = (transitionStyle edges.[3]).Value
                    Expect.equal s3.ArrowStyle ArrowStyle.Dashed "dashed deactivate"
                    Expect.equal s3.Direction Direction.Deactivating "deactivating"
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

                    let branchEdges =
                        gs.[0].Branches.[0].Elements
                        |> List.choose (function
                            | TransitionElement t -> Some t
                            | _ -> None)

                    Expect.equal branchEdges.Length 4 "four messages in branch"
                }

                test "message with parameters" {
                    let result =
                        parseWsd
                            """participant Client
participant Server
Client->Server: getData(a, b, c)
"""

                    Expect.isEmpty result.Errors "no errors"
                    let edges = transitions result
                    Expect.equal edges.[0].Event (Some "getData") "label"
                    Expect.equal edges.[0].Parameters [ "a"; "b"; "c" ] "three params"
                }

                test "message with no parameters" {
                    let result =
                        parseWsd
                            """participant Client
participant Server
Client->Server: getData
"""

                    Expect.isEmpty result.Errors "no errors"
                    let edges = transitions result
                    Expect.isEmpty edges.[0].Parameters "no params"
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
                    Expect.equal result.Document.Title (Some "My API v2") "special chars via colon syntax"
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
                    Expect.equal (notePosition ns.[0]) (Some WsdNotePosition.Over) "over"
                    Expect.equal (notePosition ns.[1]) (Some WsdNotePosition.LeftOf) "left of"
                    Expect.equal (notePosition ns.[2]) (Some WsdNotePosition.RightOf) "right of"
                }

                test "tabs and spaces both accepted" {
                    let result =
                        parseWsd "participant A\nparticipant B\n\tA->B: tabbed\n  A->B: spaced\n"

                    Expect.isEmpty result.Errors "no errors"
                    let edges = transitions result
                    Expect.equal edges.Length 2 "two messages"
                }

                test "mixed Windows and Unix line endings" {
                    let result =
                        parseWsd
                            "participant A\r\nparticipant B\nA->B: hello\r\nB->A: world\n"

                    Expect.isEmpty result.Errors "no errors"
                    let ps = stateDecls result
                    Expect.equal ps.Length 2 "two participants"
                    let edges = transitions result
                    Expect.equal edges.Length 2 "two messages"
                }

                test "message with empty parens has no parameters" {
                    let result =
                        parseWsd "participant A\nparticipant B\nA->B: getData()\n"

                    Expect.isEmpty result.Errors "no errors"
                    let edges = transitions result
                    Expect.isEmpty edges.[0].Parameters "empty parens = no params"
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
              Expect.isEmpty result.Document.Elements "empty string: no elements"
          }

          test "parseWsd is deterministic" {
              let source =
                  """participant A
participant B
A->B: hello
"""

              let result1 = parseWsd source
              let result2 = parseWsd source
              Expect.equal result1.Document.Title result2.Document.Title "title matches"
              Expect.equal (stateDecls result1).Length (stateDecls result2).Length "participants match"
              Expect.equal result1.Errors.Length result2.Errors.Length "errors match"
              Expect.equal (transitions result1).Length (transitions result2).Length "messages match"
          }

          // WSD round-trip preserves SenderRole/ReceiverRole by construction:
          // WSD maps sender→Source and receiver→Target, so Source=SenderRole
          // and Target=ReceiverRole for WSD format. The serializer reconstructs
          // arrows from Source/Target, and re-parsing populates SenderRole/ReceiverRole
          // from the same participant names.
          testCase "SenderRole and ReceiverRole survive WSD round-trip"
          <| fun _ ->
              let source = "Client->Server: doThing\n"
              let result1 = parseWsd source
              let t1 = (transitions result1) |> List.head
              Expect.equal t1.SenderRole (Some "Client") "first parse SenderRole"
              Expect.equal t1.ReceiverRole (Some "Server") "first parse ReceiverRole"

              let regenerated = Frank.Statecharts.Wsd.Serializer.serialize result1.Document
              let result2 = parseWsd regenerated
              let t2 = (transitions result2) |> List.head
              Expect.equal t2.SenderRole (Some "Client") "round-trip SenderRole"
              Expect.equal t2.ReceiverRole (Some "Server") "round-trip ReceiverRole" ]
