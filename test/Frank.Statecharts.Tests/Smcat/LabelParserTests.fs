module Frank.Statecharts.Tests.Smcat.LabelParserTests

open Expecto
open Frank.Statecharts.Ast
open Frank.Statecharts.Smcat.Types
open Frank.Statecharts.Smcat.LabelParser

let private pos : SourcePosition = { Line = 1; Column = 1 }

[<Tests>]
let labelTests =
    testList
        "Smcat.LabelParser"
        [ testCase "event only"
          <| fun _ ->
              let (label, warnings) = parseLabel "start" pos
              Expect.equal label.Event (Some "start") "event"
              Expect.equal label.Guard None "no guard"
              Expect.equal label.Action None "no action"
              Expect.isEmpty warnings "no warnings"

          testCase "guard only"
          <| fun _ ->
              let (label, warnings) = parseLabel "[isReady]" pos
              Expect.equal label.Event None "no event"
              Expect.equal label.Guard (Some "isReady") "guard"
              Expect.equal label.Action None "no action"
              Expect.isEmpty warnings "no warnings"

          testCase "action only"
          <| fun _ ->
              let (label, warnings) = parseLabel "/ doSomething" pos
              Expect.equal label.Event None "no event"
              Expect.equal label.Guard None "no guard"
              Expect.equal label.Action (Some "doSomething") "action"
              Expect.isEmpty warnings "no warnings"

          testCase "event and guard"
          <| fun _ ->
              let (label, warnings) = parseLabel "start [isReady]" pos
              Expect.equal label.Event (Some "start") "event"
              Expect.equal label.Guard (Some "isReady") "guard"
              Expect.equal label.Action None "no action"
              Expect.isEmpty warnings "no warnings"

          testCase "event and action"
          <| fun _ ->
              let (label, warnings) = parseLabel "start / doSomething" pos
              Expect.equal label.Event (Some "start") "event"
              Expect.equal label.Guard None "no guard"
              Expect.equal label.Action (Some "doSomething") "action"
              Expect.isEmpty warnings "no warnings"

          testCase "all three components"
          <| fun _ ->
              let (label, warnings) = parseLabel "start [isReady] / doSomething" pos
              Expect.equal label.Event (Some "start") "event"
              Expect.equal label.Guard (Some "isReady") "guard"
              Expect.equal label.Action (Some "doSomething") "action"
              Expect.isEmpty warnings "no warnings"

          testCase "empty label"
          <| fun _ ->
              let (label, warnings) = parseLabel "" pos
              Expect.equal label.Event None "no event"
              Expect.equal label.Guard None "no guard"
              Expect.equal label.Action None "no action"
              Expect.isEmpty warnings "no warnings"

          testCase "whitespace only label"
          <| fun _ ->
              let (label, warnings) = parseLabel "   " pos
              Expect.equal label.Event None "no event"
              Expect.equal label.Guard None "no guard"
              Expect.equal label.Action None "no action"
              Expect.isEmpty warnings "no warnings"

          testCase "guard with spaces"
          <| fun _ ->
              let (label, _) = parseLabel "event [has spaces inside]" pos
              Expect.equal label.Guard (Some "has spaces inside") "guard with spaces"

          testCase "action with spaces"
          <| fun _ ->
              let (label, _) = parseLabel "event / do the thing" pos
              Expect.equal label.Action (Some "do the thing") "action with spaces"

          testCase "unclosed bracket produces warning"
          <| fun _ ->
              let (label, warnings) = parseLabel "event [guard" pos
              Expect.equal label.Guard (Some "guard") "guard text extracted"
              Expect.equal warnings.Length 1 "one warning"
              Expect.stringContains warnings[0].Description "Unclosed" "warning mentions unclosed"

          testCase "guard then action"
          <| fun _ ->
              let (label, _) = parseLabel "[ready] / act" pos
              Expect.equal label.Event None "no event"
              Expect.equal label.Guard (Some "ready") "guard"
              Expect.equal label.Action (Some "act") "action"

          testCase "collectCustomerData with guard and action"
          <| fun _ ->
              let (label, _) = parseLabel "collectCustomerData [isValid] / logAction" pos
              Expect.equal label.Event (Some "collectCustomerData") "event"
              Expect.equal label.Guard (Some "isValid") "guard"
              Expect.equal label.Action (Some "logAction") "action" ]
