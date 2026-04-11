module HierarchyAlgebraTests

open Expecto
open Frank.Statecharts
open Frank.Statecharts.Ast

// ============================================================================
// State identifiers (redefined locally — HierarchyTests module is separate)
// ============================================================================

[<RequireQualifiedAccess>]
module private TL =
    let root = "Root"
    let active = "Active"
    let red = "Red"
    let yellow = "Yellow"
    let green = "Green"
    let off = "Off"

[<RequireQualifiedAccess>]
module private Dev =
    let root = "DevRoot"
    let standby = "Standby"
    let device = "Device"
    let display = "Display"
    let screenOn = "ScreenOn"
    let screenOff = "ScreenOff"
    let network = "Network"
    let connected = "Connected"
    let disconnected = "Disconnected"

[<RequireQualifiedAccess>]
module private Nested =
    let root = "NRoot"
    let outer = "Outer"
    let inner = "Inner"
    let a = "A"
    let b = "B"
    let c = "C"
    let off = "NOff"

// ============================================================================
// Shared hierarchy builders
// ============================================================================

/// Root(XOR) → [Active(XOR) → [Red, Yellow, Green], Off]
let private trafficLightHierarchy =
    StateHierarchy.build
        { States =
            [ { Id = TL.root
                Kind = CompositeKind.XOR
                Children = [ TL.active; TL.off ]
                InitialChild = Some TL.active
                CompletionTarget = None }
              { Id = TL.active
                Kind = CompositeKind.XOR
                Children = [ TL.red; TL.yellow; TL.green ]
                InitialChild = Some TL.red
                CompletionTarget = None } ] }

/// DevRoot(XOR) → [Standby, Device(AND) → [Display(XOR) → [ScreenOn, ScreenOff],
///                                          Network(XOR) → [Connected, Disconnected]]]
/// Root wraps Device so transition INTO Device exercises AND-state entry semantics.
let private deviceHierarchy =
    StateHierarchy.build
        { States =
            [ { Id = Dev.root
                Kind = CompositeKind.XOR
                Children = [ Dev.standby; Dev.device ]
                InitialChild = Some Dev.standby
                CompletionTarget = None }
              { Id = Dev.device
                Kind = CompositeKind.AND
                Children = [ Dev.display; Dev.network ]
                InitialChild = None
                CompletionTarget = None }
              { Id = Dev.display
                Kind = CompositeKind.XOR
                Children = [ Dev.screenOn; Dev.screenOff ]
                InitialChild = Some Dev.screenOn
                CompletionTarget = None }
              { Id = Dev.network
                Kind = CompositeKind.XOR
                Children = [ Dev.connected; Dev.disconnected ]
                InitialChild = Some Dev.connected
                CompletionTarget = None } ] }

/// NRoot(XOR) → [Outer(XOR) → [Inner(XOR) → [A, B], C], NOff]
/// Three-level nesting to differentiate shallow vs deep history recovery.
let private nestedHierarchy =
    StateHierarchy.build
        { States =
            [ { Id = Nested.root
                Kind = CompositeKind.XOR
                Children = [ Nested.outer; Nested.off ]
                InitialChild = Some Nested.outer
                CompletionTarget = None }
              { Id = Nested.outer
                Kind = CompositeKind.XOR
                Children = [ Nested.inner; Nested.c ]
                InitialChild = Some Nested.inner
                CompletionTarget = None }
              { Id = Nested.inner
                Kind = CompositeKind.XOR
                Children = [ Nested.a; Nested.b ]
                InitialChild = Some Nested.a
                CompletionTarget = None } ] }

// ============================================================================
// Equivalence tests: transition function vs RuntimeInterpreter programs
// ============================================================================

