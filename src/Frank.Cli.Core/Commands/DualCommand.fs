/// CLI command for generating client protocol duals and verifying dual consistency.
///
/// frank dual: Generate per-role client obligation annotations from a statechart.
/// frank validate --check-dual: Verify dual consistency (deadlocks, session-complete placement).
/// frank validate --check-laws: Verify algebraic composition laws via FsCheck.
module Frank.Cli.Core.Commands.DualCommand

open FsCheck
open FsCheck.FSharp
open Frank.Resources.Model
open Frank.Statecharts
open Frank.Statecharts.Dual

// ---------------------------------------------------------------------------
// Types for frank dual output
// ---------------------------------------------------------------------------

/// Flattened per-role annotation with state context for CLI output.
type RoleDualAnnotation =
    { State: string
      Descriptor: string
      Obligation: ClientObligation
      AdvancesProtocol: bool
      ChoiceGroupId: int option }

/// Result of running 'frank dual' on a statechart.
type DualExecuteResult =
    {
        /// Per-role flattened annotations keyed by role name.
        RoleDuals: Map<string, RoleDualAnnotation list>
        /// Non-final states where no role can advance the protocol.
        ProtocolSinks: string list
        /// Human-readable summary.
        Summary: string
    }

// ---------------------------------------------------------------------------
// Types for frank validate --check-dual
// ---------------------------------------------------------------------------

type DualIssue = { Severity: string; Message: string }

type CheckDualResult =
    { Issues: DualIssue list
      IsConsistent: bool }

// ---------------------------------------------------------------------------
// Types for frank validate --check-laws
// ---------------------------------------------------------------------------

type LawCheckResult =
    { Category: string
      Name: string
      Passed: bool
      FailureMessage: string option }

type CheckLawsResult =
    { LawResults: LawCheckResult list
      AllPassed: bool }

// ---------------------------------------------------------------------------
// frank dual: execute
// ---------------------------------------------------------------------------

/// Generate client protocol duals from a statechart.
/// Runs projection + dual derivation and flattens to per-role annotations.
let execute (statechart: ExtractedStatechart) : Result<DualExecuteResult, string> =
    let projections = Projection.projectAll statechart
    let deriveResult = derive statechart projections

    let roleDuals =
        deriveResult.Annotations
        |> Map.toList
        |> List.collect (fun ((role, state), annotations) ->
            annotations
            |> List.map (fun ann ->
                role,
                { State = state
                  Descriptor = ann.Descriptor
                  Obligation = ann.Obligation
                  AdvancesProtocol = ann.AdvancesProtocol
                  ChoiceGroupId = ann.ChoiceGroupId }))
        |> List.groupBy fst
        |> List.map (fun (role, pairs) -> role, pairs |> List.map snd)
        |> Map.ofList

    let roleCount = statechart.Roles.Length
    let stateCount = statechart.StateNames.Length
    let sinkCount = deriveResult.ProtocolSinks.Length

    let sinkNote =
        if sinkCount > 0 then
            $"; %d{sinkCount} protocol sink(s) detected"
        else
            ""

    let summary =
        $"Derived client duals for %d{roleCount} role(s) across %d{stateCount} state(s)%s{sinkNote}"

    Ok
        { RoleDuals = roleDuals
          ProtocolSinks = deriveResult.ProtocolSinks
          Summary = summary }

// ---------------------------------------------------------------------------
// frank validate --check-dual
// ---------------------------------------------------------------------------

/// Verify dual consistency of a statechart.
/// Checks: (1) no protocol sinks (deadlocks), (2) session-complete only at final states,
/// (3) every must-select has a corresponding advancing transition.
let checkDual (statechart: ExtractedStatechart) : Result<CheckDualResult, string> =
    let projections = Projection.projectAll statechart
    let deriveResult = derive statechart projections

    let finalStates =
        statechart.StateMetadata
        |> Map.filter (fun _ info -> info.IsFinal)
        |> Map.keys
        |> Set.ofSeq

    let issues = ResizeArray<DualIssue>()

    // Check 1: protocol sinks (non-final states where no role can advance)
    for sink in deriveResult.ProtocolSinks do
        issues.Add(
            { Severity = "error"
              Message = $"Protocol sink (deadlock) at state '%s{sink}': no role can advance the protocol" }
        )

    // Check 2: session-complete only at final states
    for KeyValue((role, state), annotations) in deriveResult.Annotations do
        for ann in annotations do
            if ann.Obligation = SessionComplete && not (Set.contains state finalStates) then
                issues.Add(
                    { Severity = "error"
                      Message =
                        $"session-complete at non-final state '%s{state}' for role '%s{role}' descriptor '%s{ann.Descriptor}'" }
                )

    // Check 3: every must-select has a corresponding advancing transition
    for KeyValue((role, state), annotations) in deriveResult.Annotations do
        for ann in annotations do
            if ann.Obligation = MustSelect && not ann.AdvancesProtocol then
                issues.Add(
                    { Severity = "warning"
                      Message =
                        $"MustSelect obligation on '%s{ann.Descriptor}' in state '%s{state}' for role '%s{role}' does not advance the protocol" }
                )

    let issueList = issues |> Seq.toList

    let hasErrors = issueList |> List.exists (fun i -> i.Severity = "error")

    Ok
        { Issues = issueList
          IsConsistent = not hasErrors }

