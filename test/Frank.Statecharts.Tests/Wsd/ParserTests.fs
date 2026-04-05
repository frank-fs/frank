module Frank.Statecharts.Tests.Wsd.ParserTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Wsd.Parser

/// Extract TransitionEdge elements from parse result
let private transitions (result: ParseResult) =
    result.Document.Elements
    |> List.choose (function
        | TransitionElement t -> Some t
        | _ -> None)

/// Extract NoteContent elements from parse result
let private notes (result: ParseResult) =
    result.Document.Elements
    |> List.choose (function
        | NoteElement n -> Some n
        | _ -> None)

/// Extract StateNode declarations from parse result
let private stateDecls (result: ParseResult) =
    result.Document.Elements
    |> List.choose (function
        | StateDecl s -> Some s
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
let private hasAutoNumber (result: ParseResult) =
    result.Document.Elements
    |> List.exists (function
        | DirectiveElement(AutoNumberDirective _) -> true
        | _ -> false)

[<Tests>]
let parserTests =
    testList
        "Parser"
        [
          // === Empty/minimal inputs (T018) ===
          testCase "empty input produces empty diagram"
          <| fun _ ->
              let result = parseWsd ""
              Expect.isEmpty result.Errors "no errors"
              Expect.isEmpty result.Warnings "no warnings"
              Expect.isNone result.Document.Title "no title"
              Expect.isFalse (hasAutoNumber result) "no autonumber"
              Expect.isEmpty (stateDecls result) "no participants"
              Expect.isEmpty result.Document.Elements "no elements"

          testCase "whitespace only produces empty diagram"
          <| fun _ ->
              let result = parseWsd "   \n  \n  "
              Expect.isEmpty result.Errors "no errors"
              Expect.isEmpty result.Document.Elements "no elements"

          testCase "single newline produces empty diagram"
          <| fun _ ->
              let result = parseWsd "\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.isEmpty result.Document.Elements "no elements"

          testCase "comments only produces empty diagram"
          <| fun _ ->
              let result = parseWsd "# comment\n# another comment\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.isEmpty result.Document.Elements "no elements"

          // === Participant tests (T019) ===
          testCase "participant explicit no alias"
          <| fun _ ->
              let result = parseWsd "participant Client\n"
              Expect.isEmpty result.Errors "no errors"
              let decls = stateDecls result
              Expect.equal decls.Length 1 "one participant"
              let p = decls.[0]
              Expect.equal p.Identifier (Some "Client") "name"
              Expect.isNone p.Label "no alias"

          testCase "participant with string alias"
          <| fun _ ->
              let result = parseWsd "participant API as \"REST API\"\n"
              Expect.isEmpty result.Errors "no errors"
              let decls = stateDecls result
              let p = decls.[0]
              Expect.equal p.Identifier (Some "API") "name"
              Expect.equal p.Label (Some "REST API") "alias"

          testCase "participant with identifier alias"
          <| fun _ ->
              let result = parseWsd "participant X as Y\n"
              Expect.isEmpty result.Errors "no errors"
              let decls = stateDecls result
              let p = decls.[0]
              Expect.equal p.Identifier (Some "X") "name"
              Expect.equal p.Label (Some "Y") "alias"

          testCase "duplicate participant is a no-op"
          <| fun _ ->
              let result = parseWsd "participant Client\nparticipant Client\n"
              Expect.isEmpty result.Errors "no errors"
              // Unique identifiers -- deduplicated at the participant map level,
              // but two StateDecl elements are still emitted
              let decls = stateDecls result
              Expect.equal decls.Length 2 "two decl elements emitted"
              // Both have the same identifier
              let uniqueNames = decls |> List.choose (fun s -> s.Identifier) |> List.distinct
              Expect.equal uniqueNames.Length 1 "still one unique participant"

          testCase "participant with no name produces error"
          <| fun _ ->
              let result = parseWsd "participant\n"
              Expect.hasLength result.Errors 1 "one error"
              Expect.equal result.Errors.[0].Description "Expected participant name" "error desc"

          testCase "multiple participants preserve order"
          <| fun _ ->
              let result = parseWsd "participant A\nparticipant B\nparticipant C\n"
              Expect.isEmpty result.Errors "no errors"
              let names = stateDecls result |> List.choose (fun s -> s.Identifier)
              Expect.equal names [ "A"; "B"; "C" ] "order preserved"

          // === Message tests (T020) ===
          testCase "solid forward message"
          <| fun _ ->
              let result =
                  parseWsd "participant Client\nparticipant Server\nClient->Server: hello\n"

              Expect.isEmpty result.Errors "no errors"
              let edges = transitions result
              Expect.hasLength edges 1 "one message"
              Expect.equal edges.[0].Source "Client" "sender"
              Expect.equal edges.[0].Target (Some "Server") "receiver"
              let style = (transitionStyle edges.[0]).Value
              Expect.equal style.ArrowStyle ArrowStyle.Solid "solid"
              Expect.equal style.Direction Direction.Forward "forward"
              Expect.equal edges.[0].Event (Some "hello") "label"

          testCase "dashed forward message"
          <| fun _ ->
              let result = parseWsd "Client-->Server: getData\n"
              let edges = transitions result
              Expect.hasLength edges 1 "one message"
              let style = (transitionStyle edges.[0]).Value
              Expect.equal style.ArrowStyle ArrowStyle.Dashed "dashed"
              Expect.equal style.Direction Direction.Forward "forward"
              Expect.equal edges.[0].Event (Some "getData") "label"

          testCase "solid deactivating message"
          <| fun _ ->
              let result = parseWsd "Server->-Client: 200 OK\n"
              let edges = transitions result
              Expect.hasLength edges 1 "one message"
              let style = (transitionStyle edges.[0]).Value
              Expect.equal style.ArrowStyle ArrowStyle.Solid "solid"
              Expect.equal style.Direction Direction.Deactivating "deactivating"
              Expect.equal edges.[0].Event (Some "200 OK") "label"

          testCase "dashed deactivating message"
          <| fun _ ->
              let result = parseWsd "Server-->-Client: result\n"
              let edges = transitions result
              Expect.hasLength edges 1 "one message"
              let style = (transitionStyle edges.[0]).Value
              Expect.equal style.ArrowStyle ArrowStyle.Dashed "dashed"
              Expect.equal style.Direction Direction.Deactivating "deactivating"
              Expect.equal edges.[0].Event (Some "result") "label"

          testCase "message with parameters"
          <| fun _ ->
              let result = parseWsd "Client->API: createUser(name, email)\n"
              let edges = transitions result
              Expect.hasLength edges 1 "one message"
              Expect.equal edges.[0].Event (Some "createUser") "label"
              Expect.equal edges.[0].Parameters [ "name"; "email" ] "two params"

          testCase "message with empty parens"
          <| fun _ ->
              let result = parseWsd "Client->API: getStatus()\n"
              let edges = transitions result
              Expect.hasLength edges 1 "one message"
              Expect.equal edges.[0].Event (Some "getStatus") "label"
              Expect.isEmpty edges.[0].Parameters "empty params"

          testCase "message with no parens"
          <| fun _ ->
              let result = parseWsd "Client->API: simple\n"
              let edges = transitions result
              Expect.equal edges.[0].Event (Some "simple") "label"
              Expect.isEmpty edges.[0].Parameters "no params"

          testCase "implicit participants produce warnings"
          <| fun _ ->
              let result = parseWsd "Foo->Bar: hello\n"
              let edges = transitions result
              Expect.hasLength edges 1 "one message"
              // Should have warnings for implicit participants
              Expect.isGreaterThanOrEqual result.Warnings.Length 2 "at least two warnings for implicit participants"
              let decls = stateDecls result
              Expect.equal decls.Length 2 "two participants"

              // Check Foo is implicit by verifying the warning exists
              let fooWarning =
                  result.Warnings |> List.exists (fun w -> w.Description.Contains("'Foo'"))

              Expect.isTrue fooWarning "Foo has implicit warning"

          testCase "explicit then implicit: participant stays explicit"
          <| fun _ ->
              let result = parseWsd "participant Client\nClient->Server: hi\n"
              Expect.isEmpty result.Errors "no errors"

              // Client is explicit -- no implicit warning for Client
              let clientWarning =
                  result.Warnings |> List.exists (fun w -> w.Description.Contains("'Client'"))

              Expect.isFalse clientWarning "Client is explicit (no implicit warning)"

              // Server is implicit -- has implicit warning
              let serverWarning =
                  result.Warnings |> List.exists (fun w -> w.Description.Contains("'Server'"))

              Expect.isTrue serverWarning "Server is implicit"

          testCase "missing receiver after arrow produces error"
          <| fun _ ->
              let result = parseWsd "Client->\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"

          // === Directive tests (T021) ===
          testCase "title directive without colon"
          <| fun _ ->
              let result = parseWsd "title My Diagram\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Document.Title (Some "My Diagram") "title"

          testCase "title directive with colon"
          <| fun _ ->
              let result = parseWsd "title: My Diagram\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Document.Title (Some "My Diagram") "title"

          testCase "autonumber directive"
          <| fun _ ->
              let result = parseWsd "autonumber\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.isTrue (hasAutoNumber result) "autonumber enabled"

          testCase "duplicate title produces warning"
          <| fun _ ->
              let result = parseWsd "title First\ntitle Second\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Document.Title (Some "Second") "last title wins"

              Expect.isTrue
                  (result.Warnings
                   |> List.exists (fun w -> w.Description = "Duplicate title directive"))
                  "has duplicate warning"

          testCase "title with no text produces empty title"
          <| fun _ ->
              let result = parseWsd "title\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Document.Title (Some "") "empty title"

          // === Note tests (T022) ===
          testCase "note over participant"
          <| fun _ ->
              let result = parseWsd "participant Client\nnote over Client: This is a note\n"
              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.hasLength ns 1 "one note"
              Expect.equal (notePosition ns.[0]) (Some WsdNotePosition.Over) "over"
              Expect.equal ns.[0].Target "Client" "target"
              Expect.equal ns.[0].Content "This is a note" "content"
              Expect.isNone (noteGuard ns.[0]) "no guard"

          testCase "note left of participant"
          <| fun _ ->
              let result = parseWsd "participant Server\nnote left of Server: Internal detail\n"
              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.hasLength ns 1 "one note"
              Expect.equal (notePosition ns.[0]) (Some WsdNotePosition.LeftOf) "left of"

          testCase "note right of participant"
          <| fun _ ->
              let result = parseWsd "participant Server\nnote right of Server: External API\n"
              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.hasLength ns 1 "one note"
              Expect.equal (notePosition ns.[0]) (Some WsdNotePosition.RightOf) "right of"

          testCase "note with guard text is parsed by guard parser"
          <| fun _ ->
              let result = parseWsd "participant Client\nnote over Client: [guard: role=admin]\n"
              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.isSome (noteGuard ns.[0]) "guard is parsed (WP06 integration)"
              Expect.equal (noteGuard ns.[0]).Value [ ("role", "admin") ] "guard pairs"
              Expect.equal ns.[0].Content "" "content is empty after guard extraction"

          testCase "note with missing position keyword produces error"
          <| fun _ ->
              let result = parseWsd "note Client: text\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"

          testCase "note over with missing participant produces error"
          <| fun _ ->
              let result = parseWsd "note over\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"

          testCase "note over participant with missing colon produces error"
          <| fun _ ->
              let result = parseWsd "participant Client\nnote over Client text\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"

          // === Group parsing (WP05) ===
          testCase "group keyword produces GroupElement"
          <| fun _ ->
              let result = parseWsd "alt condition\nend\n"
              Expect.isEmpty result.Errors "no errors"

              let groups =
                  result.Document.Elements
                  |> List.choose (function
                      | GroupElement g -> Some g
                      | _ -> None)

              Expect.hasLength groups 1 "one group"
              Expect.equal groups.[0].Kind GroupKind.Alt "alt kind"
              Expect.hasLength groups.[0].Branches 1 "one branch"
              Expect.equal groups.[0].Branches.[0].Condition (Some "condition") "condition text"

          // === Mixed input / acceptance scenarios (T023) ===
          testCase "US1-S1: participants and solid messages"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant Client
participant Server
Client->Server: request
Server->-Client: response
"""

              Expect.isEmpty result.Errors "no errors"
              Expect.equal (stateDecls result).Length 2 "two participants"
              let edges = transitions result
              Expect.equal edges.Length 2 "two messages"
              let style0 = (transitionStyle edges.[0]).Value
              Expect.equal style0.ArrowStyle ArrowStyle.Solid "first is solid"
              Expect.equal style0.Direction Direction.Forward "first is forward"
              let style1 = (transitionStyle edges.[1]).Value
              Expect.equal style1.ArrowStyle ArrowStyle.Solid "second is solid"
              Expect.equal style1.Direction Direction.Deactivating "second is deactivating"

          testCase "US1-S2: title and autonumber directives"
          <| fun _ ->
              let result =
                  parseWsd
                      """
title My Sequence
autonumber
participant Client
"""

              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Document.Title (Some "My Sequence") "title"
              Expect.isTrue (hasAutoNumber result) "autonumber"

          testCase "US1-S3: message parameters"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant Client
participant API
Client->API: createUser(name, email, role)
"""

              Expect.isEmpty result.Errors "no errors"
              let edges = transitions result
              Expect.equal edges.[0].Event (Some "createUser") "label"
              Expect.equal edges.[0].Parameters [ "name"; "email"; "role" ] "three params"

          testCase "US1-S4: mixed arrow types"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant A
participant B
A->B: solid
A-->B: dashed
B->-A: solidDeact
B-->-A: dashedDeact
"""

              Expect.isEmpty result.Errors "no errors"
              let edges = transitions result
              Expect.equal edges.Length 4 "four messages"
              let s0 = (transitionStyle edges.[0]).Value
              Expect.equal s0.ArrowStyle ArrowStyle.Solid "msg1 solid"
              Expect.equal s0.Direction Direction.Forward "msg1 forward"
              let s1 = (transitionStyle edges.[1]).Value
              Expect.equal s1.ArrowStyle ArrowStyle.Dashed "msg2 dashed"
              Expect.equal s1.Direction Direction.Forward "msg2 forward"
              let s2 = (transitionStyle edges.[2]).Value
              Expect.equal s2.ArrowStyle ArrowStyle.Solid "msg3 solid"
              Expect.equal s2.Direction Direction.Deactivating "msg3 deactivating"
              let s3 = (transitionStyle edges.[3]).Value
              Expect.equal s3.ArrowStyle ArrowStyle.Dashed "msg4 dashed"
              Expect.equal s3.Direction Direction.Deactivating "msg4 deactivating"

          testCase "mixed elements: participants, messages, notes, directives"
          <| fun _ ->
              let result =
                  parseWsd
                      """
title System Overview
autonumber
participant Client
participant Server
Client->Server: login(user, pass)
note over Server: Validate credentials
Server->-Client: 200 OK
"""

              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Document.Title (Some "System Overview") "title"
              Expect.isTrue (hasAutoNumber result) "autonumber"
              Expect.equal (stateDecls result).Length 2 "two participants"
              let edges = transitions result
              Expect.equal edges.Length 2 "two messages"
              let ns = notes result
              Expect.equal ns.Length 1 "one note"

          testCase "implicit participant upgraded to explicit"
          <| fun _ ->
              let result =
                  parseWsd
                      """
participant Server
Client->Server: hello
participant Client
"""

              Expect.isEmpty result.Errors "no errors"

              // Client was implicit then explicitly declared -- no implicit warning should remain
              // (or if the warning was emitted before the explicit decl, that's parser-internal)
              // We verify Client appears as a StateDecl
              let clientDecls =
                  stateDecls result |> List.filter (fun s -> s.Identifier = Some "Client")

              Expect.isGreaterThanOrEqual clientDecls.Length 1 "Client appears as declared"

          // === AC-1: SenderRole and ReceiverRole populated from WSD participants (issue #307) ===
          testCase "AC-1: WSD message populates SenderRole and ReceiverRole on TransitionEdge"
          <| fun _ ->
              let result = parseWsd "Client->Server: doThing\n"
              Expect.isEmpty result.Errors "no errors"
              let edges = transitions result
              Expect.hasLength edges 1 "one transition"
              let edge = edges.[0]
              Expect.equal edge.SenderRole (Some "Client") "SenderRole = Client"
              Expect.equal edge.ReceiverRole (Some "Server") "ReceiverRole = Server"
              Expect.isNone edge.PayloadType "PayloadType = None (WSD has no payload type syntax)" ]
