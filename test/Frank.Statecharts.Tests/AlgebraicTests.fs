module Frank.Statecharts.Tests.AlgebraicTests

open Expecto
open FsCheck
open Frank.Statecharts

// ---------------------------------------------------------------------------
// Part 2: Algebraic Composition Tests
// ---------------------------------------------------------------------------

// ===========================================================================
// TransitionResult.map and TransitionResult.bind
// ===========================================================================

[<Tests>]
let transitionResultMapTests =
    testList
        "TransitionResult.map"
        [ testCase "map over Transitioned applies function to state and context"
          <| fun _ ->
              let result = TransitionResult.Transitioned("XTurn", 42)
              let mapped = TransitionResult.map (fun s -> s + "!") (fun c -> c * 2) result
              Expect.equal mapped (TransitionResult.Transitioned("XTurn!", 84)) "maps state and context"

          testCase "map over Blocked preserves BlockReason"
          <| fun _ ->
              let result = TransitionResult.Blocked NotYourTurn
              let mapped = TransitionResult.map (fun (s: string) -> s + "!") (fun (c: int) -> c * 2) result
              Expect.equal mapped (TransitionResult.Blocked NotYourTurn) "Blocked preserved"

          testCase "map over Invalid preserves message"
          <| fun _ ->
              let result = TransitionResult.Invalid "bad move"
              let mapped = TransitionResult.map (fun (s: string) -> s + "!") (fun (c: int) -> c * 2) result
              Expect.equal mapped (TransitionResult.Invalid "bad move") "Invalid preserved" ]

[<Tests>]
let transitionResultBindTests =
    testList
        "TransitionResult.bind"
        [ testCase "bind over Transitioned applies function"
          <| fun _ ->
              let result = TransitionResult.Transitioned("XTurn", 42)

              let bound =
                  TransitionResult.bind (fun s c -> TransitionResult.Transitioned(s + "!", c * 2)) result

              Expect.equal bound (TransitionResult.Transitioned("XTurn!", 84)) "bind applies function"

          testCase "bind over Blocked short-circuits"
          <| fun _ ->
              let result: TransitionResult<string, int> = TransitionResult.Blocked NotAllowed
              let mutable called = false

              let bound =
                  TransitionResult.bind
                      (fun s c ->
                          called <- true
                          TransitionResult.Transitioned(s, c))
                      result

              Expect.equal bound (TransitionResult.Blocked NotAllowed) "Blocked short-circuits"
              Expect.isFalse called "function not called on Blocked"

          testCase "bind over Invalid short-circuits"
          <| fun _ ->
              let result: TransitionResult<string, int> = TransitionResult.Invalid "oops"
              let mutable called = false

              let bound =
                  TransitionResult.bind
                      (fun s c ->
                          called <- true
                          TransitionResult.Transitioned(s, c))
                      result

              Expect.equal bound (TransitionResult.Invalid "oops") "Invalid short-circuits"
              Expect.isFalse called "function not called on Invalid"

          testCase "bind chains two transitions"
          <| fun _ ->
              let step1 = TransitionResult.Transitioned("Step1", 1)

              let step2 =
                  step1
                  |> TransitionResult.bind (fun _ c -> TransitionResult.Transitioned("Step2", c + 1))

              let step3 =
                  step2
                  |> TransitionResult.bind (fun _ c -> TransitionResult.Transitioned("Step3", c + 1))

              Expect.equal step3 (TransitionResult.Transitioned("Step3", 3)) "chained bind works"

          testCase "bind chain short-circuits at first Blocked"
          <| fun _ ->
              let step1 = TransitionResult.Transitioned("Step1", 1)

              let step2 =
                  step1 |> TransitionResult.bind (fun _ _ -> TransitionResult.Blocked NotYourTurn)

              let step3 =
                  step2
                  |> TransitionResult.bind (fun _ c -> TransitionResult.Transitioned("Step3", c + 1))

              Expect.equal step3 (TransitionResult.Blocked NotYourTurn) "chain short-circuits at Blocked" ]

// ===========================================================================
// GuardResult algebra
// ===========================================================================

[<Tests>]
let guardResultAlgebraTests =
    testList
        "GuardResult algebra"
        [ testCase "identity guard always allows"
          <| fun _ ->
              let result = GuardResult.identity
              Expect.equal result Allowed "identity is Allowed"

          testCase "compose two Allowed yields Allowed"
          <| fun _ ->
              let result = GuardResult.compose Allowed Allowed
              Expect.equal result Allowed "Allowed AND Allowed = Allowed"

          testCase "compose Allowed and Blocked yields Blocked"
          <| fun _ ->
              let result = GuardResult.compose Allowed (Blocked NotAllowed)
              Expect.equal result (Blocked NotAllowed) "Allowed AND Blocked = Blocked"

          testCase "compose Blocked and Allowed yields Blocked"
          <| fun _ ->
              let result = GuardResult.compose (Blocked NotYourTurn) Allowed
              Expect.equal result (Blocked NotYourTurn) "Blocked AND Allowed = Blocked"

          testCase "compose two Blocked yields first Blocked"
          <| fun _ ->
              let result = GuardResult.compose (Blocked NotYourTurn) (Blocked NotAllowed)
              Expect.equal result (Blocked NotYourTurn) "first Blocked wins"

          testCase "alternative two Allowed yields Allowed"
          <| fun _ ->
              let result = GuardResult.alternative Allowed Allowed
              Expect.equal result Allowed "Allowed OR Allowed = Allowed"

          testCase "alternative Allowed and Blocked yields Allowed"
          <| fun _ ->
              let result = GuardResult.alternative Allowed (Blocked NotAllowed)
              Expect.equal result Allowed "Allowed OR Blocked = Allowed"

          testCase "alternative Blocked and Allowed yields Allowed"
          <| fun _ ->
              let result = GuardResult.alternative (Blocked NotYourTurn) Allowed
              Expect.equal result Allowed "Blocked OR Allowed = Allowed"

          testCase "alternative two Blocked yields second Blocked"
          <| fun _ ->
              let result = GuardResult.alternative (Blocked NotYourTurn) (Blocked NotAllowed)
              Expect.equal result (Blocked NotAllowed) "both Blocked: second wins" ]

