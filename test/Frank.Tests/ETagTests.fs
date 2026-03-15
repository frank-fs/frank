module Frank.Tests.ETagTests

open System.Text
open Expecto
open Frank

[<Tests>]
let etagFormatTests =
    testList
        "ETagFormat"
        [ test "quote wraps in double quotes" {
              let result = ETagFormat.quote "abc123"
              Expect.equal result "\"abc123\"" "Should wrap value in double quotes"
          }

          test "unquote extracts from quoted string" {
              let result = ETagFormat.unquote "\"abc123\""
              Expect.equal result (Some "abc123") "Should extract inner value"
          }

          test "unquote returns None for unquoted string" {
              let result = ETagFormat.unquote "abc123"
              Expect.equal result None "Should return None for unquoted input"
          }

          test "unquote returns None for weak ETag" {
              let result = ETagFormat.unquote "W/\"abc123\""
              Expect.equal result None "Should return None for weak ETag"
          }

          test "isWeak detects weak ETags" {
              Expect.isTrue (ETagFormat.isWeak "W/\"abc123\"") "Should detect W/\"...\" as weak"
              Expect.isFalse (ETagFormat.isWeak "\"abc123\"") "Strong ETag should not be weak"
              Expect.isFalse (ETagFormat.isWeak null) "null should not be weak"
          }

          test "isStrong detects strong ETags" {
              Expect.isTrue (ETagFormat.isStrong "\"abc123\"") "Quoted string should be strong"
              Expect.isFalse (ETagFormat.isStrong "W/\"abc123\"") "Weak ETag should not be strong"
          }

          test "computeFromBytes produces consistent hex output" {
              let data = Encoding.UTF8.GetBytes("hello world")
              let result1 = ETagFormat.computeFromBytes data
              let result2 = ETagFormat.computeFromBytes data
              Expect.equal result1 result2 "Same input should produce same output"
              Expect.equal result1.Length 32 "Should be 32 hex chars (128 bits)"

              Expect.isTrue
                  (result1 |> Seq.forall (fun c -> "0123456789abcdef".Contains(string c)))
                  "Should be lowercase hex"
          }

          test "computeFromBytes produces different output for different input" {
              let data1 = Encoding.UTF8.GetBytes("hello")
              let data2 = Encoding.UTF8.GetBytes("world")
              let result1 = ETagFormat.computeFromBytes data1
              let result2 = ETagFormat.computeFromBytes data2
              Expect.notEqual result1 result2 "Different input should produce different output"
          } ]

[<Tests>]
let etagComparisonTests =
    testList
        "ETagComparison"
        [ test "strongMatch: matching strong ETags returns true" {
              let result = ETagComparison.strongMatch "\"abc\"" "\"abc\""
              Expect.isTrue result "Identical strong ETags should match"
          }

          test "strongMatch: weak vs strong returns false" {
              let result = ETagComparison.strongMatch "W/\"abc\"" "\"abc\""
              Expect.isFalse result "Weak vs strong should not match"
          }

          test "strongMatch: different strong ETags returns false" {
              let result = ETagComparison.strongMatch "\"abc\"" "\"def\""
              Expect.isFalse result "Different strong ETags should not match"
          }

          test "parseIfNoneMatch splits comma-separated values" {
              let result = ETagComparison.parseIfNoneMatch "\"a\", \"b\", \"c\""
              Expect.equal result [ "\"a\""; "\"b\""; "\"c\"" ] "Should split and trim values"
          }

          test "parseIfNoneMatch returns empty list for null" {
              let result = ETagComparison.parseIfNoneMatch null
              Expect.isEmpty result "null should produce empty list"
          }

          test "parseIfNoneMatch returns empty list for whitespace" {
              let result = ETagComparison.parseIfNoneMatch "   "
              Expect.isEmpty result "Whitespace should produce empty list"
          }

          test "anyMatch with wildcard * returns true when currentETag is Some" {
              let result = ETagComparison.anyMatch (Some "\"abc\"") "*"
              Expect.isTrue result "Wildcard should match any Some ETag"
          }

          test "anyMatch with None currentETag returns false" {
              let result = ETagComparison.anyMatch None "*"
              Expect.isFalse result "None currentETag should always return false"
          }

          test "anyMatch with None currentETag returns false even for specific values" {
              let result = ETagComparison.anyMatch None "\"abc\""
              Expect.isFalse result "None currentETag should always return false"
          }

          test "anyMatch with matching ETag in list returns true" {
              let result = ETagComparison.anyMatch (Some "\"b\"") "\"a\", \"b\", \"c\""
              Expect.isTrue result "Should find matching ETag in comma-separated list"
          }

          test "anyMatch with no matching ETag in list returns false" {
              let result = ETagComparison.anyMatch (Some "\"d\"") "\"a\", \"b\", \"c\""
              Expect.isFalse result "Should not match when ETag is not in list"
          } ]
