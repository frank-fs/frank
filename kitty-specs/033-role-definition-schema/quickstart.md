# Quickstart: Role Definition Schema

**Date**: 2026-03-21
**Feature**: 033-role-definition-schema

## Declaring Roles

```fsharp
let gameById = statefulResource "/games/{id}" {
    machine gameMachine
    resolveInstanceId (fun ctx -> ctx.Request.RouteValues["id"] :?> string)

    // Declare named roles with claims predicates
    role "PlayerX" (fun claims -> claims.HasClaim("player", "X"))
    role "PlayerO" (fun claims -> claims.HasClaim("player", "O"))
    role "Observer" (fun _ -> true)

    inState (forState XTurn [
        get Handlers.getGame
        post Handlers.handleMove
    ])
    inState (forState OTurn [
        get Handlers.getGame
        post Handlers.handleMove
    ])
}
```

## Using Roles in Guards

```fsharp
let turnGuard: Guard<GameState, GameEvent, GameContext> =
    AccessControl(
        "TurnGuard",
        fun ctx ->
            match ctx.CurrentState with
            | XTurn ->
                if ctx.HasRole "PlayerX" then Allowed
                else Blocked NotYourTurn
            | OTurn ->
                if ctx.HasRole "PlayerO" then Allowed
                else Blocked NotYourTurn
            | _ -> Allowed
    )
```

## Accessing Roles in Handlers

```fsharp
let getGame: RequestDelegate =
    RequestDelegate(fun ctx ->
        task {
            let roleFeature = ctx.Features.Get<IRoleFeature>()
            let roles = roleFeature.Roles  // Set<string>

            // Customize response based on roles
            let isPlayer = roles.Contains("PlayerX") || roles.Contains("PlayerO")
            // ...
        })
```

## Backward Compatibility

Existing `statefulResource` definitions without `role` declarations continue to work unchanged. The resolved role set is empty, and `HasRole` always returns `false`.
