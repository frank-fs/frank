module Frank.Statecharts.Tests.Wsd.ParserTests

open Expecto
open Frank.Statecharts.Wsd.Types
open Frank.Statecharts.Wsd.Parser

let private messages (result: ParseResult) =
    result.Diagram.Elements
    |> List.choose (function
        | MessageElement m -> Some m
        | _ -> None)

let private notes (result: ParseResult) =
    result.Diagram.Elements
    |> List.choose (function
        | NoteElement n -> Some n
        | _ -> None)

let private participantDecls (result: ParseResult) =
    result.Diagram.Elements
    |> List.choose (function
        | ParticipantDecl p -> Some p
        | _ -> None)

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
              Expect.isNone result.Diagram.Title "no title"
              Expect.isFalse result.Diagram.AutoNumber "no autonumber"
              Expect.isEmpty result.Diagram.Participants "no participants"
              Expect.isEmpty result.Diagram.Elements "no elements"

          testCase "whitespace only produces empty diagram"
          <| fun _ ->
              let result = parseWsd "   \n  \n  "
              Expect.isEmpty result.Errors "no errors"
              Expect.isEmpty result.Diagram.Elements "no elements"

          testCase "single newline produces empty diagram"
          <| fun _ ->
              let result = parseWsd "\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.isEmpty result.Diagram.Elements "no elements"

          testCase "comments only produces empty diagram"
          <| fun _ ->
              let result = parseWsd "# comment\n# another comment\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.isEmpty result.Diagram.Elements "no elements"

          // === Participant tests (T019) ===
          testCase "participant explicit no alias"
          <| fun _ ->
              let result = parseWsd "participant Client\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Diagram.Participants.Length 1 "one participant"
              let p = result.Diagram.Participants.[0]
              Expect.equal p.Name "Client" "name"
              Expect.isNone p.Alias "no alias"
              Expect.isTrue p.Explicit "explicit"

          testCase "participant with string alias"
          <| fun _ ->
              let result = parseWsd "participant API as \"REST API\"\n"
              Expect.isEmpty result.Errors "no errors"
              let p = result.Diagram.Participants.[0]
              Expect.equal p.Name "API" "name"
              Expect.equal p.Alias (Some "REST API") "alias"
              Expect.isTrue p.Explicit "explicit"

          testCase "participant with identifier alias"
          <| fun _ ->
              let result = parseWsd "participant X as Y\n"
              Expect.isEmpty result.Errors "no errors"
              let p = result.Diagram.Participants.[0]
              Expect.equal p.Name "X" "name"
              Expect.equal p.Alias (Some "Y") "alias"

          testCase "duplicate participant is a no-op"
          <| fun _ ->
              let result = parseWsd "participant Client\nparticipant Client\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Diagram.Participants.Length 1 "still one participant"
              let decls = participantDecls result
              Expect.equal decls.Length 2 "two decl elements emitted"

          testCase "participant with no name produces error"
          <| fun _ ->
              let result = parseWsd "participant\n"
              Expect.hasLength result.Errors 1 "one error"
              Expect.equal result.Errors.[0].Description "Expected participant name" "error desc"

          testCase "multiple participants preserve order"
          <| fun _ ->
              let result = parseWsd "participant A\nparticipant B\nparticipant C\n"
              Expect.isEmpty result.Errors "no errors"
              let names = result.Diagram.Participants |> List.map (fun p -> p.Name)
              Expect.equal names [ "A"; "B"; "C" ] "order preserved"

          // === Message tests (T020) ===
          testCase "solid forward message"
          <| fun _ ->
              let result =
                  parseWsd "participant Client\nparticipant Server\nClient->Server: hello\n"

              Expect.isEmpty result.Errors "no errors"
              let msgs = messages result
              Expect.hasLength msgs 1 "one message"
              Expect.equal msgs.[0].Sender "Client" "sender"
              Expect.equal msgs.[0].Receiver "Server" "receiver"
              Expect.equal msgs.[0].ArrowStyle Solid "solid"
              Expect.equal msgs.[0].Direction Forward "forward"
              Expect.equal msgs.[0].Label "hello" "label"

          testCase "dashed forward message"
          <| fun _ ->
              let result = parseWsd "Client-->Server: getData\n"
              let msgs = messages result
              Expect.hasLength msgs 1 "one message"
              Expect.equal msgs.[0].ArrowStyle Dashed "dashed"
              Expect.equal msgs.[0].Direction Forward "forward"
              Expect.equal msgs.[0].Label "getData" "label"

          testCase "solid deactivating message"
          <| fun _ ->
              let result = parseWsd "Server->-Client: 200 OK\n"
              let msgs = messages result
              Expect.hasLength msgs 1 "one message"
              Expect.equal msgs.[0].ArrowStyle Solid "solid"
              Expect.equal msgs.[0].Direction Deactivating "deactivating"
              Expect.equal msgs.[0].Label "200 OK" "label"

          testCase "dashed deactivating message"
          <| fun _ ->
              let result = parseWsd "Server-->-Client: result\n"
              let msgs = messages result
              Expect.hasLength msgs 1 "one message"
              Expect.equal msgs.[0].ArrowStyle Dashed "dashed"
              Expect.equal msgs.[0].Direction Deactivating "deactivating"
              Expect.equal msgs.[0].Label "result" "label"

          testCase "message with parameters"
          <| fun _ ->
              let result = parseWsd "Client->API: createUser(name, email)\n"
              let msgs = messages result
              Expect.hasLength msgs 1 "one message"
              Expect.equal msgs.[0].Label "createUser" "label"
              Expect.equal msgs.[0].Parameters [ "name"; "email" ] "two params"

          testCase "message with empty parens"
          <| fun _ ->
              let result = parseWsd "Client->API: getStatus()\n"
              let msgs = messages result
              Expect.hasLength msgs 1 "one message"
              Expect.equal msgs.[0].Label "getStatus" "label"
              Expect.isEmpty msgs.[0].Parameters "empty params"

          testCase "message with no parens"
          <| fun _ ->
              let result = parseWsd "Client->API: simple\n"
              let msgs = messages result
              Expect.equal msgs.[0].Label "simple" "label"
              Expect.isEmpty msgs.[0].Parameters "no params"

          testCase "implicit participants produce warnings"
          <| fun _ ->
              let result = parseWsd "Foo->Bar: hello\n"
              let msgs = messages result
              Expect.hasLength msgs 1 "one message"
              // Should have warnings for implicit participants
              Expect.isGreaterThanOrEqual result.Warnings.Length 2 "at least two warnings for implicit participants"
              Expect.equal result.Diagram.Participants.Length 2 "two participants"

              let foo = result.Diagram.Participants |> List.find (fun p -> p.Name = "Foo")

              Expect.isFalse foo.Explicit "Foo is implicit"

          testCase "explicit then implicit: participant stays explicit"
          <| fun _ ->
              let result = parseWsd "participant Client\nClient->Server: hi\n"
              Expect.isEmpty result.Errors "no errors"

              let client = result.Diagram.Participants |> List.find (fun p -> p.Name = "Client")

              Expect.isTrue client.Explicit "Client is explicit"

              let server = result.Diagram.Participants |> List.find (fun p -> p.Name = "Server")

              Expect.isFalse server.Explicit "Server is implicit"

          testCase "missing receiver after arrow produces error"
          <| fun _ ->
              let result = parseWsd "Client->\n"
              Expect.isGreaterThanOrEqual result.Errors.Length 1 "at least one error"

          // === Directive tests (T021) ===
          testCase "title directive without colon"
          <| fun _ ->
              let result = parseWsd "title My Diagram\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Diagram.Title (Some "My Diagram") "title"

          testCase "title directive with colon"
          <| fun _ ->
              let result = parseWsd "title: My Diagram\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Diagram.Title (Some "My Diagram") "title"

          testCase "autonumber directive"
          <| fun _ ->
              let result = parseWsd "autonumber\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.isTrue result.Diagram.AutoNumber "autonumber enabled"

          testCase "duplicate title produces warning"
          <| fun _ ->
              let result = parseWsd "title First\ntitle Second\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Diagram.Title (Some "Second") "last title wins"

              Expect.isTrue
                  (result.Warnings
                   |> List.exists (fun w -> w.Description = "Duplicate title directive"))
                  "has duplicate warning"

          testCase "title with no text produces empty title"
          <| fun _ ->
              let result = parseWsd "title\n"
              Expect.isEmpty result.Errors "no errors"
              Expect.equal result.Diagram.Title (Some "") "empty title"

          // === Note tests (T022) ===
          testCase "note over participant"
          <| fun _ ->
              let result = parseWsd "participant Client\nnote over Client: This is a note\n"
              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.hasLength ns 1 "one note"
              Expect.equal ns.[0].NotePosition NotePosition.Over "over"
              Expect.equal ns.[0].Target "Client" "target"
              Expect.equal ns.[0].Content "This is a note" "content"
              Expect.isNone ns.[0].Guard "no guard"

          testCase "note left of participant"
          <| fun _ ->
              let result = parseWsd "participant Server\nnote left of Server: Internal detail\n"
              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.hasLength ns 1 "one note"
              Expect.equal ns.[0].NotePosition NotePosition.LeftOf "left of"

          testCase "note right of participant"
          <| fun _ ->
              let result = parseWsd "participant Server\nnote right of Server: External API\n"
              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.hasLength ns 1 "one note"
              Expect.equal ns.[0].NotePosition NotePosition.RightOf "right of"

          testCase "note with guard text is parsed by guard parser"
          <| fun _ ->
              let result = parseWsd "participant Client\nnote over Client: [guard: role=admin]\n"
              Expect.isEmpty result.Errors "no errors"
              let ns = notes result
              Expect.isSome ns.[0].Guard "guard is parsed (WP06 integration)"
              Expect.equal ns.[0].Guard.Value.Pairs [ ("role", "admin") ] "guard pairs"
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
                  result.Diagram.Elements
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
              Expect.equal result.Diagram.Participants.Length 2 "two participants"
              let msgs = messages result
              Expect.equal msgs.Length 2 "two messages"
              Expect.equal msgs.[0].ArrowStyle Solid "first is solid"
              Expect.equal msgs.[0].Direction Forward "first is forward"
              Expect.equal msgs.[1].ArrowStyle Solid "second is solid"
              Expect.equal msgs.[1].Direction Deactivating "second is deactivating"

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
              Expect.equal result.Diagram.Title (Some "My Sequence") "title"
              Expect.isTrue result.Diagram.AutoNumber "autonumber"

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
              let msgs = messages result
              Expect.equal msgs.[0].Label "createUser" "label"
              Expect.equal msgs.[0].Parameters [ "name"; "email"; "role" ] "three params"

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
              let msgs = messages result
              Expect.equal msgs.Length 4 "four messages"
              Expect.equal msgs.[0].ArrowStyle Solid "msg1 solid"
              Expect.equal msgs.[0].Direction Forward "msg1 forward"
              Expect.equal msgs.[1].ArrowStyle Dashed "msg2 dashed"
              Expect.equal msgs.[1].Direction Forward "msg2 forward"
              Expect.equal msgs.[2].ArrowStyle Solid "msg3 solid"
              Expect.equal msgs.[2].Direction Deactivating "msg3 deactivating"
              Expect.equal msgs.[3].ArrowStyle Dashed "msg4 dashed"
              Expect.equal msgs.[3].Direction Deactivating "msg4 deactivating"

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
              Expect.equal result.Diagram.Title (Some "System Overview") "title"
              Expect.isTrue result.Diagram.AutoNumber "autonumber"
              Expect.equal result.Diagram.Participants.Length 2 "two participants"
              let msgs = messages result
              Expect.equal msgs.Length 2 "two messages"
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

              let client = result.Diagram.Participants |> List.find (fun p -> p.Name = "Client")

              Expect.isTrue client.Explicit "Client upgraded to explicit" ]
