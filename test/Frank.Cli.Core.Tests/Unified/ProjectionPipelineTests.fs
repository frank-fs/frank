module Frank.Cli.Core.Tests.Unified.ProjectionPipelineTests

open Expecto
open Frank.Resources.Model
open Frank.Cli.Core.Unified.ProjectionPipeline

// ══════════════════════════════════════════════════════════════════════════════
// Shared fixtures
// ══════════════════════════════════════════════════════════════════════════════

let private ticTacToeTransitions =
    [ // PlayerX moves from XTurn
      { Event = "MakeMove"
        Source = "XTurn"
        Target = "OTurn"
        Guard = Some "TurnGuard"
        Constraint = RestrictedTo [ "PlayerX" ]
        Safety = Unsafe }
      { Event = "MakeMove"
        Source = "XTurn"
        Target = "Won"
        Guard = Some "TurnGuard"
        Constraint = RestrictedTo [ "PlayerX" ]
        Safety = Unsafe }
      // PlayerO moves from OTurn
      { Event = "MakeMove"
        Source = "OTurn"
        Target = "XTurn"
        Guard = Some "TurnGuard"
        Constraint = RestrictedTo [ "PlayerO" ]
        Safety = Unsafe }
      { Event = "MakeMove"
        Source = "OTurn"
        Target = "Won"
        Guard = Some "TurnGuard"
        Constraint = RestrictedTo [ "PlayerO" ]
        Safety = Unsafe } ]

let private capabilities: HttpCapability list =
    [ { Method = "GET"
        StateKey = Some "XTurn"
        LinkRelation = "self"
        IsSafe = true }
      { Method = "POST"
        StateKey = Some "XTurn"
        LinkRelation = "makeMove"
        IsSafe = false }
      { Method = "GET"
        StateKey = Some "OTurn"
        LinkRelation = "self"
        IsSafe = true }
      { Method = "POST"
        StateKey = Some "OTurn"
        LinkRelation = "makeMove"
        IsSafe = false }
      { Method = "GET"
        StateKey = Some "Won"
        LinkRelation = "self"
        IsSafe = true } ]

// ══════════════════════════════════════════════════════════════════════════════
// Tests
// ══════════════════════════════════════════════════════════════════════════════

[<Tests>]
let projectionPipelineTests =
    testList
        "ProjectionPipeline"
        [ testList
              "filterCapabilitiesByTransitions"
              [ testCase "PlayerX: keeps POST in XTurn, removes POST in OTurn"
                <| fun _ ->
                    let playerXTransitions =
                        ticTacToeTransitions
                        |> List.filter (fun t ->
                            match t.Constraint with
                            | RestrictedTo roles -> List.contains "PlayerX" roles
                            | Unrestricted -> true)

                    let filtered = filterCapabilitiesByTransitions playerXTransitions capabilities

                    let xTurnPost =
                        filtered
                        |> List.tryFind (fun c -> c.Method = "POST" && c.StateKey = Some "XTurn")

                    Expect.isSome xTurnPost "PlayerX should keep POST in XTurn"

                    let oTurnPost =
                        filtered
                        |> List.tryFind (fun c -> c.Method = "POST" && c.StateKey = Some "OTurn")

                    Expect.isNone oTurnPost "PlayerX should lose POST in OTurn"

                testCase "PlayerO: keeps POST in OTurn, removes POST in XTurn"
                <| fun _ ->
                    let playerOTransitions =
                        ticTacToeTransitions
                        |> List.filter (fun t ->
                            match t.Constraint with
                            | RestrictedTo roles -> List.contains "PlayerO" roles
                            | Unrestricted -> true)

                    let filtered = filterCapabilitiesByTransitions playerOTransitions capabilities

                    let oTurnPost =
                        filtered
                        |> List.tryFind (fun c -> c.Method = "POST" && c.StateKey = Some "OTurn")

                    Expect.isSome oTurnPost "PlayerO should keep POST in OTurn"

                    let xTurnPost =
                        filtered
                        |> List.tryFind (fun c -> c.Method = "POST" && c.StateKey = Some "XTurn")

                    Expect.isNone xTurnPost "PlayerO should lose POST in XTurn"

                testCase "Spectator: no unsafe transitions removes all POST"
                <| fun _ ->
                    let spectatorTransitions: TransitionSpec list = []

                    let filtered = filterCapabilitiesByTransitions spectatorTransitions capabilities

                    let postCaps = filtered |> List.filter (fun c -> c.Method = "POST")
                    Expect.isEmpty postCaps "Spectator should have no POST capabilities"

                testCase "Spectator: all GET capabilities survive"
                <| fun _ ->
                    let spectatorTransitions: TransitionSpec list = []

                    let filtered = filterCapabilitiesByTransitions spectatorTransitions capabilities

                    let getCaps = filtered |> List.filter (fun c -> c.Method = "GET")
                    Expect.equal getCaps.Length 3 "Spectator should keep all 3 GET capabilities"

                testCase "capabilities without StateKey always kept"
                <| fun _ ->
                    let capsWithGlobal =
                        capabilities
                        @ [ { Method = "POST"
                              StateKey = None
                              LinkRelation = "create"
                              IsSafe = false } ]

                    let filtered = filterCapabilitiesByTransitions [] capsWithGlobal

                    let globalPost =
                        filtered
                        |> List.tryFind (fun c -> c.Method = "POST" && c.StateKey = None)

                    Expect.isSome globalPost "Unscoped POST should survive even with no transitions"

                testCase "self-loop transitions do not grant unsafe access"
                <| fun _ ->
                    let selfLoopTransitions =
                        [ { Event = "Refresh"
                            Source = "XTurn"
                            Target = "XTurn"
                            Guard = None
                            Constraint = Unrestricted
                            Safety = Unsafe } ]

                    let filtered = filterCapabilitiesByTransitions selfLoopTransitions capabilities

                    let xTurnPost =
                        filtered
                        |> List.tryFind (fun c -> c.Method = "POST" && c.StateKey = Some "XTurn")

                    Expect.isNone xTurnPost "Self-loop should not grant POST access" ]

          testList
              "filterCapabilitiesByStates"
              [ testCase "removes capabilities for pruned states"
                <| fun _ ->
                    let filtered = filterCapabilitiesByStates [ "XTurn"; "Won" ] capabilities

                    let oTurnCaps =
                        filtered |> List.filter (fun c -> c.StateKey = Some "OTurn")

                    Expect.isEmpty oTurnCaps "OTurn capabilities should be removed"

                    let xTurnCaps =
                        filtered |> List.filter (fun c -> c.StateKey = Some "XTurn")

                    Expect.equal xTurnCaps.Length 2 "XTurn capabilities should survive"

                testCase "capabilities without StateKey survive pruning"
                <| fun _ ->
                    let capsWithGlobal =
                        [ { Method = "OPTIONS"
                            StateKey = None
                            LinkRelation = "self"
                            IsSafe = true } ]

                    let filtered = filterCapabilitiesByStates [ "XTurn" ] capsWithGlobal
                    Expect.equal filtered.Length 1 "Global capability should survive" ]

          testList
              "roleSlug"
              [ testCase "produces lowercase slug"
                <| fun _ ->
                    let slug = roleSlug "games" "PlayerX"
                    Expect.equal slug "games-playerx" "Should be lowercase"

                testCase "handles single-word role"
                <| fun _ ->
                    let slug = roleSlug "games" "Spectator"
                    Expect.equal slug "games-spectator" "Should be lowercase" ] ]
