module Wsd.LexerTests

open Expecto
open Frank.Statecharts.Wsd.Types
open Frank.Statecharts.Wsd.Lexer

let private tokenKinds source =
    tokenize source |> List.map (fun t -> t.Kind)

let private tokenKindsNoEof source =
    tokenize source
    |> List.map (fun t -> t.Kind)
    |> List.filter (fun k -> k <> Eof && k <> Newline)

[<Tests>]
let keywordTests =
    testList
        "Lexer.Keywords"
        [ testCase "participant keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "participant"
              Expect.equal kinds [ Participant ] "participant"

          testCase "title keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "title"
              Expect.equal kinds [ Title ] "title"

          testCase "autonumber keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "autonumber"
              Expect.equal kinds [ AutoNumber ] "autonumber"

          testCase "note keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "note"
              Expect.equal kinds [ Note ] "note"

          testCase "over keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "over"
              Expect.equal kinds [ TokenKind.Over ] "over"

          testCase "left of multi-word keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "left of"
              Expect.equal kinds [ TokenKind.LeftOf ] "left of"

          testCase "right of multi-word keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "right of"
              Expect.equal kinds [ TokenKind.RightOf ] "right of"

          testCase "alt keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "alt"
              Expect.equal kinds [ TokenKind.Alt ] "alt"

          testCase "opt keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "opt"
              Expect.equal kinds [ TokenKind.Opt ] "opt"

          testCase "loop keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "loop"
              Expect.equal kinds [ TokenKind.Loop ] "loop"

          testCase "end keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "end"
              Expect.equal kinds [ End ] "end"

          testCase "else keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "else"
              Expect.equal kinds [ Else ] "else"

          testCase "as keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "as"
              Expect.equal kinds [ As ] "as"

          testCase "case insensitive keywords"
          <| fun _ ->
              let kinds = tokenKindsNoEof "PARTICIPANT"
              Expect.equal kinds [ Participant ] "uppercase participant"

          testCase "mixed case keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "Participant"
              Expect.equal kinds [ Participant ] "mixed case"

          testCase "LEFT OF case insensitive"
          <| fun _ ->
              let kinds = tokenKindsNoEof "LEFT OF"
              Expect.equal kinds [ TokenKind.LeftOf ] "LEFT OF"

          testCase "left without of is identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "left"
              Expect.equal kinds [ Identifier "left" ] "left alone is not LeftOf"

          testCase "participants is identifier not keyword"
          <| fun _ ->
              let kinds = tokenKindsNoEof "participants"
              Expect.equal kinds [ Identifier "participants" ] "longer word is identifier" ]

[<Tests>]
let arrowTests =
    testList
        "Lexer.Arrows"
        [ testCase "solid arrow ->"
          <| fun _ ->
              let kinds = tokenKinds "A->B"

              Expect.equal kinds [ Identifier "A"; SolidArrow; Identifier "B"; Newline; Eof ] "solid arrow"

          testCase "dashed arrow -->"
          <| fun _ ->
              let kinds = tokenKinds "A-->B"

              Expect.equal kinds [ Identifier "A"; DashedArrow; Identifier "B"; Newline; Eof ] "dashed arrow"

          testCase "solid deactivate ->-"
          <| fun _ ->
              let kinds = tokenKinds "A->-B"

              Expect.equal kinds [ Identifier "A"; SolidDeactivate; Identifier "B"; Newline; Eof ] "solid deactivate"

          testCase "dashed deactivate -->-"
          <| fun _ ->
              let kinds = tokenKinds "A-->-B"

              Expect.equal kinds [ Identifier "A"; DashedDeactivate; Identifier "B"; Newline; Eof ] "dashed deactivate"

          testCase "solid arrow message"
          <| fun _ ->
              let kinds = tokenKinds "Client->Server: hello"

              Expect.equal
                  kinds
                  [ Identifier "Client"
                    SolidArrow
                    Identifier "Server"
                    Colon
                    TextContent "hello"
                    Newline
                    Eof ]
                  "solid arrow message tokens" ]

