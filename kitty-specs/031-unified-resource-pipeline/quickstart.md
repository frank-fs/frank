# Quickstart: Unified Resource Pipeline

## For Developers (CLI workflow)

```bash
# 1. Extract unified resource model from your project
frank-cli extract --project MyApp/MyApp.fsproj

# 2. Generate format artifacts
frank-cli generate --project MyApp/MyApp.fsproj --format alps     # ALPS profile
frank-cli generate --project MyApp/MyApp.fsproj --format all      # All formats
frank-cli generate --project MyApp/MyApp.fsproj --format affordance-map  # Binary map

# 3. Validate OpenAPI consistency
frank-cli validate --project MyApp/MyApp.fsproj --openapi

# 4. Inspect extraction (human-readable)
frank-cli extract --project MyApp/MyApp.fsproj --output-format json

# 5. Cache is automatic — subsequent commands skip FCS analysis if source unchanged
frank-cli generate --project MyApp/MyApp.fsproj --format wsd   # Fast (cached)
frank-cli extract --project MyApp/MyApp.fsproj --force          # Force re-extract
```

## For Application Runtime

```fsharp
// In your Frank application's Program.fs:

let app =
    webHost {
        useOpenApi          // OpenAPI + Scalar UI
        useAffordances      // Link + Allow headers + profile/schema serving

        resource "/games/{gameId}" {
            name "Games"
        }

        statefulResource "/games/{gameId}" {
            machine gameMachine
            resolveInstanceId (fun ctx -> ctx.Request.RouteValues["gameId"] :?> string)
            inState (forState XTurn [ StateHandlerBuilder.get getGame; StateHandlerBuilder.post makeMove ])
            inState (forState OTurn [ StateHandlerBuilder.get getGame; StateHandlerBuilder.post makeMove ])
            inState (forState (Won "X") [ StateHandlerBuilder.get getGame ])
            inState (forState Draw [ StateHandlerBuilder.get getGame ])
        }
    }
```

**What happens at runtime:**

```http
GET /games/42 HTTP/1.1

HTTP/1.1 200 OK
Allow: GET, POST
Link: <https://example.com/alps/games>; rel="profile"
Link: <https://example.com/schemas/game>; rel="describedby"
Link: </games/42>; rel="self"
Link: </games/42>; rel="https://example.com/alps/games#makeMove"; method="POST"
Content-Type: application/json

{"board": [...], "currentTurn": "X"}
```

Three self-descriptive links:
- `rel="profile"` → ALPS: what this resource **can do** (behavioral semantics)
- `rel="describedby"` → JSON Schema: what this response **looks like** (structural schema)
- `rel="self"` + transition links → what actions are **available now** (state-dependent affordances)

When the game reaches Won state:

```http
GET /games/42 HTTP/1.1

HTTP/1.1 200 OK
Allow: GET
Link: <https://example.com/alps/games>; rel="profile"
Link: <https://example.com/schemas/game>; rel="describedby"
Link: </games/42>; rel="self"
Content-Type: application/json

{"board": [...], "winner": "X"}
```

## For Datastar Applications

```fsharp
// In your SSE handler, use affordancesFor to drive conditional rendering:
let gameHandler (ctx: HttpContext) = task {
    let state = ctx.Items["statechart.stateKey"] :?> string
    let affordances = AffordanceHelper.affordancesFor "/games/{gameId}" state

    if affordances.AllowedMethods |> List.contains "POST" then
        // Stream interactive controls
        do! ctx |> patchElements "#controls" (renderMoveButton())
    else
        // Stream read-only display
        do! ctx |> patchElements "#controls" (renderGameOver())
}
```

## Build Integration

The MSBuild target auto-embeds the unified state binary. Add the package reference:

```xml
<PackageReference Include="Frank.Affordances" Version="..." />
```

After `dotnet build`, the binary state is embedded in your assembly automatically (if `obj/frank-cli/unified-state.bin` exists from a prior `frank-cli extract`).