// Hand-written programs — intentionally uses raw alg.Bind cascades to validate
// interpreter semantics directly, independent of TransitionProgram.fromTransition.
// Deep nesting is inherent to the algebra's CPS syntax; the builder exists to avoid
// it in production code. See fromTransitionTests below for builder equivalence.
[<Tests>]
let algebraEquivalenceTests =
    testList
        "TransitionAlgebra RuntimeInterpreter"
        [ testCase "XOR transition (Red → Green) matches transition"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

              let history = HistoryRecord.empty

              let expected =
                  HierarchicalRuntime.transition trafficLightHierarchy config TL.red TL.green history

              let program (alg: TransitionAlgebra<'r>) : 'r =
                  alg.Bind
                      (alg.RecordHistory TL.active)
                      (fun () -> alg.Bind (alg.Exit TL.red) (fun () -> alg.Enter TL.green))

              let actual =
                  HierarchicalRuntime.runProgram trafficLightHierarchy config history program

              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet expected.Configuration)
                  "Configuration"

              Expect.equal actual.ExitedStates expected.ExitedStates "ExitedStates"
              Expect.equal actual.EnteredStates expected.EnteredStates "EnteredStates"

          testCase "cross-composite transition (Red → Off) matches transition"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

              let history = HistoryRecord.empty

              let expected =
                  HierarchicalRuntime.transition trafficLightHierarchy config TL.red TL.off history

              // RecordHistory for LCA (Root) and each composite in exit path (Active)
              // before any Exit calls — history must see the original config.
              let program (alg: TransitionAlgebra<'r>) : 'r =
                  alg.Bind
                      (alg.RecordHistory TL.root)
                      (fun () ->
                          alg.Bind
                              (alg.RecordHistory TL.active)
                              (fun () ->
                                  alg.Bind
                                      (alg.Exit TL.red)
                                      (fun () -> alg.Bind (alg.Exit TL.active) (fun () -> alg.Enter TL.off))))

              let actual =
                  HierarchicalRuntime.runProgram trafficLightHierarchy config history program

              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet expected.Configuration)
                  "Configuration"

              Expect.equal actual.ExitedStates expected.ExitedStates "ExitedStates"
              Expect.equal actual.EnteredStates expected.EnteredStates "EnteredStates"

          testCase "AND-state entry (Standby → Device) matches transition"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState deviceHierarchy Dev.standby ActiveStateConfiguration.empty

              let history = HistoryRecord.empty

              let expected =
                  HierarchicalRuntime.transition deviceHierarchy config Dev.standby Dev.device history

              let program (alg: TransitionAlgebra<'r>) : 'r =
                  alg.Bind
                      (alg.RecordHistory Dev.root)
                      (fun () ->
                          alg.Bind
                              (alg.Exit Dev.standby)
                              (fun () ->
                                  alg.Bind
                                      (alg.Enter Dev.device)
                                      (fun () -> alg.Fork [ Dev.display; Dev.network ])))

              let actual =
                  HierarchicalRuntime.runProgram deviceHierarchy config history program

              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet expected.Configuration)
                  "Configuration"

              Expect.equal actual.ExitedStates expected.ExitedStates "ExitedStates"
              Expect.equal actual.EnteredStates expected.EnteredStates "EnteredStates"

          testCase "self-transition (Red → Red) matches transition"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

              let history = HistoryRecord.empty

              let expected =
                  HierarchicalRuntime.transition trafficLightHierarchy config TL.red TL.red history

              let program (alg: TransitionAlgebra<'r>) : 'r =
                  alg.Bind (alg.Exit TL.red) (fun () -> alg.Enter TL.red)

              let actual =
                  HierarchicalRuntime.runProgram trafficLightHierarchy config history program

              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet expected.Configuration)
                  "Configuration"

              Expect.equal actual.ExitedStates expected.ExitedStates "ExitedStates"
              Expect.equal actual.EnteredStates expected.EnteredStates "EnteredStates"

          testCase "shallow history recovery matches enterWithHistory"
          <| fun () ->
              // Setup: enter Active (→ Red), transition Red → Green, exit to Off
              let config0 =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

              let redToGreen =
                  HierarchicalRuntime.transition trafficLightHierarchy config0 TL.red TL.green HistoryRecord.empty

              let greenToOff =
                  HierarchicalRuntime.transition
                      trafficLightHierarchy
                      redToGreen.Configuration
                      TL.green
                      TL.off
                      redToGreen.HistoryRecord

              // Expected: re-enter Active with shallow history
              let expectedConfig =
                  HierarchicalRuntime.enterWithHistory
                      trafficLightHierarchy
                      HistoryKind.Shallow
                      TL.active
                      greenToOff.Configuration
                      greenToOff.HistoryRecord

              // Actual: use RestoreHistory program
              let program (alg: TransitionAlgebra<'r>) : 'r =
                  alg.RestoreHistory(TL.active, HistoryKind.Shallow)

              let actual =
                  HierarchicalRuntime.runProgram
                      trafficLightHierarchy
                      greenToOff.Configuration
                      greenToOff.HistoryRecord
                      program

              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet expectedConfig)
                  "Configuration matches enterWithHistory"

          testCase "deep history recovery restores nested leaf"
          <| fun () ->
              // Enter Outer (→Inner→A), transition A→B, then B→Off
              let config0 =
                  HierarchicalRuntime.enterState nestedHierarchy Nested.outer ActiveStateConfiguration.empty

              let aToB =
                  HierarchicalRuntime.transition nestedHierarchy config0 Nested.a Nested.b HistoryRecord.empty

              let bToOff =
                  HierarchicalRuntime.transition
                      nestedHierarchy
                      aToB.Configuration
                      Nested.b
                      Nested.off
                      aToB.HistoryRecord

              // Deep history: restores Outer → Inner → B (the actual last leaf)
              let expectedConfig =
                  HierarchicalRuntime.enterWithHistory
                      nestedHierarchy
                      HistoryKind.Deep
                      Nested.outer
                      bToOff.Configuration
                      bToOff.HistoryRecord

              let program (alg: TransitionAlgebra<'r>) : 'r =
                  alg.RestoreHistory(Nested.outer, HistoryKind.Deep)

              let actual =
                  HierarchicalRuntime.runProgram
                      nestedHierarchy
                      bToOff.Configuration
                      bToOff.HistoryRecord
                      program

              // Deep should restore B, not fall back to initial A
              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet expectedConfig)
                  "Configuration matches enterWithHistory (deep)"

              Expect.isTrue
                  (ActiveStateConfiguration.isActive Nested.b actual.Configuration)
                  "B is active (deep restores actual leaf, not initial A)"

          testCase "history fallback to initial child when no history recorded"
          <| fun () ->
              // Start in Off — no history for Active exists
              let config =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.off ActiveStateConfiguration.empty

              let history = HistoryRecord.empty

              let expectedConfig =
                  HierarchicalRuntime.enterWithHistory
                      trafficLightHierarchy
                      HistoryKind.Shallow
                      TL.active
                      config
                      history

              let program (alg: TransitionAlgebra<'r>) : 'r =
                  alg.RestoreHistory(TL.active, HistoryKind.Shallow)

              let actual =
                  HierarchicalRuntime.runProgram trafficLightHierarchy config history program

              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet expectedConfig)
                  "Configuration matches enterWithHistory (no history)"

              // Fallback: enters initial child Red
              Expect.isTrue
                  (ActiveStateConfiguration.isActive TL.red actual.Configuration)
                  "Red is active (fallback to initial child)"

          testCase "AND-state exit (ScreenOn → Standby) matches transition"
          <| fun () ->
              // Enter Device — all regions active
              let config =
                  HierarchicalRuntime.enterState deviceHierarchy Dev.device ActiveStateConfiguration.empty

              let history = HistoryRecord.empty

              let expected =
                  HierarchicalRuntime.transition deviceHierarchy config Dev.screenOn Dev.standby history

              // RecordHistory for LCA (DevRoot) and composites in exit path (Display, Device)
              // before any Exit calls.
              let program (alg: TransitionAlgebra<'r>) : 'r =
                  alg.Bind
                      (alg.RecordHistory Dev.root)
                      (fun () ->
                          alg.Bind
                              (alg.RecordHistory Dev.display)
                              (fun () ->
                                  alg.Bind
                                      (alg.RecordHistory Dev.device)
                                      (fun () ->
                                          alg.Bind
                                              (alg.Exit Dev.screenOn)
                                              (fun () ->
                                                  alg.Bind
                                                      (alg.Exit Dev.display)
                                                      (fun () ->
                                                          alg.Bind
                                                              (alg.Exit Dev.device)
                                                              (fun () -> alg.Enter Dev.standby))))))

              let actual =
                  HierarchicalRuntime.runProgram deviceHierarchy config history program

              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet expected.Configuration)
                  "Configuration"

              Expect.equal actual.ExitedStates expected.ExitedStates "ExitedStates"
              Expect.equal actual.EnteredStates expected.EnteredStates "EnteredStates"

          testCase "Return produces identity (no state change)"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

              let history = HistoryRecord.empty

              let program (alg: TransitionAlgebra<'r>) : 'r = alg.Return()

              let actual =
                  HierarchicalRuntime.runProgram trafficLightHierarchy config history program

              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet config)
                  "Configuration unchanged"

              Expect.equal actual.ExitedStates [] "No states exited"
              Expect.equal actual.EnteredStates [] "No states entered"

          testCase "transition to XOR composite (Off → Active) auto-enters initial child"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.off ActiveStateConfiguration.empty

              let history = HistoryRecord.empty

              let expected =
                  HierarchicalRuntime.transition trafficLightHierarchy config TL.off TL.active history

              let program (alg: TransitionAlgebra<'r>) : 'r =
                  alg.Bind
                      (alg.RecordHistory TL.root)
                      (fun () -> alg.Bind (alg.Exit TL.off) (fun () -> alg.Enter TL.active))

              let actual =
                  HierarchicalRuntime.runProgram trafficLightHierarchy config history program

              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet expected.Configuration)
                  "Configuration"

              Expect.equal actual.ExitedStates expected.ExitedStates "ExitedStates"
              Expect.equal actual.EnteredStates expected.EnteredStates "EnteredStates"

              // Active's initial child is Red — enterState should activate it
              Expect.isTrue
                  (ActiveStateConfiguration.isActive TL.red actual.Configuration)
                  "Red is active (initial child of Active)"

          testCase "history recording: multi-exit records match transition"
          <| fun () ->
              // Green→Off exits Green then Active. RecordHistory for LCA (Root)
              // and composite in exit path (Active) must happen before any Exit
              // to capture the original config.
              let config0 =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

              let toGreen =
                  HierarchicalRuntime.transition
                      trafficLightHierarchy
                      config0
                      TL.red
                      TL.green
                      HistoryRecord.empty

              let expected =
                  HierarchicalRuntime.transition
                      trafficLightHierarchy
                      toGreen.Configuration
                      TL.green
                      TL.off
                      toGreen.HistoryRecord

              let program (alg: TransitionAlgebra<'r>) : 'r =
                  alg.Bind
                      (alg.RecordHistory TL.root)
                      (fun () ->
                          alg.Bind
                              (alg.RecordHistory TL.active)
                              (fun () ->
                                  alg.Bind
                                      (alg.Exit TL.green)
                                      (fun () -> alg.Bind (alg.Exit TL.active) (fun () -> alg.Enter TL.off))))

              let actual =
                  HierarchicalRuntime.runProgram
                      trafficLightHierarchy
                      toGreen.Configuration
                      toGreen.HistoryRecord
                      program

              // Config/Exited/Entered should match regardless of history recording
              Expect.equal
                  (ActiveStateConfiguration.toSet actual.Configuration)
                  (ActiveStateConfiguration.toSet expected.Configuration)
                  "Configuration"

              Expect.equal actual.ExitedStates expected.ExitedStates "ExitedStates"
              Expect.equal actual.EnteredStates expected.EnteredStates "EnteredStates"

              // History fidelity: compare what was recorded for Active.
              // transition uses original config (Active has Green); interpreter's Exit
              // uses progressive config (Green already exited, so Active has nothing).
              let expectedActiveHistory =
                  HistoryRecord.tryGet TL.active expected.HistoryRecord
                  |> Option.map ActiveStateConfiguration.toSet

              let actualActiveHistory =
                  HistoryRecord.tryGet TL.active actual.HistoryRecord
                  |> Option.map ActiveStateConfiguration.toSet

              Expect.equal actualActiveHistory expectedActiveHistory "Active history recording" ]

// ============================================================================
// TransitionProgram.fromTransition builder tests
// ============================================================================

/// Helper: assert full equivalence (Config, Exited, Entered, History) between
/// transition and a program built by fromTransition.
let private assertFullEquivalence hierarchy config history source target testLabel =
    let expected =
        HierarchicalRuntime.transition hierarchy config source target history

    let program = TransitionProgram.fromTransition hierarchy source target

    let actual =
        HierarchicalRuntime.runProgram hierarchy config history program

    Expect.equal
        (ActiveStateConfiguration.toSet actual.Configuration)
        (ActiveStateConfiguration.toSet expected.Configuration)
        (sprintf "%s: Configuration" testLabel)

    Expect.equal actual.ExitedStates expected.ExitedStates (sprintf "%s: ExitedStates" testLabel)
    Expect.equal actual.EnteredStates expected.EnteredStates (sprintf "%s: EnteredStates" testLabel)

    // Full history fidelity — compare all entries
    let expectedHistory = HistoryRecord.toMap expected.HistoryRecord
    let actualHistory = HistoryRecord.toMap actual.HistoryRecord

    let allKeys = Set.union (Map.keys expectedHistory |> Set.ofSeq) (Map.keys actualHistory |> Set.ofSeq)

    for key in allKeys do
        let exp = Map.tryFind key expectedHistory |> Option.map ActiveStateConfiguration.toSet
        let act = Map.tryFind key actualHistory |> Option.map ActiveStateConfiguration.toSet
        Expect.equal act exp (sprintf "%s: HistoryRecord[%s]" testLabel key)

[<Tests>]
let fromTransitionTests =
    testList
        "TransitionProgram.fromTransition"
        [ testCase "XOR sibling (Red → Green)"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

              assertFullEquivalence trafficLightHierarchy config HistoryRecord.empty TL.red TL.green "Red→Green"

          testCase "cross-composite (Red → Off)"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

              assertFullEquivalence trafficLightHierarchy config HistoryRecord.empty TL.red TL.off "Red→Off"

          testCase "AND-state entry (Standby → Device)"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState deviceHierarchy Dev.standby ActiveStateConfiguration.empty

              assertFullEquivalence deviceHierarchy config HistoryRecord.empty Dev.standby Dev.device "Standby→Device"

          testCase "self-transition (Red → Red)"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

              assertFullEquivalence trafficLightHierarchy config HistoryRecord.empty TL.red TL.red "Red→Red"

          testCase "AND-state exit (ScreenOn → Standby)"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState deviceHierarchy Dev.device ActiveStateConfiguration.empty

              assertFullEquivalence deviceHierarchy config HistoryRecord.empty Dev.screenOn Dev.standby "ScreenOn→Standby"

          testCase "transition to XOR composite (Off → Active)"
          <| fun () ->
              let config =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.off ActiveStateConfiguration.empty

              assertFullEquivalence trafficLightHierarchy config HistoryRecord.empty TL.off TL.active "Off→Active"

          testCase "multi-exit with history fidelity (Green → Off)"
          <| fun () ->
              let config0 =
                  HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

              let toGreen =
                  HierarchicalRuntime.transition
                      trafficLightHierarchy
                      config0
                      TL.red
                      TL.green
                      HistoryRecord.empty

              assertFullEquivalence
                  trafficLightHierarchy
                  toGreen.Configuration
                  toGreen.HistoryRecord
                  TL.green
                  TL.off
                  "Green→Off (with prior history)"

          testCase "nested cross-composite (B → NOff)"
          <| fun () ->
              let config0 =
                  HierarchicalRuntime.enterState nestedHierarchy Nested.outer ActiveStateConfiguration.empty

              let aToB =
                  HierarchicalRuntime.transition nestedHierarchy config0 Nested.a Nested.b HistoryRecord.empty

              assertFullEquivalence
                  nestedHierarchy
                  aToB.Configuration
                  aToB.HistoryRecord
                  Nested.b
                  Nested.off
                  "B→NOff (3-level exit)" ]

// ============================================================================
// Monad law tests for RuntimeInterpreter
// Establishes the algebraic contract that future interpreters must satisfy.
// ============================================================================

/// Run a RuntimeStep on a concrete config/history and return the 4-tuple.
let private runStep
    (step: RuntimeStep)
    (config: ActiveStateConfiguration)
    (history: HistoryRecord)
    =
    step (config, history)

[<Tests>]
let monadLawTests =
    let alg = HierarchicalRuntime.createInterpreter trafficLightHierarchy

    let config =
        HierarchicalRuntime.enterState trafficLightHierarchy TL.active ActiveStateConfiguration.empty

    let history = HistoryRecord.empty

    // A concrete operation for law testing
    let f () = alg.Exit TL.red

    testList
        "RuntimeInterpreter monad laws"
        [ testCase "left identity: Bind(Zero(), fun () -> f()) = f()"
          <| fun () ->
              let lhs = alg.Bind (alg.Zero()) f
              let rhs = f ()

              let (c1, h1, ex1, en1) = runStep lhs config history
              let (c2, h2, ex2, en2) = runStep rhs config history

              Expect.equal (ActiveStateConfiguration.toSet c1) (ActiveStateConfiguration.toSet c2) "Config"
              Expect.equal (HistoryRecord.toMap h1) (HistoryRecord.toMap h2) "History"
              Expect.equal ex1 ex2 "ExitedStates"
              Expect.equal en1 en2 "EnteredStates"

          testCase "right identity: Bind(f(), fun () -> Zero()) = f()"
          <| fun () ->
              let lhs = alg.Bind (f ()) (fun () -> alg.Zero())
              let rhs = f ()

              let (c1, h1, ex1, en1) = runStep lhs config history
              let (c2, h2, ex2, en2) = runStep rhs config history

              Expect.equal (ActiveStateConfiguration.toSet c1) (ActiveStateConfiguration.toSet c2) "Config"
              Expect.equal (HistoryRecord.toMap h1) (HistoryRecord.toMap h2) "History"
              Expect.equal ex1 ex2 "ExitedStates"
              Expect.equal en1 en2 "EnteredStates"

          testCase "associativity: Bind(Bind(a, b), c) = Bind(a, fun () -> Bind(b(), c))"
          <| fun () ->
              let a = alg.RecordHistory TL.active
              let b () = alg.Exit TL.red
              let c () = alg.Enter TL.green

              let lhs = alg.Bind (alg.Bind a b) c
              let rhs = alg.Bind a (fun () -> alg.Bind (b ()) c)

              let (c1, h1, ex1, en1) = runStep lhs config history
              let (c2, h2, ex2, en2) = runStep rhs config history

              Expect.equal (ActiveStateConfiguration.toSet c1) (ActiveStateConfiguration.toSet c2) "Config"
              Expect.equal (HistoryRecord.toMap h1) (HistoryRecord.toMap h2) "History"
              Expect.equal ex1 ex2 "ExitedStates"
              Expect.equal en1 en2 "EnteredStates" ]
