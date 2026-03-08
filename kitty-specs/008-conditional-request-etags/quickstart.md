# Quickstart: Conditional Request ETags

**Feature**: 008-conditional-request-etags
**Date**: 2026-03-07

## 1. Registering the ETag Middleware

Enable conditional request handling framework-wide by adding `useConditionalRequests` to your `webHost` CE. All resources with an `IETagProvider` automatically participate.

```fsharp
open Frank.Builder
open Frank.ConditionalRequests

webHost [||] {
    useDefaults
    service (fun services ->
        services.AddETagCache(maxEntries = 10_000)  // MailboxProcessor-backed cache
    )
    plug useConditionalRequests  // Enables ETag generation + If-None-Match/If-Match
    resource myResource
}
```

The middleware runs after routing and before handler execution. It has no effect on resources without an `IETagProvider`.

## 2. Automatic ETags on Statechart Resources

If you use `statefulResource` (Frank.Statecharts), ETags are computed automatically from the `('State * 'Context)` pair. Register the statechart ETag provider:

```fsharp
open Frank.Builder
open Frank.Statecharts
open Frank.ConditionalRequests

type GameState = XTurn | OTurn | XWins | OWins | Draw
type GameContext = { Board: string; MoveCount: int }

let gameResource =
    statefulResource "/games/{gameId}" {
        name "game"
        initialState XTurn
        transition gameTransition
        contextSerializer (fun ctx ->
            System.Text.Encoding.UTF8.GetBytes(sprintf "%s|%d" ctx.Board ctx.MoveCount))

        inState XTurn {
            get (fun ctx -> task { ... })
            post (fun ctx -> task { ... })  // Make a move
        }

        inState OTurn {
            get (fun ctx -> task { ... })
            post (fun ctx -> task { ... })
        }

        inState XWins {
            get (fun ctx -> task { ... })
            // No POST -- game is over
        }
    }

webHost [||] {
    useDefaults
    service (fun services ->
        services
            .AddETagCache()
            .AddStatechartETagProvider<GameState, GameContext>()
    )
    plug useConditionalRequests
    plug useStatecharts
    resource gameResource
}
```

Every GET response now includes an `ETag` header. When a move is made (state transition), the ETag changes automatically.

## 3. Custom IETagProvider for Plain Resources

For non-statechart resources, implement `IETagProvider` to derive ETags from your own state:

```fsharp
open Frank.ConditionalRequests

type ProductETagProvider(db: IProductRepository) =
    interface IETagProvider with
        member _.ComputeETag(instanceId: string) = task {
            match! db.GetProduct(instanceId) with
            | Some product ->
                // Hash the product version/timestamp for ETag
                let data =
                    System.Text.Encoding.UTF8.GetBytes(
                        sprintf "%s|%d" product.Id product.Version)
                return Some (ETagFormat.computeFromBytes data)
            | None ->
                return None
        }

let productResource =
    resource "/products/{productId}" {
        name "product"
        etagProvider "product"  // Links to the registered IETagProvider
        resolveInstanceId (fun ctx -> ctx.GetRouteValue("productId") |> string)

        get (fun ctx -> task {
            // Normal GET handler -- ETag header is set automatically by middleware
            let productId = ctx.GetRouteValue("productId") |> string
            let! product = ctx.RequestServices.GetRequiredService<IProductRepository>().GetProduct(productId)
            // ... write response
        })

        put (fun ctx -> task {
            // Normal PUT handler -- If-Match is checked automatically by middleware
            // If the ETag doesn't match, the client gets 412 before this runs
            let productId = ctx.GetRouteValue("productId") |> string
            // ... update product
        })
    }

webHost [||] {
    useDefaults
    service (fun services ->
        services
            .AddETagCache()
            .AddSingleton<IETagProvider>(ProductETagProvider(services.GetRequiredService()))
    )
    plug useConditionalRequests
    resource productResource
}
```

## 4. Conditional GET (304 Not Modified)

Clients that cache responses can send `If-None-Match` to check if the resource has changed:

