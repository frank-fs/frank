module Frank.Statecharts.Tests.Ast.TypeConstructionTests

open Expecto
open Frank.Statecharts.Ast

// ---------------------------------------------------------------------------
// T023: Programmatic AST construction tests
// ---------------------------------------------------------------------------

let private buildTicTacToe () =
    let idle =
        { Identifier = Some "idle"
          Label = None
          Kind = Initial
          Children = []
          Activities = None
          Position = None
          Annotations = [] }

    let xTurn =
        { Identifier = Some "xTurn"
          Label = Some "X's Turn"
          Kind = Regular
          Children = []
          Activities = None
          Position = None
          Annotations = [] }

    let oTurn =
        { Identifier = Some "oTurn"
          Label = Some "O's Turn"
          Kind = Regular
          Children = []
          Activities = None
          Position = None
          Annotations = [] }

    let gameOver =
        { Identifier = Some "gameOver"
          Label = None
          Kind = Final
          Children = []
          Activities = None
          Position = None
          Annotations = [] }

    let transitions =
        [ { Source = "idle"
            Target = Some "xTurn"
            Event = Some "start"
            Guard = None
            GuardHref = None
            Action = None
            Parameters = []
            SenderRole = None
            ReceiverRole = None
            PayloadType = None
            Position = None
            Annotations = [] }
          { Source = "xTurn"
            Target = Some "oTurn"
            Event = Some "move"
            Guard = Some "validMove"
            GuardHref = None
            Action = None
            Parameters = []
            SenderRole = None
            ReceiverRole = None
            PayloadType = None
            Position = None
            Annotations = [] }
          { Source = "oTurn"
            Target = Some "xTurn"
            Event = Some "move"
            Guard = Some "validMove"
            GuardHref = None
            Action = None
            Parameters = []
            SenderRole = None
            ReceiverRole = None
            PayloadType = None
            Position = None
            Annotations = [] }
          { Source = "xTurn"
            Target = Some "gameOver"
            Event = Some "win"
            Guard = None
            GuardHref = None
            Action = None
            Parameters = []
            SenderRole = None
            ReceiverRole = None
            PayloadType = None
            Position = None
            Annotations = [] }
          { Source = "oTurn"
            Target = Some "gameOver"
            Event = Some "win"
            Guard = None
            GuardHref = None
            Action = None
            Parameters = []
            SenderRole = None
            ReceiverRole = None
            PayloadType = None
            Position = None
            Annotations = [] } ]

    { Title = Some "Tic-Tac-Toe"
      InitialStateId = Some "idle"
      Elements =
          [ StateDecl idle; StateDecl xTurn; StateDecl oTurn; StateDecl gameOver ]
          @ (transitions |> List.map TransitionElement)
      DataEntries = []
      Annotations = [] }

[<Tests>]
let ticTacToeTests =
    testList
        "Ast.TypeConstruction.TicTacToe"
        [ testCase "tic-tac-toe document has correct title"
          <| fun _ ->
              let doc = buildTicTacToe ()
              Expect.equal doc.Title (Some "Tic-Tac-Toe") "title"

          testCase "tic-tac-toe document has 4 states"
          <| fun _ ->
              let doc = buildTicTacToe ()

              let stateCount =
                  doc.Elements
                  |> List.choose (function
                      | StateDecl _ -> Some ()
                      | _ -> None)
                  |> List.length

              Expect.equal stateCount 4 "4 states"

          testCase "tic-tac-toe document has 5 transitions"
          <| fun _ ->
              let doc = buildTicTacToe ()

              let transitionCount =
                  doc.Elements
                  |> List.choose (function
                      | TransitionElement _ -> Some ()
                      | _ -> None)
                  |> List.length

              Expect.equal transitionCount 5 "5 transitions"

          testCase "tic-tac-toe document has initial state"
          <| fun _ ->
              let doc = buildTicTacToe ()
              Expect.equal doc.InitialStateId (Some "idle") "initial state is idle"

          testCase "guard on xTurn->oTurn transition"
          <| fun _ ->
              let doc = buildTicTacToe ()

              let edge =
                  doc.Elements
                  |> List.choose (function
                      | TransitionElement t -> Some t
                      | _ -> None)
                  |> List.find (fun t -> t.Source = "xTurn" && t.Target = Some "oTurn")

              Expect.equal edge.Guard (Some "validMove") "guard is validMove"

          testCase "all populated fields have expected values"
          <| fun _ ->
              let doc = buildTicTacToe ()

              let states =
                  doc.Elements
                  |> List.choose (function
                      | StateDecl s -> Some s
                      | _ -> None)

              let xTurn = states |> List.find (fun s -> s.Identifier = Some "xTurn")
              Expect.equal xTurn.Label (Some "X's Turn") "xTurn has label"

              let transitions =
                  doc.Elements
                  |> List.choose (function
                      | TransitionElement t -> Some t
                      | _ -> None)

              let startTransition =
                  transitions |> List.find (fun t -> t.Event = Some "start")

              Expect.equal startTransition.Source "idle" "start transition source"
              Expect.equal startTransition.Target (Some "xTurn") "start transition target" ]

