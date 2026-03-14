module Frank.Statecharts.Tests.Wsd.GuardParserTests

open Expecto
open Frank.Statecharts.Wsd.Types
open Frank.Statecharts.Wsd.GuardParser

let private pos line col : SourcePosition = { Line = line; Column = col }

[<Tests>]
let guardParserTests =
    testList
        "GuardParser"
        [
          // === No guard cases ===
          testCase "no guard: plain text returns None"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "This is plain text" (pos 1 1)

              Expect.isNone guard "no guard"
              Expect.equal remaining "This is plain text" "content unchanged"
              Expect.isEmpty errors "no errors"
              Expect.isEmpty warnings "no warnings"

          testCase "no guard: empty string returns None"
          <| fun _ ->
              let (guard, remaining, errors, warnings) = tryParseGuard "" (pos 1 1)
              Expect.isNone guard "no guard"
              Expect.equal remaining "" "content unchanged"
              Expect.isEmpty errors "no errors"
              Expect.isEmpty warnings "no warnings"

          testCase "no guard: unrelated brackets"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "[some other thing]" (pos 1 1)

              Expect.isNone guard "no guard"
              Expect.equal remaining "[some other thing]" "content unchanged"
              Expect.isEmpty errors "no errors"
              Expect.isEmpty warnings "no warnings"

          testCase "no guard: guard not at start"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "Some text [guard: role=admin]" (pos 1 1)

              Expect.isNone guard "no guard when not at start"
              Expect.equal remaining "Some text [guard: role=admin]" "content unchanged"
              Expect.isEmpty errors "no errors"
              Expect.isEmpty warnings "no warnings"

          // === Simple guard cases ===
          testCase "US2-S1: single guard pair"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "[guard: role=PlayerX]" (pos 5 20)

              Expect.isSome guard "guard found"
              Expect.equal guard.Value.Pairs [ ("role", "PlayerX") ] "one pair"
              Expect.equal remaining "" "no remaining"
              Expect.isEmpty errors "no errors"
              Expect.isEmpty warnings "no warnings"

          testCase "US2-S2: two guard pairs in order"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "[guard: state=XTurn, role=PlayerX]" (pos 1 1)

              Expect.isSome guard "guard found"
              Expect.equal guard.Value.Pairs [ ("state", "XTurn"); ("role", "PlayerX") ] "two pairs in order"
              Expect.equal remaining "" "no remaining"
              Expect.isEmpty errors "no errors"
              Expect.isEmpty warnings "no warnings"

          testCase "single pair with various key names"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "[guard: my-key_1=some-value]" (pos 1 1)

              Expect.isSome guard "guard found"
              Expect.equal guard.Value.Pairs [ ("my-key_1", "some-value") ] "pair extracted"
              Expect.isEmpty errors "no errors"
              Expect.isEmpty warnings "no warnings"

          testCase "three pairs"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "[guard: a=1, b=2, c=3]" (pos 1 1)

              Expect.isSome guard "guard found"
              Expect.equal guard.Value.Pairs [ ("a", "1"); ("b", "2"); ("c", "3") ] "three pairs"
              Expect.isEmpty errors "no errors"
              Expect.isEmpty warnings "no warnings"

          // === Mixed content ===
          testCase "guard with remaining text"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "[guard: role=admin] Must be admin" (pos 1 1)

              Expect.isSome guard "guard found"
              Expect.equal guard.Value.Pairs [ ("role", "admin") ] "pair extracted"
              Expect.equal remaining "Must be admin" "remaining text preserved"
              Expect.isEmpty errors "no errors"
              Expect.isEmpty warnings "no warnings"

          testCase "guard with only whitespace remaining"
          <| fun _ ->
              let (guard, remaining, errors, warnings) = tryParseGuard "[guard: x=y]  " (pos 1 1)

              Expect.isSome guard "guard found"
              Expect.equal guard.Value.Pairs [ ("x", "y") ] "pair extracted"
              Expect.equal remaining "" "whitespace remaining fully trimmed"

          testCase "US2-S3: regular note no guard"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "This is a regular note" (pos 1 1)

              Expect.isNone guard "no guard"
              Expect.equal remaining "This is a regular note" "content unchanged"

          // === Whitespace handling ===
          testCase "leading whitespace before guard"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "   [guard: key=val]" (pos 1 1)

              Expect.isSome guard "guard found despite leading spaces"
              Expect.equal guard.Value.Pairs [ ("key", "val") ] "pair extracted"

          testCase "spaces around key and value"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "[guard:  key = val ]" (pos 1 1)

              Expect.isSome guard "guard found"
              Expect.equal guard.Value.Pairs [ ("key", "val") ] "trimmed key and value"

          // === Case insensitivity ===
          testCase "case insensitive: Guard"
          <| fun _ ->
              let (guard, _, errors, _) = tryParseGuard "[Guard: role=admin]" (pos 1 1)
              Expect.isSome guard "guard found with [Guard:"
              Expect.isEmpty errors "no errors"

          testCase "case insensitive: GUARD"
          <| fun _ ->
              let (guard, _, errors, _) = tryParseGuard "[GUARD: role=admin]" (pos 1 1)
              Expect.isSome guard "guard found with [GUARD:"
              Expect.isEmpty errors "no errors"

          testCase "case insensitive: mixed case"
          <| fun _ ->
              let (guard, _, errors, _) = tryParseGuard "[GuArD: role=admin]" (pos 1 1)
              Expect.isSome guard "guard found with mixed case"
              Expect.isEmpty errors "no errors"

          // === Error cases ===
          testCase "US2-S4: unclosed bracket"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "[guard: malformed" (pos 3 10)

              Expect.isNone guard "no guard on unclosed bracket"
              Expect.equal remaining "[guard: malformed" "content unchanged"
              Expect.hasLength errors 1 "one error"
              let err = List.head errors
              Expect.equal err.Description "Unclosed guard annotation bracket" "error description"
              Expect.equal err.Position (pos 3 10) "error at base position"

          testCase "empty key: =value"
          <| fun _ ->
              let (guard, remaining, errors, warnings) = tryParseGuard "[guard: =value]" (pos 1 1)

              Expect.isSome guard "guard still returned"
              Expect.isEmpty guard.Value.Pairs "no valid pairs"
              Expect.hasLength errors 1 "one error"
              Expect.equal (List.head errors).Description "Empty key in guard annotation" "error description"

          testCase "missing equals: key without value"
          <| fun _ ->
              let (guard, remaining, errors, warnings) = tryParseGuard "[guard: key]" (pos 1 1)

              Expect.isSome guard "guard returned"
              Expect.isEmpty guard.Value.Pairs "no valid pairs"
              Expect.hasLength errors 1 "one error"
              Expect.equal (List.head errors).Description "Missing '=' in guard pair" "error description"

          testCase "empty value: key="
          <| fun _ ->
              let (guard, remaining, errors, warnings) = tryParseGuard "[guard: key=]" (pos 1 1)

              Expect.isSome guard "guard returned"
              Expect.equal guard.Value.Pairs [ ("key", "") ] "pair with empty value"
              Expect.isEmpty errors "no errors"
              Expect.hasLength warnings 1 "one warning"
              Expect.equal (List.head warnings).Description "Empty value in guard annotation" "warning description"

          testCase "empty guard annotation"
          <| fun _ ->
              let (guard, remaining, errors, warnings) = tryParseGuard "[guard: ]" (pos 1 1)

              Expect.isSome guard "guard returned"
              Expect.isEmpty guard.Value.Pairs "no pairs"
              Expect.isEmpty errors "no errors"
              Expect.hasLength warnings 1 "one warning"
              Expect.equal (List.head warnings).Description "Empty guard annotation" "warning description"

          testCase "multiple errors in one guard"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "[guard: =bad, key, good=ok]" (pos 1 1)

              Expect.isSome guard "guard returned"
              Expect.equal guard.Value.Pairs [ ("good", "ok") ] "only valid pair extracted"
              Expect.hasLength errors 2 "two errors"

              Expect.isTrue
                  (errors |> List.exists (fun e -> e.Description = "Empty key in guard annotation"))
                  "has empty key error"

              Expect.isTrue
                  (errors |> List.exists (fun e -> e.Description = "Missing '=' in guard pair"))
                  "has missing equals error"

          // === Position tracking ===
          testCase "position is preserved from input"
          <| fun _ ->
              let (guard, _, _, _) = tryParseGuard "[guard: x=y]" (pos 10 30)
              Expect.isSome guard "guard found"
              Expect.equal guard.Value.Position (pos 10 30) "position matches input"

          testCase "position with leading whitespace offset"
          <| fun _ ->
              let (guard, _, _, _) = tryParseGuard "  [guard: x=y]" (pos 1 1)
              Expect.isSome guard "guard found"
              Expect.equal guard.Value.Position.Column 3 "column offset by leading spaces"

          // === Multiple guards ===
          testCase "multiple guards - only first extracted"
          <| fun _ ->
              let (guard, remaining, errors, warnings) =
                  tryParseGuard "[guard: a=1] [guard: b=2] extra" (pos 1 1)

              Expect.isSome guard "first guard found"
              Expect.equal guard.Value.Pairs [ ("a", "1") ] "first guard pairs"
              Expect.equal remaining "[guard: b=2] extra" "second guard is in remaining"
              Expect.isEmpty errors "no errors"
              Expect.isEmpty warnings "no warnings" ]