```
# First request -- get the resource and its ETag
GET /games/42 HTTP/1.1

HTTP/1.1 200 OK
ETag: "a1b2c3d4e5f67890a1b2c3d4e5f67890"
Content-Type: application/json

{"state": "XTurn", "board": ".........", "moveCount": 0}
```

```
# Second request -- send the ETag back to check for changes
GET /games/42 HTTP/1.1
If-None-Match: "a1b2c3d4e5f67890a1b2c3d4e5f67890"

HTTP/1.1 304 Not Modified
ETag: "a1b2c3d4e5f67890a1b2c3d4e5f67890"
(no body)
```

```
# After a state transition (someone made a move), the ETag has changed
GET /games/42 HTTP/1.1
If-None-Match: "a1b2c3d4e5f67890a1b2c3d4e5f67890"

HTTP/1.1 200 OK
ETag: "f0e1d2c3b4a59687f0e1d2c3b4a59687"
Content-Type: application/json

{"state": "OTurn", "board": "X........", "moveCount": 1}
```

Multiple ETags can be sent in a single header (any match triggers 304):

```
GET /games/42 HTTP/1.1
If-None-Match: "old1", "old2", "a1b2c3d4e5f67890a1b2c3d4e5f67890"

HTTP/1.1 304 Not Modified
```

The wildcard `*` matches any existing resource:

```
GET /games/42 HTTP/1.1
If-None-Match: *

HTTP/1.1 304 Not Modified
```

## 5. Optimistic Concurrency (412 Precondition Failed)

Use `If-Match` on mutation requests to prevent lost updates when multiple clients modify the same resource:

```
# Client A reads the game
GET /games/42 HTTP/1.1

HTTP/1.1 200 OK
ETag: "a1b2c3d4e5f67890a1b2c3d4e5f67890"
Content-Type: application/json

{"state": "XTurn", "board": ".........", "moveCount": 0}
```

```
# Client B also reads the game (same ETag)
GET /games/42 HTTP/1.1

HTTP/1.1 200 OK
ETag: "a1b2c3d4e5f67890a1b2c3d4e5f67890"
```

```
# Client A makes a move -- succeeds, state transitions
POST /games/42 HTTP/1.1
If-Match: "a1b2c3d4e5f67890a1b2c3d4e5f67890"
Content-Type: application/json

{"position": 0}

HTTP/1.1 200 OK
ETag: "f0e1d2c3b4a59687f0e1d2c3b4a59687"
```

```
# Client B tries to make a move with the OLD ETag -- rejected!
POST /games/42 HTTP/1.1
If-Match: "a1b2c3d4e5f67890a1b2c3d4e5f67890"
Content-Type: application/json

{"position": 4}

HTTP/1.1 412 Precondition Failed
(Client B must re-GET to see the updated board and retry)
```

Requests without `If-Match` proceed normally (conditional headers are optional):

```
# No If-Match -- no precondition check, request proceeds
POST /games/42 HTTP/1.1
Content-Type: application/json

{"position": 0}

HTTP/1.1 200 OK
ETag: "f0e1d2c3b4a59687f0e1d2c3b4a59687"
```

## Summary of HTTP Behavior

| Method | Header | Condition | Response |
|--------|--------|-----------|----------|
| GET/HEAD | `If-None-Match` | ETag matches | 304 Not Modified (no body) |
| GET/HEAD | `If-None-Match` | ETag differs | 200 OK (full response + new ETag) |
| GET/HEAD | `If-None-Match: *` | Resource exists | 304 Not Modified |
| POST/PUT/DELETE | `If-Match` | ETag matches | Request proceeds normally |
| POST/PUT/DELETE | `If-Match` | ETag differs | 412 Precondition Failed |
| POST/PUT/DELETE | `If-Match: *` | Resource exists | Request proceeds normally |
| Any | (no conditional header) | -- | Request proceeds normally |
| Any | (resource has no IETagProvider) | -- | Request proceeds normally, no ETag header |
