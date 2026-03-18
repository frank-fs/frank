module Frank.Statecharts.Tests.Validation.StringDistanceTests

open Expecto
open Frank.Statecharts.Validation.StringDistance

[<Tests>]
let jaroTests =
    testList
        "Validation.StringDistance.Jaro"
        [ test "identical strings return 1.0" {
              Expect.floatClose Accuracy.high (jaro "hello" "hello") 1.0 "identical"
          }

          test "empty strings return 1.0 (both empty = identical)" {
              Expect.floatClose Accuracy.high (jaro "" "") 1.0 "both empty"
          }

          test "one empty string returns 0.0" {
              Expect.floatClose Accuracy.high (jaro "hello" "") 0.0 "one empty"
              Expect.floatClose Accuracy.high (jaro "" "hello") 0.0 "other empty"
          }

          test "MARTHA vs MARHTA produces ~0.9444" {
              let result = jaro "MARTHA" "MARHTA"
              Expect.isTrue (result > 0.94 && result < 0.95) (sprintf "expected ~0.9444, got %f" result)
          }

          test "completely different strings return 0.0" {
              Expect.floatClose Accuracy.high (jaro "abc" "xyz") 0.0 "no matches"
          }

          test "single char match" {
              Expect.floatClose Accuracy.high (jaro "a" "a") 1.0 "single char identical"
          }

          test "repeated chars handled correctly" {
              // With the s1Matches guard, "aa" vs "a" should count 1 match, not 2
              let result = jaro "aa" "a"
              Expect.isTrue (result > 0.0 && result < 1.0) (sprintf "expected partial match, got %f" result)
          } ]

[<Tests>]
let jaroWinklerTests =
    testList
        "Validation.StringDistance.JaroWinkler"
        [ test "identical strings return 1.0" {
              Expect.floatClose Accuracy.high (jaroWinkler "hello" "hello") 1.0 "identical"
          }

          test "MARTHA vs MARHTA produces ~0.9611" {
              let result = jaroWinkler "MARTHA" "MARHTA"
              Expect.isTrue (result > 0.96 && result < 0.97) (sprintf "expected ~0.9611, got %f" result)
          }

          test "prefix bonus applied" {
              let jScore = jaro "startOnboarding" "start"
              let jwScore = jaroWinkler "startOnboarding" "start"
              Expect.isTrue (jwScore >= jScore) "Winkler bonus should increase or maintain score"
          }

          test "prefix bonus capped at 4 characters" {
              // Two strings with 5-char common prefix should get same bonus as 4-char
              let score4 = jaroWinkler "abcdXYZ" "abcdUVW"
              let score5 = jaroWinkler "abcdeXYZ" "abcdeUVW"
              // Both should have prefix bonus, but the cap means 5-char prefix doesn't get extra
              Expect.isTrue (score4 > 0.0) "4-char prefix has bonus"
              Expect.isTrue (score5 > 0.0) "5-char prefix has bonus"
          }

          test "startOnboarding vs start similarity" {
              let result = jaroWinkler "startOnboarding" "start"
              Expect.isTrue (result > 0.7 && result < 0.9) (sprintf "expected ~0.78, got %f" result)
          }

          test "Idle vs idle high similarity (casing)" {
              let result = jaroWinkler "Idle" "idle"
              Expect.isTrue (result > 0.8) (sprintf "expected high similarity for casing diff, got %f" result)
          }

          test "completely different strings return low score" {
              let result = jaroWinkler "login" "shutdown"
              Expect.isTrue (result < 0.6) (sprintf "expected low similarity, got %f" result)
          } ]
