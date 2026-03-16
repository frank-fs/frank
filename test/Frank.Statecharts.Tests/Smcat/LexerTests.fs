module Frank.Statecharts.Tests.Smcat.LexerTests

open Expecto
open Frank.Statecharts.Smcat.Types
open Frank.Statecharts.Smcat.Lexer

let private tokenKinds source =
    tokenize source |> List.map (fun t -> t.Kind)

let private tokenKindsNoEof source =
    tokenize source
    |> List.map (fun t -> t.Kind)
    |> List.filter (fun k -> k <> Eof)

[<Tests>]
let basicTokenTests =
    testList
        "Smcat.Lexer.BasicTokens"
        [ testCase "simple transition"
          <| fun _ ->
              let kinds = tokenKinds "a => b"
              Expect.equal kinds [ Identifier "a"; TransitionArrow; Identifier "b"; Eof ] "simple transition"

          testCase "comma separated states with semicolon"
          <| fun _ ->
              let kinds = tokenKindsNoEof "state1, state2;"

              Expect.equal
                  kinds
                  [ Identifier "state1"; Comma; Identifier "state2"; Semicolon ]
                  "comma and semicolon"

          testCase "transition with event"
          <| fun _ ->
              let kinds = tokenKinds "a => b: event;"

              Expect.equal
                  kinds
                  [ Identifier "a"
                    TransitionArrow
                    Identifier "b"
                    Colon
                    Identifier "event"
                    Semicolon
                    Eof ]
                  "transition with event" ]

[<Tests>]
let quotedStringTests =
    testList
        "Smcat.Lexer.QuotedStrings"
        [ testCase "basic quoted string"
          <| fun _ ->
              let kinds = tokenKindsNoEof "\"hello world\""
              Expect.equal kinds [ QuotedString "hello world" ] "basic quoted string"

          testCase "empty quoted string"
          <| fun _ ->
              let kinds = tokenKindsNoEof "\"\""
              Expect.equal kinds [ QuotedString "" ] "empty quoted string"

          testCase "escaped quote in quoted string"
          <| fun _ ->
              let kinds = tokenKindsNoEof "\"say \\\"hi\\\"\""
              Expect.equal kinds [ QuotedString "say \"hi\"" ] "escaped quotes"

          testCase "unicode characters preserved"
          <| fun _ ->
              let kinds = tokenKindsNoEof "\"caf\u00e9\""
              Expect.equal kinds [ QuotedString "caf\u00e9" ] "unicode preserved"

          testCase "newline inside quoted string"
          <| fun _ ->
              let tokens = tokenize "\"line1\nline2\""
              let qs = tokens |> List.find (fun t -> match t.Kind with QuotedString _ -> true | _ -> false)

              match qs.Kind with
              | QuotedString s -> Expect.equal s "line1\nline2" "newline in quoted string"
              | _ -> failtest "expected QuotedString"

          testCase "unclosed quoted string at EOF"
          <| fun _ ->
              let kinds = tokenKindsNoEof "\"unclosed"
              Expect.equal kinds [ QuotedString "unclosed" ] "unclosed string emits partial" ]

[<Tests>]
let activityTests =
    testList
        "Smcat.Lexer.Activities"
        [ testCase "entry slash"
          <| fun _ ->
              let kinds = tokenKindsNoEof "entry/ start"
              Expect.contains kinds EntrySlash "contains EntrySlash"

          testCase "exit slash"
          <| fun _ ->
              let kinds = tokenKindsNoEof "exit/ stop"
              Expect.contains kinds ExitSlash "contains ExitSlash"

          testCase "entry without slash is identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "entry"
              Expect.equal kinds [ Identifier "entry" ] "entry without slash is identifier"

          testCase "exit without slash is identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "exit"
              Expect.equal kinds [ Identifier "exit" ] "exit without slash is identifier"

          testCase "entry with space before slash"
          <| fun _ ->
              // "entry /" should be Identifier "entry" then ForwardSlash
              let kinds = tokenKindsNoEof "entry /"
              Expect.equal kinds [ Identifier "entry"; ForwardSlash ] "entry space slash"

          testCase "ellipsis"
          <| fun _ ->
              let kinds = tokenKindsNoEof "..."
              Expect.equal kinds [ Ellipsis ] "ellipsis" ]

[<Tests>]
let commentTests =
    testList
        "Smcat.Lexer.Comments"
        [ testCase "comment line is discarded"
          <| fun _ ->
              let kinds = tokenKinds "# comment\na => b"

              Expect.equal
                  kinds
                  [ Newline; Identifier "a"; TransitionArrow; Identifier "b"; Eof ]
                  "comment skipped"

          testCase "comment only input"
          <| fun _ ->
              let kinds = tokenKinds "# just a comment"
              // No tokens emitted for the comment itself; only Eof
              Expect.equal kinds [ Eof ] "comment only"

          testCase "comment after blank line"
          <| fun _ ->
              let kinds = tokenKinds "\n# comment\na"

              Expect.equal kinds [ Newline; Newline; Identifier "a"; Eof ] "comment after blank line" ]