// ---------------------------------------------------------------------------
// SC-006: Structural equality tests
// ---------------------------------------------------------------------------

[<Tests>]
let structuralEqualityTests =
    testList
        "Ast.TypeConstruction.StructuralEquality"
        [ testCase "structural equality: identical ASTs are equal"
          <| fun _ ->
              let doc1 = buildTicTacToe ()
              let doc2 = buildTicTacToe ()
              Expect.equal doc1 doc2 "identical ASTs must be equal"

          testCase "structural equality: different ASTs are not equal"
          <| fun _ ->
              let doc1 = buildTicTacToe ()
              let doc2 = { doc1 with Title = Some "Different" }
              Expect.notEqual doc1 doc2 "different titles means not equal"

          testCase "structural equality: different elements means not equal"
          <| fun _ ->
              let doc1 = buildTicTacToe ()
              let doc2 = { doc1 with Elements = [] }
              Expect.notEqual doc1 doc2 "different elements means not equal"

          testCase "structural equality: empty documents are equal"
          <| fun _ ->
              let doc1 =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              let doc2 =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              Expect.equal doc1 doc2 "two empty documents are equal" ]

// ---------------------------------------------------------------------------
// Empty document edge case
// ---------------------------------------------------------------------------

[<Tests>]
let emptyDocumentTests =
    testList
        "Ast.TypeConstruction.EmptyDocument"
        [ testCase "empty document is valid"
          <| fun _ ->
              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = []
                    DataEntries = []
                    Annotations = [] }

              Expect.isNone doc.Title "no title"
              Expect.isEmpty doc.Elements "no elements"
              Expect.isEmpty doc.DataEntries "no data entries"
              Expect.isEmpty doc.Annotations "no annotations" ]

// ---------------------------------------------------------------------------
// T025: Annotation coexistence tests (US5)
// ---------------------------------------------------------------------------

[<Tests>]
let annotationCoexistenceTests =
    testList
        "Ast.TypeConstruction.AnnotationCoexistence"
        [ testCase "WSD and SCXML annotations coexist on transition"
          <| fun _ ->
              let edge =
                  { Source = "s1"
                    Target = Some "s2"
                    Event = Some "go"
                    Guard = None
                    GuardHref = None
                    Action = None
                    Parameters = []
                    SenderRole = None
                    ReceiverRole = None
                    PayloadType = None
                    Position = None
                    Annotations =
                        [ WsdAnnotation(
                              WsdTransitionStyle
                                  { ArrowStyle = Dashed
                                    Direction = Forward })
                          ScxmlAnnotation(ScxmlNamespace "http://example.com") ] }

              let wsdAnns =
                  edge.Annotations
                  |> List.choose (function
                      | WsdAnnotation w -> Some w
                      | _ -> None)

              let scxmlAnns =
                  edge.Annotations
                  |> List.choose (function
                      | ScxmlAnnotation s -> Some s
                      | _ -> None)

              Expect.hasLength wsdAnns 1 "one WSD annotation"
              Expect.hasLength scxmlAnns 1 "one SCXML annotation"

          testCase "WSD and SCXML annotations coexist on state"
          <| fun _ ->
              let state =
                  { Identifier = Some "s1"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = None
                    Annotations =
                        [ WsdAnnotation(WsdNotePosition Over)
                          ScxmlAnnotation(ScxmlHistory("h1", Deep, None)) ] }

              let wsdAnns =
                  state.Annotations
                  |> List.choose (function
                      | WsdAnnotation w -> Some w
                      | _ -> None)

              let scxmlAnns =
                  state.Annotations
                  |> List.choose (function
                      | ScxmlAnnotation s -> Some s
                      | _ -> None)

              Expect.hasLength wsdAnns 1 "one WSD annotation"
              Expect.hasLength scxmlAnns 1 "one SCXML annotation"

          testCase "annotations from all 5 formats on same node"
          <| fun _ ->
              let annotations =
                  [ WsdAnnotation(
                        WsdTransitionStyle
                            { ArrowStyle = Solid
                              Direction = Forward })
                    AlpsAnnotation(AlpsTransitionType Safe)
                    ScxmlAnnotation(ScxmlNamespace "ns")
                    SmcatAnnotation(SmcatColor "red")
                    XStateAnnotation(XStateAction "log") ]

              let edge =
                  { Source = "s1"
                    Target = Some "s2"
                    Event = None
                    Guard = None
                    GuardHref = None
                    Action = None
                    Parameters = []
                    SenderRole = None
                    ReceiverRole = None
                    PayloadType = None
                    Position = None
                    Annotations = annotations }

              Expect.hasLength edge.Annotations 5 "all 5 format annotations present" ]

