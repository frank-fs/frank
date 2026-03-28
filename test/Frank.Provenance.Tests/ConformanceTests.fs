module Frank.Provenance.Tests.ConformanceTests

open System
open Expecto
open Frank.Provenance
open Frank.Resources.Model

// -- Test fixtures --

let private mkTransition event source target roleConstraint =
    { Event = event
      Source = source
      Target = target
      Guard = None
      Constraint = roleConstraint }

/// TicTacToe-like statechart with two players and a spectator.
let private ticTacToeChart: ExtractedStatechart =
    { RouteTemplate = "/games/{gameId}"
      StateNames = [ "XTurn"; "OTurn"; "XWins"; "OWins"; "Draw" ]
      InitialStateKey = "XTurn"
      GuardNames = []
      StateMetadata =
        Map.ofList
            [ "XTurn",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "OTurn",
              { AllowedMethods = [ "GET"; "PUT" ]
                IsFinal = false
                Description = None }
              "XWins",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None }
              "OWins",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None }
              "Draw",
              { AllowedMethods = [ "GET" ]
                IsFinal = true
                Description = None } ]
      Roles =
        [ { Name = "PlayerX"
            Description = Some "Player X" }
          { Name = "PlayerO"
            Description = Some "Player O" }
          { Name = "Spectator"
            Description = Some "Observer" } ]
      Transitions =
        [ mkTransition "getGame" "XTurn" "XTurn" Unrestricted
          mkTransition "getGame" "OTurn" "OTurn" Unrestricted
          mkTransition "getGame" "XWins" "XWins" Unrestricted
          mkTransition "getGame" "OWins" "OWins" Unrestricted
          mkTransition "getGame" "Draw" "Draw" Unrestricted
          mkTransition "makeMove" "XTurn" "OTurn" (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "XWins" (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "XTurn" "Draw" (RestrictedTo [ "PlayerX" ])
          mkTransition "makeMove" "OTurn" "XTurn" (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "OWins" (RestrictedTo [ "PlayerO" ])
          mkTransition "makeMove" "OTurn" "Draw" (RestrictedTo [ "PlayerO" ]) ] }

/// Pre-projected per-role profiles from the TicTacToe chart.
let private projections = Projection.projectAll ticTacToeChart

let private now = DateTimeOffset(2026, 3, 23, 12, 0, 0, TimeSpan.Zero)

let private makeRecord (roles: string list) (prevState: string) (newState: string) (event: string) =
    let activity =
        { ProvenanceActivity.Id = $"urn:frank:activity:{Guid.NewGuid()}"
          HttpMethod = "POST"
          ResourceUri = "/games/1"
          EventName = event
          PreviousState = prevState
          NewState = newState
          StartedAt = now
          EndedAt = now }

    let agent =
        { ProvenanceAgent.Id = "urn:frank:agent:person:test"
          AgentType = AgentType.Person("Test", "test") }

    let usedEntity =
        { ProvenanceEntity.Id = $"urn:frank:entity:{Guid.NewGuid()}"
          ResourceUri = "/games/1"
          StateName = prevState
          CapturedAt = now }

    let generatedEntity =
        { ProvenanceEntity.Id = $"urn:frank:entity:{Guid.NewGuid()}"
          ResourceUri = "/games/1"
          StateName = newState
          CapturedAt = now }

    { ProvenanceRecord.Id = $"urn:frank:record:{Guid.NewGuid()}"
      ResourceUri = "/games/1"
      RecordedAt = now
      Activity = activity
      Agent = agent
      GeneratedEntity = generatedEntity
      UsedEntity = usedEntity
      ActingRoles = roles }

[<Tests>]
let conformanceTests =
    testList
        "ConformanceChecker"
        [ test "valid trace produces clean report" {
              let records =
                  [ makeRecord [ "PlayerX" ] "XTurn" "OTurn" "makeMove"
                    makeRecord [ "PlayerO" ] "OTurn" "XTurn" "makeMove"
                    makeRecord [ "PlayerX" ] "XTurn" "XWins" "makeMove" ]

              let report = ConformanceChecker.checkConformance projections records

              Expect.equal report.TotalRecords 3 "TotalRecords"
              Expect.equal report.ConformantCount 3 "ConformantCount"
              Expect.isEmpty report.Violations "No violations"
          }

          test "single violation when role lacks transition" {
              let records = [ makeRecord [ "PlayerO" ] "XTurn" "OTurn" "makeMove" ]

              let report = ConformanceChecker.checkConformance projections records

              Expect.equal report.Violations.Length 1 "One violation"
              let v = report.Violations.[0]
              Expect.equal v.Reasons.Length 1 "One reason"

              match v.Reasons.[0] with
              | ViolationReason.TransitionNotInProjection role -> Expect.equal role "PlayerO" "Violating role"
              | other -> failtest $"Expected TransitionNotInProjection, got {other}"
          }

          test "multi-role pass when at least one role has transition" {
              let records = [ makeRecord [ "Spectator"; "PlayerX" ] "XTurn" "OTurn" "makeMove" ]

              let report = ConformanceChecker.checkConformance projections records

              Expect.equal report.ConformantCount 1 "Conformant"
              Expect.isEmpty report.Violations "No violations — PlayerX authorizes"
          }

          test "multi-role all-fail when no role has transition" {
              let records = [ makeRecord [ "Spectator"; "PlayerO" ] "XTurn" "OTurn" "makeMove" ]

              let report = ConformanceChecker.checkConformance projections records

              Expect.equal report.Violations.Length 1 "One violation"
              let v = report.Violations.[0]
              Expect.equal v.Reasons.Length 2 "Both roles failed"
          }

          test "no acting roles produces NoActingRoles violation" {
              let records = [ makeRecord [] "XTurn" "OTurn" "makeMove" ]

              let report = ConformanceChecker.checkConformance projections records

              Expect.equal report.Violations.Length 1 "One violation"

              match report.Violations.[0].Reasons.[0] with
              | ViolationReason.NoActingRoles -> ()
              | other -> failtest $"Expected NoActingRoles, got {other}"
          }

          test "role not in projection map produces RoleNotInProjection" {
              let records = [ makeRecord [ "UnknownRole" ] "XTurn" "OTurn" "makeMove" ]

              let report = ConformanceChecker.checkConformance projections records

              Expect.equal report.Violations.Length 1 "One violation"

              match report.Violations.[0].Reasons.[0] with
              | ViolationReason.RoleNotInProjection role -> Expect.equal role "UnknownRole" "Unknown role flagged"
              | other -> failtest $"Expected RoleNotInProjection, got {other}"
          }

          test "empty records produces clean empty report" {
              let report = ConformanceChecker.checkConformance projections []

              Expect.equal report.TotalRecords 0 "TotalRecords"
              Expect.equal report.ConformantCount 0 "ConformantCount"
              Expect.isEmpty report.Violations "No violations"
          }

          test "mixed trace reports correct counts" {
              let records =
                  [ makeRecord [ "PlayerX" ] "XTurn" "OTurn" "makeMove" // valid
                    makeRecord [ "PlayerO" ] "XTurn" "OTurn" "makeMove" // invalid
                    makeRecord [ "PlayerO" ] "OTurn" "XTurn" "makeMove" ] // valid

              let report = ConformanceChecker.checkConformance projections records

              Expect.equal report.TotalRecords 3 "TotalRecords"
              Expect.equal report.ConformantCount 2 "ConformantCount"
              Expect.equal report.Violations.Length 1 "One violation"
          }

          test "unrestricted transition passes for all roles" {
              let records =
                  [ makeRecord [ "Spectator" ] "XTurn" "XTurn" "getGame"
                    makeRecord [ "PlayerX" ] "OTurn" "OTurn" "getGame"
                    makeRecord [ "PlayerO" ] "XWins" "XWins" "getGame" ]

              let report = ConformanceChecker.checkConformance projections records

              Expect.equal report.ConformantCount 3 "All conformant"
              Expect.isEmpty report.Violations "No violations for unrestricted transitions"
          } ]

[<Tests>]
let sequenceConformanceTests =
    testList
        "ConformanceChecker.checkSequenceConformance"
        [ test "valid sequence from initial state produces clean report" {
              let records =
                  [ makeRecord [ "PlayerX" ] "XTurn" "OTurn" "makeMove"
                    makeRecord [ "PlayerO" ] "OTurn" "XTurn" "makeMove"
                    makeRecord [ "PlayerX" ] "XTurn" "XWins" "makeMove" ]

              let report = ConformanceChecker.checkSequenceConformance "XTurn" projections records

              Expect.equal report.TotalRecords 3 "TotalRecords"
              Expect.equal report.ConformantCount 3 "ConformantCount"
              Expect.isEmpty report.Violations "No violations"
          }

          test "first transition not from initial state produces violation" {
              let records =
                  [ makeRecord [ "PlayerO" ] "OTurn" "XTurn" "makeMove"
                    makeRecord [ "PlayerX" ] "XTurn" "OTurn" "makeMove" ]

              let report = ConformanceChecker.checkSequenceConformance "XTurn" projections records

              Expect.isGreaterThanOrEqual report.Violations.Length 1 "At least one violation for non-initial start"

              let firstViolation = report.Violations.[0]

              let hasSequenceViolation =
                  firstViolation.Reasons
                  |> List.exists (fun r ->
                      match r with
                      | ViolationReason.StateSequenceViolation _ -> true
                      | _ -> false)

              Expect.isTrue hasSequenceViolation "Should have StateSequenceViolation reason"

              let seqViolation =
                  firstViolation.Reasons
                  |> List.pick (fun r ->
                      match r with
                      | ViolationReason.StateSequenceViolation(expected, actual) -> Some(expected, actual)
                      | _ -> None)

              Expect.equal (fst seqViolation) "XTurn" "Expected initial state"
              Expect.equal (snd seqViolation) "OTurn" "Actual first state"
          }

          test "gap in sequence produces violation" {
              let records =
                  [ makeRecord [ "PlayerX" ] "XTurn" "OTurn" "makeMove"
                    // Gap: previous target was OTurn, but this starts at XWins
                    makeRecord [ "Spectator" ] "XWins" "XWins" "getGame" ]

              let report = ConformanceChecker.checkSequenceConformance "XTurn" projections records

              Expect.isGreaterThanOrEqual report.Violations.Length 1 "At least one violation for gap"

              let gapViolation =
                  report.Violations
                  |> List.find (fun v ->
                      v.Reasons
                      |> List.exists (fun r ->
                          match r with
                          | ViolationReason.StateSequenceViolation _ -> true
                          | _ -> false))

              let seqReason =
                  gapViolation.Reasons
                  |> List.pick (fun r ->
                      match r with
                      | ViolationReason.StateSequenceViolation(expected, actual) -> Some(expected, actual)
                      | _ -> None)

              Expect.equal (fst seqReason) "OTurn" "Expected previous target"
              Expect.equal (snd seqReason) "XWins" "Actual source state"
          }

          test "mid-sequence gap produces violation on second record" {
              // First transition is valid from initial state, but second has wrong source
              let records =
                  [ makeRecord [ "PlayerX" ] "XTurn" "OTurn" "makeMove"
                    // Gap: expected OTurn (previous target), actual XTurn
                    makeRecord [ "PlayerX" ] "XTurn" "XWins" "makeMove"
                    makeRecord [ "PlayerO" ] "OTurn" "XTurn" "makeMove" ]

              let report = ConformanceChecker.checkSequenceConformance "XTurn" projections records

              // Records 2 and 3 should have sequence violations
              Expect.equal report.Violations.Length 2 "Two violations for mid-sequence gaps"

              let secondRecordViolation = report.Violations.[0]

              let seqReason =
                  secondRecordViolation.Reasons
                  |> List.pick (fun r ->
                      match r with
                      | ViolationReason.StateSequenceViolation(expected, actual) -> Some(expected, actual)
                      | _ -> None)

              Expect.equal (fst seqReason) "OTurn" "Expected previous target"
              Expect.equal (snd seqReason) "XTurn" "Actual source state"
          }

          test "empty records produces clean empty report" {
              let report = ConformanceChecker.checkSequenceConformance "XTurn" projections []

              Expect.equal report.TotalRecords 0 "TotalRecords"
              Expect.isEmpty report.Violations "No violations"
          }

          test "sequence violations combine with role violations" {
              // Wrong initial state AND unknown role — should produce both violation types
              let records = [ makeRecord [ "Admin" ] "OTurn" "XTurn" "makeMove" ]

              let report = ConformanceChecker.checkSequenceConformance "XTurn" projections records

              Expect.equal report.Violations.Length 1 "One violation record"
              let v = report.Violations.[0]

              let hasSequence =
                  v.Reasons
                  |> List.exists (fun r ->
                      match r with
                      | ViolationReason.StateSequenceViolation _ -> true
                      | _ -> false)

              let hasRoleViolation =
                  v.Reasons
                  |> List.exists (fun r ->
                      match r with
                      | ViolationReason.RoleNotInProjection _ -> true
                      | _ -> false)

              Expect.isTrue hasSequence "Should have StateSequenceViolation"
              Expect.isTrue hasRoleViolation "Should have RoleNotInProjection"
          }

          test "single-record valid sequence from initial state" {
              let records = [ makeRecord [ "PlayerX" ] "XTurn" "OTurn" "makeMove" ]

              let report = ConformanceChecker.checkSequenceConformance "XTurn" projections records

              Expect.equal report.ConformantCount 1 "Conformant"
              Expect.isEmpty report.Violations "No violations"
          } ]