// ---------------------------------------------------------------------------
// frank validate --check-laws
// ---------------------------------------------------------------------------

/// Run a FsCheck property (as a thunk that throws on failure), returning a LawCheckResult.
let private runProperty (category: string) (name: string) (check: unit -> unit) : LawCheckResult =
    try
        check ()

        { Category = category
          Name = name
          Passed = true
          FailureMessage = None }
    with ex ->
        { Category = category
          Name = name
          Passed = false
          FailureMessage = Some ex.Message }

/// Verify algebraic composition laws via FsCheck.
/// Checks: GuardResult monoid laws (identity, associativity),
/// TransitionResult functor laws (identity, composition).
let checkLaws () : Result<CheckLawsResult, string> =
    let genBlockReason: Gen<BlockReason> =
        let defaultArbs = ArbMap.defaults

        Gen.oneof
            [ gen { return NotAllowed }
              gen { return NotYourTurn }
              gen { return InvalidTransition }
              gen { return PreconditionFailed }
              gen {
                  let! code = (ArbMap.arbitrary<int> defaultArbs).Generator
                  let! msg = (ArbMap.arbitrary<string> defaultArbs).Generator
                  return Custom(code, msg)
              } ]

    let genGuardResult: Gen<GuardResult> =
        Gen.oneof
            [ gen { return Allowed }
              gen {
                  let! reason = genBlockReason
                  return Blocked reason
              } ]

    let arbGuardResult: Arbitrary<GuardResult> = Arb.fromGen genGuardResult

    let results = ResizeArray<LawCheckResult>()

    // GuardResult monoid: left identity
    results.Add(
        runProperty "GuardResult monoid" "left identity" (fun () ->
            Prop.forAll arbGuardResult (fun g -> GuardResult.compose GuardResult.identity g = g)
            |> Check.QuickThrowOnFailure)
    )

    // GuardResult monoid: right identity
    results.Add(
        runProperty "GuardResult monoid" "right identity" (fun () ->
            Prop.forAll arbGuardResult (fun g -> GuardResult.compose g GuardResult.identity = g)
            |> Check.QuickThrowOnFailure)
    )

    // GuardResult monoid: associativity
    results.Add(
        runProperty "GuardResult monoid" "associativity" (fun () ->
            Prop.forAll (Arb.fromGen (Gen.zip3 genGuardResult genGuardResult genGuardResult)) (fun (a, b, c) ->
                let leftAssoc = GuardResult.compose (GuardResult.compose a b) c
                let rightAssoc = GuardResult.compose a (GuardResult.compose b c)
                leftAssoc = rightAssoc)
            |> Check.QuickThrowOnFailure)
    )

    // TransitionResult functor: identity
    results.Add(
        runProperty "TransitionResult functor" "identity" (fun () ->
            let check (s: string, c: int) =
                let original = TransitionResult.Transitioned(s, c)
                let mapped = TransitionResult.map id id original
                mapped = original

            Check.QuickThrowOnFailure check)
    )

    // TransitionResult functor: composition
    results.Add(
        runProperty "TransitionResult functor" "composition" (fun () ->
            let check (s: string, c: int) =
                let f (x: string) = x.Length
                let g (n: int) = n * 2
                let cf (x: int) = x + 1
                let cg (x: int) = x * 3
                let original = TransitionResult.Transitioned(s, c)
                let composed = TransitionResult.map (f >> g) (cf >> cg) original

                let sequential = original |> TransitionResult.map f cf |> TransitionResult.map g cg

                composed = sequential

            Check.QuickThrowOnFailure check)
    )

    let resultList = results |> Seq.toList
    let allPassed = resultList |> List.forall (fun r -> r.Passed)

    Ok
        { LawResults = resultList
          AllPassed = allPassed }