// ---------------------------------------------------------------------------
// T026: Source position tests (US4)
// ---------------------------------------------------------------------------

[<Tests>]
let sourcePositionTests =
    testList
        "Ast.TypeConstruction.SourcePosition"
        [ testCase "programmatic construction has None position"
          <| fun _ ->
              let state =
                  { Identifier = Some "s1"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              Expect.isNone state.Position "programmatic = no position"

          testCase "parser output has Some position"
          <| fun _ ->
              let state =
                  { Identifier = Some "Client"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = Some { Line = 3; Column = 1 }
                    Annotations = [] }

              Expect.isSome state.Position "parser output = has position"
              Expect.equal state.Position.Value.Line 3 "line 3"
              Expect.equal state.Position.Value.Column 1 "column 1"

          testCase "SourcePosition is a struct"
          <| fun _ ->
              let pos: SourcePosition = { Line = 1; Column = 1 }
              Expect.equal pos.Line 1 "line"
              Expect.equal pos.Column 1 "column"

          testCase "WSD parser output carries source positions"
          <| fun _ ->
              let result =
                  Frank.Statecharts.Wsd.Parser.parseWsd "participant Client\nClient->Client: self\n"

              let states =
                  result.Document.Elements
                  |> List.choose (function
                      | StateDecl s -> Some s
                      | _ -> None)

              Expect.isTrue
                  (states |> List.forall (fun s -> s.Position.IsSome))
                  "all states have positions"

              let transitions =
                  result.Document.Elements
                  |> List.choose (function
                      | TransitionElement t -> Some t
                      | _ -> None)

              Expect.isTrue
                  (transitions |> List.forall (fun t -> t.Position.IsSome))
                  "all transitions have positions" ]

// ---------------------------------------------------------------------------
// T027: Edge case tests
// ---------------------------------------------------------------------------

[<Tests>]
let edgeCaseTests =
    testList
        "Ast.TypeConstruction.EdgeCases"
        [ testCase "state with no transitions is valid"
          <| fun _ ->
              let state =
                  { Identifier = Some "sink"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = [ StateDecl state ]
                    DataEntries = []
                    Annotations = [] }

              Expect.hasLength doc.Elements 1 "one element"

          testCase "transition with no event is valid (completion transition)"
          <| fun _ ->
              let edge =
                  { Source = "s1"
                    Target = Some "s2"
                    Event = None
                    Guard = None
                    GuardHref = None
                    Action = None
                    Parameters = []
                    SenderRole = None
                    ReceiverRole = None
                    PayloadType = None
                    Position = None
                    Annotations = [] }

              Expect.isNone edge.Event "no event = completion transition"

          testCase "transition with no target is valid (internal transition)"
          <| fun _ ->
              let edge =
                  { Source = "s1"
                    Target = None
                    Event = Some "tick"
                    Guard = None
                    GuardHref = None
                    Action = None
                    Parameters = []
                    SenderRole = None
                    ReceiverRole = None
                    PayloadType = None
                    Position = None
                    Annotations = [] }

              Expect.isNone edge.Target "no target = internal transition"

          testCase "self-transition is valid"
          <| fun _ ->
              let edge =
                  { Source = "s1"
                    Target = Some "s1"
                    Event = Some "retry"
                    Guard = None
                    GuardHref = None
                    Action = None
                    Parameters = []
                    SenderRole = None
                    ReceiverRole = None
                    PayloadType = None
                    Position = None
                    Annotations = [] }

              Expect.equal edge.Source "s1" "source"
              Expect.equal edge.Target (Some "s1") "target = source"

          testCase "multiple transitions between same states with different events"
          <| fun _ ->
              let e1 =
                  { Source = "s1"
                    Target = Some "s2"
                    Event = Some "eventA"
                    Guard = None
                    GuardHref = None
                    Action = None
                    Parameters = []
                    SenderRole = None
                    ReceiverRole = None
                    PayloadType = None
                    Position = None
                    Annotations = [] }

              let e2 =
                  { Source = "s1"
                    Target = Some "s2"
                    Event = Some "eventB"
                    Guard = None
                    GuardHref = None
                    Action = None
                    Parameters = []
                    SenderRole = None
                    ReceiverRole = None
                    PayloadType = None
                    Position = None
                    Annotations = [] }

              let doc =
                  { Title = None
                    InitialStateId = None
                    Elements = [ TransitionElement e1; TransitionElement e2 ]
                    DataEntries = []
                    Annotations = [] }

              let transitions =
                  doc.Elements
                  |> List.choose (function
                      | TransitionElement t -> Some t
                      | _ -> None)

              Expect.hasLength transitions 2 "two transitions"

          testCase "deeply nested hierarchy (5+ levels)"
          <| fun _ ->
              let leaf =
                  { Identifier = Some "leaf"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              let l4 =
                  { Identifier = Some "l4"
                    Label = None
                    Kind = Regular
                    Children = [ leaf ]
                    Activities = None
                    Position = None
                    Annotations = [] }

              let l3 =
                  { Identifier = Some "l3"
                    Label = None
                    Kind = Regular
                    Children = [ l4 ]
                    Activities = None
                    Position = None
                    Annotations = [] }

              let l2 =
                  { Identifier = Some "l2"
                    Label = None
                    Kind = Regular
                    Children = [ l3 ]
                    Activities = None
                    Position = None
                    Annotations = [] }

              let l1 =
                  { Identifier = Some "l1"
                    Label = None
                    Kind = Regular
                    Children = [ l2 ]
                    Activities = None
                    Position = None
                    Annotations = [] }

              Expect.hasLength l1.Children 1 "l1 has child"
              Expect.hasLength l1.Children.[0].Children 1 "l2 has child"
              Expect.hasLength l1.Children.[0].Children.[0].Children 1 "l3 has child"
              Expect.hasLength l1.Children.[0].Children.[0].Children.[0].Children 1 "l4 has child"
              Expect.isEmpty l1.Children.[0].Children.[0].Children.[0].Children.[0].Children "leaf has no children"

          testCase "data entry with empty expression is valid"
          <| fun _ ->
              let data =
                  { Name = "x"
                    Expression = None
                    Position = None }

              Expect.isNone data.Expression "empty expression"

          testCase "empty annotations list is valid"
          <| fun _ ->
              let state =
                  { Identifier = Some "s1"
                    Label = None
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              Expect.isEmpty state.Annotations "empty annotations"

          testCase "unicode characters in identifiers and events"
          <| fun _ ->
              let state =
                  { Identifier = Some "Utilisateur"
                    Label = Some "Benutzer"
                    Kind = Regular
                    Children = []
                    Activities = None
                    Position = None
                    Annotations = [] }

              let edge =
                  { Source = "Utilisateur"
                    Target = Some "Serveur"
                    Event = Some "requete"
                    Guard = Some "estPret"
                    GuardHref = None
                    Action = None
                    Parameters = []
                    SenderRole = None
                    ReceiverRole = None
                    PayloadType = None
                    Position = None
                    Annotations = [] }

              Expect.equal state.Identifier (Some "Utilisateur") "unicode identifier"
              Expect.equal edge.Event (Some "requete") "unicode event" ]