[<Tests>]
let punctuationTests =
    testList
        "Lexer.Punctuation"
        [ testCase "colon"
          <| fun _ ->
              let kinds = tokenKindsNoEof ":"
              Expect.equal kinds [ Colon ] "colon"

          testCase "parentheses"
          <| fun _ ->
              let kinds = tokenKindsNoEof "()"
              Expect.equal kinds [ LeftParen; RightParen ] "parens"

          testCase "comma"
          <| fun _ ->
              let kinds = tokenKindsNoEof ","
              Expect.equal kinds [ Comma ] "comma"

          testCase "brackets"
          <| fun _ ->
              let kinds = tokenKindsNoEof "[]"
              Expect.equal kinds [ LeftBracket; RightBracket ] "brackets"

          testCase "equals"
          <| fun _ ->
              let kinds = tokenKindsNoEof "="
              Expect.equal kinds [ Equals ] "equals" ]

[<Tests>]
let identifierTests =
    testList
        "Lexer.Identifiers"
        [ testCase "simple identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "Alice"
              Expect.equal kinds [ Identifier "Alice" ] "simple"

          testCase "underscore identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "_private"
              Expect.equal kinds [ Identifier "_private" ] "underscore"

          testCase "alphanumeric identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "Server2"
              Expect.equal kinds [ Identifier "Server2" ] "alphanumeric"

          testCase "hyphenated identifier"
          <| fun _ ->
              let kinds = tokenKindsNoEof "my-service"
              Expect.equal kinds [ Identifier "my-service" ] "hyphenated"

          testCase "hyphenated identifier stops before arrow"
          <| fun _ ->
              let kinds = tokenKinds "my-service->other"

              Expect.equal
                  kinds
                  [ Identifier "my-service"; SolidArrow; Identifier "other"; Newline; Eof ]
                  "hyphen before arrow" ]

[<Tests>]
let stringLiteralTests =
    testList
        "Lexer.StringLiterals"
        [ testCase "basic string literal"
          <| fun _ ->
              let kinds = tokenKindsNoEof "\"hello world\""
              Expect.equal kinds [ StringLiteral "hello world" ] "basic string"

          testCase "escaped quote in string"
          <| fun _ ->
              let kinds = tokenKindsNoEof "\"say \\\"hi\\\"\""
              Expect.equal kinds [ StringLiteral "say \"hi\"" ] "escaped quotes"

          testCase "empty string"
          <| fun _ ->
              let kinds = tokenKindsNoEof "\"\""
              Expect.equal kinds [ StringLiteral "" ] "empty string" ]

[<Tests>]
let textContentTests =
    testList
        "Lexer.TextContent"
        [ testCase "text after colon"
          <| fun _ ->
              let kinds = tokenKinds "A->B: hello world"

              Expect.equal
                  kinds
                  [ Identifier "A"
                    SolidArrow
                    Identifier "B"
                    Colon
                    TextContent "hello world"
                    Newline
                    Eof ]
                  "text after colon"

          testCase "text after alt keyword"
          <| fun _ ->
              let kinds = tokenKinds "alt condition is true"

              Expect.equal kinds [ TokenKind.Alt; TextContent "condition is true"; Newline; Eof ] "alt with condition"

          testCase "text after loop keyword"
          <| fun _ ->
              let kinds = tokenKinds "loop 10 times"

              Expect.equal kinds [ TokenKind.Loop; TextContent "10 times"; Newline; Eof ] "loop with text"

          testCase "colon with no text after"
          <| fun _ ->
              let kinds = tokenKinds "A->B:"

              Expect.equal kinds [ Identifier "A"; SolidArrow; Identifier "B"; Colon; Newline; Eof ] "colon no text" ]

[<Tests>]
let commentTests =
    testList
        "Lexer.Comments"
        [ testCase "full line comment"
          <| fun _ ->
              let kinds = tokenKinds "# this is a comment\nA->B"

              Expect.equal kinds [ Newline; Identifier "A"; SolidArrow; Identifier "B"; Newline; Eof ] "comment skipped"

          testCase "comment only input"
          <| fun _ ->
              let kinds = tokenKinds "# just a comment"
              Expect.equal kinds [ Newline; Eof ] "comment only" ]

[<Tests>]
let whitespaceTests =
    testList
        "Lexer.Whitespace"
        [ testCase "blank line emits newline"
          <| fun _ ->
              let kinds = tokenKinds "\n"
              Expect.equal kinds [ Newline; Eof ] "blank line"

          testCase "tabs are whitespace"
          <| fun _ ->
              let kinds = tokenKinds "\tparticipant\tAlice"

              Expect.equal kinds [ Participant; Identifier "Alice"; Newline; Eof ] "tabs as whitespace" ]

