module Frank.Statecharts.Tests.Scxml.ErrorTests

open Expecto
open Frank.Statecharts.Scxml.Types
open Frank.Statecharts.Scxml.Parser

[<Tests>]
let errorTests =
    testList
        "Scxml.Errors"
        [
          // === Malformed XML tests ===

          testCase "malformed XML: unclosed tag"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml"><state id="s1">"""

              let result = parseString xml
              Expect.isNone result.Document "document should be None"
              Expect.isNonEmpty result.Errors "should have errors"
              let err = result.Errors.[0]
              Expect.isTrue (err.Description.Length > 0) "error should have description"
              Expect.isSome err.Position "error should have position"

          testCase "malformed XML: invalid entity reference"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml">&invalid;</scxml>"""

              let result = parseString xml
              Expect.isNone result.Document "document should be None"
              Expect.isNonEmpty result.Errors "should have errors"

          testCase "malformed XML: empty string"
          <| fun _ ->
              let result = parseString ""
              Expect.isNone result.Document "document should be None"
              Expect.isNonEmpty result.Errors "should have errors"

          testCase "malformed XML: missing closing tag"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml"><state id="s1"/>"""

              let result = parseString xml
              Expect.isNone result.Document "document should be None"
              Expect.isNonEmpty result.Errors "should have errors"
              let err = result.Errors.[0]
              Expect.isSome err.Position "error should have position"

          testCase "error position has line and column"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml"><state id="s1">"""

              let result = parseString xml
              let pos = result.Errors.[0].Position.Value
              Expect.isGreaterThan pos.Line 0 "line should be positive"
              Expect.isGreaterThan pos.Column 0 "column should be positive"

          // === Structural validation tests ===

          testCase "non-scxml root element produces error"
          <| fun _ ->
              let xml = """<notscxml/>"""
              let result = parseString xml
              Expect.isNone result.Document "document should be None"
              Expect.isNonEmpty result.Errors "should have errors"
              Expect.isTrue
                  (result.Errors.[0].Description.Contains("scxml"))
                  "error should mention scxml"

          // === Warning tests ===

          testCase "unknown element inside state produces warning"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <unknown/>
  </state>
</scxml>"""

              let result = parseString xml
              Expect.isSome result.Document "should still parse successfully"
              Expect.isNonEmpty result.Warnings "should have warnings"
              Expect.isTrue
                  (result.Warnings.[0].Description.Contains("unknown"))
                  "warning should mention unknown element"

          testCase "unknown element inside scxml root produces warning"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1"/>
  <custom/>
</scxml>"""

              let result = parseString xml
              Expect.isSome result.Document "should still parse successfully"
              Expect.isNonEmpty result.Warnings "should have warnings"
              Expect.isTrue
                  (result.Warnings.[0].Description.Contains("custom"))
                  "warning should mention custom element"

          testCase "out-of-scope elements do not produce warnings"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <onentry/>
    <onexit/>
  </state>
</scxml>"""

              let result = parseString xml
              Expect.isSome result.Document "should parse successfully"
              Expect.isEmpty result.Warnings "no warnings for out-of-scope elements"

          testCase "invalid history type value produces warning"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="s1">
  <state id="s1">
    <history id="h1" type="invalid"/>
  </state>
</scxml>"""

              let result = parseString xml
              Expect.isSome result.Document "should still parse"
              Expect.isNonEmpty result.Warnings "should have warnings"
              Expect.isTrue
                  (result.Warnings.[0].Description.Contains("invalid"))
                  "warning should mention invalid type"
              // Should default to Shallow
              let h = result.Document.Value.States.[0].HistoryNodes.[0]
              Expect.equal h.Kind Shallow "defaults to Shallow"

          testCase "valid document has no errors and no warnings"
          <| fun _ ->
              let xml =
                  """<scxml xmlns="http://www.w3.org/2005/07/scxml" initial="idle">
  <state id="idle">
    <transition event="go" target="active"/>
  </state>
  <state id="active"/>
</scxml>"""

              let result = parseString xml
              Expect.isSome result.Document "should parse"
              Expect.isEmpty result.Errors "no errors"
              Expect.isEmpty result.Warnings "no warnings" ]