[<Tests>]
let attributeTests =
    testList
        "Smcat.Lexer.Attributes"
        [ testCase "attribute bracket with key=value"
          <| fun _ ->
              let kinds = tokenKindsNoEof "[color=\"red\"]"

              Expect.equal
                  kinds
                  [ LeftBracket
                    Identifier "color"
                    Equals
                    QuotedString "red"
                    RightBracket ]
                  "attribute key=value" ]

[<Tests>]
let compositeStateTests =
    testList
        "Smcat.Lexer.CompositeStates"
        [ testCase "composite state braces"
          <| fun _ ->
              let kinds = tokenKindsNoEof "a { b => c; }"

              Expect.equal
                  kinds
                  [ Identifier "a"
                    LeftBrace
                    Identifier "b"
                    TransitionArrow
                    Identifier "c"
                    Semicolon
                    RightBrace ]
                  "composite braces" ]

[<Tests>]
let lineEndingTests =
    testList
        "Smcat.Lexer.LineEndings"
        [ testCase "unix line endings"
          <| fun _ ->
              let kinds = tokenKinds "a\nb"

              Expect.equal
                  kinds
                  [ Identifier "a"; Newline; Identifier "b"; Eof ]
                  "unix newline"

          testCase "windows line endings"
          <| fun _ ->
              let kinds = tokenKinds "a\r\nb"

              Expect.equal
                  kinds
                  [ Identifier "a"; Newline; Identifier "b"; Eof ]
                  "windows newline"

          testCase "carriage return only"
          <| fun _ ->
              let kinds = tokenKinds "a\rb"

              Expect.equal
                  kinds
                  [ Identifier "a"; Newline; Identifier "b"; Eof ]
                  "old mac newline"

          testCase "consecutive newlines"
          <| fun _ ->
              let kinds = tokenKinds "a\n\nb"

              Expect.equal
                  kinds
                  [ Identifier "a"; Newline; Newline; Identifier "b"; Eof ]
                  "consecutive newlines" ]

[<Tests>]
let pseudoStateTests =
    testList
        "Smcat.Lexer.PseudoStates"
        [ testCase "caret prefix"
          <| fun _ ->
              let kinds = tokenKindsNoEof "^choice"
              Expect.equal kinds [ Caret; Identifier "choice" ] "caret prefix"

          testCase "close bracket prefix at statement start"
          <| fun _ ->
              let kinds = tokenKinds "]forkjoin"
              Expect.contains kinds CloseBracketPrefix "close bracket prefix at start"

          testCase "close bracket prefix after semicolon"
          <| fun _ ->
              let kinds = tokenKinds "a; ]forkjoin"
              Expect.contains kinds CloseBracketPrefix "close bracket prefix after semicolon"

          testCase "close bracket prefix after newline"
          <| fun _ ->
              let kinds = tokenKinds "a\n]forkjoin"
              Expect.contains kinds CloseBracketPrefix "close bracket prefix after newline" ]

[<Tests>]
let positionTests =
    testList
        "Smcat.Lexer.Positions"
        [ testCase "first token at line 1 col 1"
          <| fun _ ->
              let tokens = tokenize "a"
              let first = tokens.Head
              Expect.equal first.Position.Line 1 "line 1"
              Expect.equal first.Position.Column 1 "col 1"

          testCase "second line position"
          <| fun _ ->
              let tokens = tokenize "a\nb"
              let b = tokens |> List.find (fun t -> t.Kind = Identifier "b")
              Expect.equal b.Position.Line 2 "line 2"
              Expect.equal b.Position.Column 1 "col 1"

          testCase "column advances correctly"
          <| fun _ ->
              let tokens = tokenize "a => b"
              let arrow = tokens |> List.find (fun t -> t.Kind = TransitionArrow)
              Expect.equal arrow.Position.Column 3 "arrow at col 3"
              let b = tokens |> List.find (fun t -> t.Kind = Identifier "b")
              Expect.equal b.Position.Column 6 "b at col 6"

          testCase "windows line ending counts as one newline"
          <| fun _ ->
              let tokens = tokenize "a\r\nb"
              let b = tokens |> List.find (fun t -> t.Kind = Identifier "b")
              Expect.equal b.Position.Line 2 "line 2 after CRLF"
              Expect.equal b.Position.Column 1 "col 1 after CRLF" ]