// ===========================================================================
// Property-based tests: Functor laws for TransitionResult.map
// ===========================================================================

[<Tests>]
let transitionResultFunctorLaws =
    testList
        "TransitionResult.map functor laws"
        [ testCase "map id = id (Transitioned)"
          <| fun _ ->
              let check (s: string, c: int) =
                  let original = TransitionResult.Transitioned(s, c)
                  let mapped = TransitionResult.map id id original
                  mapped = original

              Check.QuickThrowOnFailure check

          testCase "map id = id (Blocked)"
          <| fun _ ->
              let result: TransitionResult<string, int> = TransitionResult.Blocked NotAllowed
              let mapped = TransitionResult.map id id result
              Expect.equal mapped result "map id on Blocked = id"

          testCase "map id = id (Invalid)"
          <| fun _ ->
              let check (msg: string) =
                  let original: TransitionResult<string, int> = TransitionResult.Invalid msg
                  let mapped = TransitionResult.map id id original
                  mapped = original

              Check.QuickThrowOnFailure check

          testCase "map (f >> g) = map f >> map g (composition law)"
          <| fun _ ->
              let f (s: string) = s.Length
              let g (n: int) = n * 2
              let cf (c: int) = c + 1
              let cg (c: int) = c * 3

              let check (s: string, c: int) =
                  let original = TransitionResult.Transitioned(s, c)
                  let composed = TransitionResult.map (f >> g) (cf >> cg) original
                  let sequential = original |> TransitionResult.map f cf |> TransitionResult.map g cg
                  composed = sequential

              Check.QuickThrowOnFailure check ]

// ===========================================================================
// Property-based tests: Monoid laws for GuardResult.compose
// ===========================================================================

[<Tests>]
let guardResultMonoidLaws =
    testList
        "GuardResult.compose monoid laws"
        [ testCase "compose identity g = g (left identity)"
          <| fun _ ->
              Expect.equal (GuardResult.compose GuardResult.identity Allowed) Allowed "identity compose Allowed"

              Expect.equal
                  (GuardResult.compose GuardResult.identity (Blocked NotAllowed))
                  (Blocked NotAllowed)
                  "identity compose Blocked"

          testCase "compose g identity = g (right identity)"
          <| fun _ ->
              Expect.equal (GuardResult.compose Allowed GuardResult.identity) Allowed "Allowed compose identity"

              Expect.equal
                  (GuardResult.compose (Blocked NotAllowed) GuardResult.identity)
                  (Blocked NotAllowed)
                  "Blocked compose identity"

          testCase "compose is associative"
          <| fun _ ->
              // (a . b) . c = a . (b . c)
              let a = Allowed
              let b = Blocked NotYourTurn
              let c = Allowed

              let leftAssoc = GuardResult.compose (GuardResult.compose a b) c
              let rightAssoc = GuardResult.compose a (GuardResult.compose b c)

              Expect.equal leftAssoc rightAssoc "compose is associative"

          testCase "compose associativity with all Blocked"
          <| fun _ ->
              let a = Blocked NotAllowed
              let b = Blocked NotYourTurn
              let c = Blocked InvalidTransition

              let leftAssoc = GuardResult.compose (GuardResult.compose a b) c
              let rightAssoc = GuardResult.compose a (GuardResult.compose b c)

              Expect.equal leftAssoc rightAssoc "compose associative with Blocked values" ]

// ===========================================================================
// Guard composition with Order Fulfillment fixture
// ===========================================================================

[<Tests>]
let guardCompositionOrderFulfillmentTests =
    testList
        "Guard composition with Order Fulfillment fixture"
        [ testCase "compose BuyerGuard AND SellerGuard for Confirmed state"
          <| fun _ ->
              // In Confirmed state, both Buyer and Seller can act.
              // Composing their guard results: both Allowed = Allowed
              let buyerResult = Allowed
              let sellerResult = Allowed
              let composed = GuardResult.compose buyerResult sellerResult
              Expect.equal composed Allowed "both guards passing yields Allowed"

          testCase "compose BuyerGuard blocked AND SellerGuard allowed"
          <| fun _ ->
              // In Confirmed state, if buyer's guard blocks but seller's allows
              let buyerResult = Blocked NotYourTurn
              let sellerResult = Allowed
              let composed = GuardResult.compose buyerResult sellerResult
              Expect.equal composed (Blocked NotYourTurn) "composition short-circuits on first Blocked"

          testCase "alternative BuyerGuard OR SellerGuard in Confirmed"
          <| fun _ ->
              // Alternative: either guard passing is sufficient
              let buyerResult = Blocked NotYourTurn
              let sellerResult = Allowed
              let alt = GuardResult.alternative buyerResult sellerResult
              Expect.equal alt Allowed "alternative allows when one passes"

          testCase "multi-guard composition: BuyerGuard AND SellerGuard AND WarehouseGuard"
          <| fun _ ->
              // Simulating all three role guards being checked
              let results = [ Allowed; Blocked NotYourTurn; Allowed ]

              let composed =
                  results
                  |> List.fold GuardResult.compose GuardResult.identity

              Expect.equal composed (Blocked NotYourTurn) "fold compose with identity works" ]
