module TypeTests

open Expecto
open Frank.Statecharts
open System.Security.Claims

type TurnstileState =
    | Locked
    | Unlocked

type TurnstileEvent =
    | Coin
    | Push

let transition state event (_ctx: unit) =
    match state, event with
    | Locked, Coin -> TransitionResult.Transitioned(Unlocked, ())
    | Unlocked, Push -> TransitionResult.Transitioned(Locked, ())
    | Locked, Push -> TransitionResult.Blocked(BlockReason.InvalidTransition)
    | Unlocked, Coin -> TransitionResult.Transitioned(Unlocked, ())

let turnstileMachine: StateMachine<TurnstileState, TurnstileEvent, unit> =
    { Initial = Locked
      InitialContext = ()
      Transition = transition
      Guards = []
      StateMetadata =
        Map.ofList
            [ Locked,
              { AllowedMethods = [ "POST" ]
                IsFinal = false
                Description = Some "Waiting for coin" }
              Unlocked,
              { AllowedMethods = [ "POST" ]
                IsFinal = false
                Description = Some "Ready to push" } ] }

[<Tests>]
let blockReasonTests =
    testList
        "BlockReason"
        [ test "Custom carries code and message" {
              let reason = Custom(429, "Rate limited")

              match reason with
              | Custom(code, msg) ->
                  Expect.equal code 429 "code"
                  Expect.equal msg "Rate limited" "message"
              | _ -> failtest "Expected Custom"
          }

          test "NotAllowed can be constructed and matched" {
              let reason = NotAllowed

              match reason with
              | NotAllowed -> ()
              | _ -> failtest "Expected NotAllowed"
          }

          test "NotYourTurn can be constructed and matched" {
              let reason = NotYourTurn

              match reason with
              | NotYourTurn -> ()
              | _ -> failtest "Expected NotYourTurn"
          } ]

[<Tests>]
let guardResultTests =
    testList
        "GuardResult"
        [ test "Allowed can be constructed" {
              let result = Allowed

              match result with
              | Allowed -> ()
              | _ -> failtest "Expected Allowed"
          }

          test "Blocked carries BlockReason" {
              let result = Blocked(NotYourTurn)

              match result with
              | Blocked reason -> Expect.equal reason NotYourTurn "reason"
              | _ -> failtest "Expected Blocked"
          } ]

[<Tests>]
let transitionResultTests =
    testList
        "TransitionResult"
        [ test "Transitioned carries state and context" {
              let result = TransitionResult.Transitioned(Unlocked, ())

              match result with
              | TransitionResult.Transitioned(state, ctx) ->
                  Expect.equal state Unlocked "state"
                  Expect.equal ctx () "context"
              | _ -> failtest "Expected Transitioned"
          }

          test "Blocked carries BlockReason" {
              let result = TransitionResult.Blocked(BlockReason.InvalidTransition)

              match result with
              | TransitionResult.Blocked reason -> Expect.equal reason BlockReason.InvalidTransition "reason"
              | _ -> failtest "Expected Blocked"
          }

          test "Invalid carries message" {
              let result = TransitionResult.Invalid("bad move")

              match result with
              | TransitionResult.Invalid msg -> Expect.equal msg "bad move" "message"
              | _ -> failtest "Expected Invalid"
          } ]

[<Tests>]
let stateMachineTests =
    testList
        "StateMachine"
        [ test "transition Locked + Coin = Unlocked" {
              match turnstileMachine.Transition Locked Coin () with
              | TransitionResult.Transitioned(Unlocked, ()) -> ()
              | other -> failtestf "Expected Transitioned(Unlocked), got %A" other
          }

          test "transition Unlocked + Push = Locked" {
              match turnstileMachine.Transition Unlocked Push () with
              | TransitionResult.Transitioned(Locked, ()) -> ()
              | other -> failtestf "Expected Transitioned(Locked), got %A" other
          }

          test "transition Locked + Push = Blocked" {
              match turnstileMachine.Transition Locked Push () with
              | TransitionResult.Blocked(BlockReason.InvalidTransition) -> ()
              | other -> failtestf "Expected Blocked(InvalidTransition), got %A" other
          }

          test "transition Unlocked + Coin = Unlocked (idempotent)" {
              match turnstileMachine.Transition Unlocked Coin () with
              | TransitionResult.Transitioned(Unlocked, ()) -> ()
              | other -> failtestf "Expected Transitioned(Unlocked), got %A" other
          }

          test "Initial state is Locked" { Expect.equal turnstileMachine.Initial Locked "initial state" }

          test "StateMetadata lookup by state key" {
              let info = turnstileMachine.StateMetadata[Locked]
              Expect.equal info.AllowedMethods [ "POST" ] "allowed methods"
              Expect.isFalse info.IsFinal "not final"
              Expect.equal info.Description (Some "Waiting for coin") "description"
          } ]

[<Tests>]
let guardTests =
    testList
        "Guard"
        [ test "AccessControl guard evaluates ClaimsPrincipal - allowed" {
              let adminGuard: Guard<TurnstileState, TurnstileEvent, unit> =
                  AccessControl(
                      "isAdmin",
                      fun ctx ->
                          if ctx.User.IsInRole("Admin") then
                              Allowed
                          else
                              Blocked(NotAllowed)
                  )

              let adminIdentity = ClaimsIdentity([ Claim(ClaimTypes.Role, "Admin") ], "test")
              let adminPrincipal = ClaimsPrincipal(adminIdentity)

              let ctx: AccessControlContext<TurnstileState, unit> =
                  { User = adminPrincipal
                    CurrentState = Locked
                    Context = () }

              match adminGuard with
              | AccessControl(_, pred) -> Expect.equal (pred ctx) Allowed "admin should be allowed"
              | _ -> failtest "Expected AccessControl"
          }

          test "AccessControl guard evaluates ClaimsPrincipal - blocked" {
              let adminGuard: Guard<TurnstileState, TurnstileEvent, unit> =
                  AccessControl(
                      "isAdmin",
                      fun ctx ->
                          if ctx.User.IsInRole("Admin") then
                              Allowed
                          else
                              Blocked(NotAllowed)
                  )

              let userIdentity = ClaimsIdentity([ Claim(ClaimTypes.Role, "User") ], "test")
              let userPrincipal = ClaimsPrincipal(userIdentity)

              let ctx: AccessControlContext<TurnstileState, unit> =
                  { User = userPrincipal
                    CurrentState = Locked
                    Context = () }

              match adminGuard with
              | AccessControl(_, pred) -> Expect.equal (pred ctx) (Blocked(NotAllowed)) "non-admin should be blocked"
              | _ -> failtest "Expected AccessControl"
          }

          test "EventValidation guard receives event value" {
              let eventGuard: Guard<TurnstileState, TurnstileEvent, unit> =
                  EventValidation(
                      "checkEvent",
                      fun ctx ->
                          match ctx.Event with
                          | Coin -> Allowed
                          | Push -> Blocked(InvalidTransition)
                  )

              let identity = ClaimsIdentity([ Claim(ClaimTypes.Role, "User") ], "test")
              let principal = ClaimsPrincipal(identity)

              let ctx: EventValidationContext<TurnstileState, TurnstileEvent, unit> =
                  { User = principal
                    CurrentState = Locked
                    Event = Coin
                    Context = () }

              match eventGuard with
              | EventValidation(_, pred) -> Expect.equal (pred ctx) Allowed "Coin should be allowed"
              | _ -> failtest "Expected EventValidation"
          } ]