[<Tests>]
let lineEndingTests =
    testList
        "Lexer.LineEndings"
        [ testCase "unix line endings"
          <| fun _ ->
              let kinds = tokenKinds "A\nB"

              Expect.equal kinds [ Identifier "A"; Newline; Identifier "B"; Newline; Eof ] "unix"

          testCase "windows line endings"
          <| fun _ ->
              let kinds = tokenKinds "A\r\nB"

              Expect.equal kinds [ Identifier "A"; Newline; Identifier "B"; Newline; Eof ] "windows"

          testCase "mixed line endings"
          <| fun _ ->
              let kinds = tokenKinds "A\nB\r\nC"

              Expect.equal
                  kinds
                  [ Identifier "A"
                    Newline
                    Identifier "B"
                    Newline
                    Identifier "C"
                    Newline
                    Eof ]
                  "mixed" ]

[<Tests>]
let positionTests =
    testList
        "Lexer.Positions"
        [ testCase "first token at line 1 col 1"
          <| fun _ ->
              let tokens = tokenize "Alice"
              let first = tokens.Head
              Expect.equal first.Position.Line 1 "line 1"
              Expect.equal first.Position.Column 1 "col 1"

          testCase "second line position"
          <| fun _ ->
              let tokens = tokenize "A\nB"
              let b = tokens |> List.find (fun t -> t.Kind = Identifier "B")
              Expect.equal b.Position.Line 2 "line 2"
              Expect.equal b.Position.Column 1 "col 1"

          testCase "column advances correctly"
          <| fun _ ->
              let tokens = tokenize "A->B"
              let arrow = tokens |> List.find (fun t -> t.Kind = SolidArrow)
              Expect.equal arrow.Position.Column 2 "arrow at col 2"
              let b = tokens |> List.find (fun t -> t.Kind = Identifier "B")
              Expect.equal b.Position.Column 4 "B at col 4"

          testCase "windows line ending counts as one newline"
          <| fun _ ->
              let tokens = tokenize "A\r\nB"
              let b = tokens |> List.find (fun t -> t.Kind = Identifier "B")
              Expect.equal b.Position.Line 2 "line 2 after CRLF" ]

[<Tests>]
let integrationTests =
    testList
        "Lexer.Integration"
        [ testCase "participant declaration with alias"
          <| fun _ ->
              let kinds = tokenKinds "participant Alice as A"

              Expect.equal
                  kinds
                  [ Participant; Identifier "Alice"; As; Identifier "A"; Newline; Eof ]
                  "participant with alias"

          testCase "note over participant"
          <| fun _ ->
              let kinds = tokenKinds "note over Alice: important"

              Expect.equal
                  kinds
                  [ Note
                    TokenKind.Over
                    Identifier "Alice"
                    Colon
                    TextContent "important"
                    Newline
                    Eof ]
                  "note over"

          testCase "full WSD snippet"
          <| fun _ ->
              let source =
                  "participant Client\nparticipant Server\nClient->Server: request\nServer-->Client: response"

              let kinds = tokenKinds source

              Expect.equal
                  kinds
                  [ Participant
                    Identifier "Client"
                    Newline
                    Participant
                    Identifier "Server"
                    Newline
                    Identifier "Client"
                    SolidArrow
                    Identifier "Server"
                    Colon
                    TextContent "request"
                    Newline
                    Identifier "Server"
                    DashedArrow
                    Identifier "Client"
                    Colon
                    TextContent "response"
                    Newline
                    Eof ]
                  "full snippet"

          testCase "empty input"
          <| fun _ ->
              let kinds = tokenKinds ""
              Expect.equal kinds [ Newline; Eof ] "empty input"

          testCase "alt-else-end block"
          <| fun _ ->
              let kinds = tokenKinds "alt success\nA->B: ok\nelse failure\nA->B: err\nend"

              Expect.equal
                  kinds
                  [ TokenKind.Alt
                    TextContent "success"
                    Newline
                    Identifier "A"
                    SolidArrow
                    Identifier "B"
                    Colon
                    TextContent "ok"
                    Newline
                    Else
                    TextContent "failure"
                    Newline
                    Identifier "A"
                    SolidArrow
                    Identifier "B"
                    Colon
                    TextContent "err"
                    Newline
                    End
                    Newline
                    Eof ]
                  "alt-else-end" ]