[<Tests>]
let identifierTests =
    testList
        "Smcat.Lexer.Identifiers"
        [ testCase "simple identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "myState"
              Expect.equal kinds [ Identifier "myState" ] "simple"

          testCase "underscore identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "_private"
              Expect.equal kinds [ Identifier "_private" ] "underscore"

          testCase "dot-separated identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "deep.history"
              Expect.equal kinds [ Identifier "deep.history" ] "dot-separated"

          testCase "hyphenated identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "state-name"
              Expect.equal kinds [ Identifier "state-name" ] "hyphenated"

          testCase "identifier stops before arrow"
          <| fun _ ->
              let kinds = tokenKinds "a=>b"

              Expect.equal
                  kinds
                  [ Identifier "a"; TransitionArrow; Identifier "b"; Eof ]
                  "identifier stops before arrow" ]

[<Tests>]
let punctuationTests =
    testList
        "Smcat.Lexer.Punctuation"
        [ testCase "colon"
          <| fun _ ->
              let kinds = tokenKindsNoEof ":"
              Expect.equal kinds [ Colon ] "colon"

          testCase "semicolon"
          <| fun _ ->
              let kinds = tokenKindsNoEof ";"
              Expect.equal kinds [ Semicolon ] "semicolon"

          testCase "comma"
          <| fun _ ->
              let kinds = tokenKindsNoEof ","
              Expect.equal kinds [ Comma ] "comma"

          testCase "equals not followed by >"
          <| fun _ ->
              let kinds = tokenKindsNoEof "="
              Expect.equal kinds [ Equals ] "equals"

          testCase "forward slash"
          <| fun _ ->
              let kinds = tokenKindsNoEof "/"
              Expect.equal kinds [ ForwardSlash ] "forward slash"

          testCase "left bracket"
          <| fun _ ->
              let kinds = tokenKindsNoEof "a ["
              Expect.contains kinds LeftBracket "left bracket"

          testCase "transition arrow =>"
          <| fun _ ->
              let kinds = tokenKindsNoEof "=>"
              Expect.equal kinds [ TransitionArrow ] "transition arrow" ]

[<Tests>]
let edgeCaseTests =
    testList
        "Smcat.Lexer.EdgeCases"
        [ testCase "empty input"
          <| fun _ ->
              let kinds = tokenKinds ""
              Expect.equal kinds [ Eof ] "empty input"

          testCase "whitespace only"
          <| fun _ ->
              let kinds = tokenKinds "   "
              Expect.equal kinds [ Eof ] "whitespace only"

          testCase "tabs only"
          <| fun _ ->
              let kinds = tokenKinds "\t\t"
              Expect.equal kinds [ Eof ] "tabs only"

          testCase "multiple statements"
          <| fun _ ->
              let kinds = tokenKinds "a => b;\nc => d;"

              Expect.equal
                  kinds
                  [ Identifier "a"
                    TransitionArrow
                    Identifier "b"
                    Semicolon
                    Newline
                    Identifier "c"
                    TransitionArrow
                    Identifier "d"
                    Semicolon
                    Eof ]
                  "multiple statements" ]

[<Tests>]
let integrationTests =
    testList
        "Smcat.Lexer.Integration"
        [ testCase "full smcat snippet"
          <| fun _ ->
              let source = "initial => idle;\nidle => \"processing\" : start;"

              let kinds = tokenKinds source

              Expect.equal
                  kinds
                  [ Identifier "initial"
                    TransitionArrow
                    Identifier "idle"
                    Semicolon
                    Newline
                    Identifier "idle"
                    TransitionArrow
                    QuotedString "processing"
                    Colon
                    Identifier "start"
                    Semicolon
                    Eof ]
                  "full smcat snippet"

          testCase "state with activities"
          <| fun _ ->
              let kinds = tokenKindsNoEof "idle [color=\"green\"]"

              Expect.equal
                  kinds
                  [ Identifier "idle"
                    LeftBracket
                    Identifier "color"
                    Equals
                    QuotedString "green"
                    RightBracket ]
                  "state with attributes"

          testCase "composite state with children"
          <| fun _ ->
              let source = "parent {\n  child1 => child2;\n}"
              let kinds = tokenKinds source

              Expect.equal
                  kinds
                  [ Identifier "parent"
                    LeftBrace
                    Newline
                    Identifier "child1"
                    TransitionArrow
                    Identifier "child2"
                    Semicolon
                    Newline
                    RightBrace
                    Eof ]
                  "composite state"

          testCase "entry and exit activities in state"
          <| fun _ ->
              let kinds = tokenKindsNoEof "entry/ startTimer\nexit/ stopTimer"

              Expect.equal
                  kinds
                  [ EntrySlash
                    Identifier "startTimer"
                    Newline
                    ExitSlash
                    Identifier "stopTimer" ]
                  "entry and exit activities" ]
