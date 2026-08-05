# Frank.Alps: multi-document ALPS profiles (per-resource, per-role)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** `Serialization.fs` assumes every descriptor a served document references is present in that same document. True in v1 (one document, `/.well-known/alps.json`, everything local). False once a served document is a *filtered view* of that one profile — a per-resource excerpt (`Alps.excerpt`) or a role-pruned full document (`AuthorizationFilter`) — and a kept descriptor still references one that got filtered out. Fix cross-document reference resolution (three call sites, one shared serializer), then prove it end-to-end with a ping/pong sample exercising doc-linking, state-gating, and role-projection together.

**Tracks:** frank-fs/frank#488

**Explicitly out of scope** (see design doc's *Deferred / explicitly out of scope*): tic-tac-toe seat-scoped documents (seats aren't stable roles — left to the separate `tic-tac-toe` repo), hierarchical/composite states, the multi-`useAlps` analyzer guardrail, the order fulfillment sample app, and any genuinely separate (not filtered-view) second document.

**Design doc:** `docs/superpowers/specs/2026-08-04-frank-alps-multi-doc-linking.md`

## Global Constraints

- `Serialization.toJson`'s signature changes (`profile: Descriptor list -> string` → `rootUri: Uri -> profile: Descriptor list -> string`) — a breaking change for every caller. `grep -rn "Serialization.toJson" src/ test/ sample/` before finishing each task touching it; update every hit, not just the ones named below.
- Every `.fs` file has a matching `.fsi` (`CLAUDE.md`). Update signature files alongside implementation.
- Test framework is Expecto.
- Verify across all three TFMs (`net8.0;net9.0;net10.0`) — this package multi-targets.
- Commit directly to this task's branch when done (trunk-based repo — no PR needed once merged back to master by the coordinator). Create the branch/worktree before starting; do not commit to `master`.
- No change to `AlpsOptions`, `Descriptor`, `DescriptorRef`, or any authoring combinator's *public* signature (`href`, `hrefExternal`, `rt`, `from`, etc. stay exactly as authored). Only the serializer and the two HTTP handlers around it change.

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/Frank.Alps/Serialization.fs` | Modify | `idsIn`/`resolveRef` helpers; `toJson` gains `rootUri: Uri` parameter; all three hardcoded `"#" + id` sites route through `resolveRef` |
| `src/Frank.Alps/Serialization.fsi` | Modify | `toJson`'s new signature, doc comment explaining the two-case resolution |
| `src/Frank.Alps/AlpsDocument.fs` | Modify | `documentHandler` takes `AlpsOptions`, builds its own `rootUri`; `install` registers `AlpsOptions` as a DI singleton |
| `src/Frank.Alps/AlpsDocument.fsi` | Modify (if `documentHandler`'s signature is exposed — check; likely stays private, only doc comment needs updating) | |
| `src/Frank.Alps/Excerpt.fs` | Modify | Resolve `rootUri` from DI (`ctx.RequestServices.GetService<AlpsOptions>()`, fallback `AlpsOptions.Default`) before calling `toJson` |
| `test/Frank.Alps.Tests/SerializationTests.fs` | Modify | All 10 direct `Serialization.toJson [...]` calls gain a `rootUri` argument; new tests for the cross-doc fallback case |
| `sample/Frank.Alps.Sample/Program.fs` | Modify | Add `PingPong` descriptors + `/sessions`, `/sessions/{id}`, `/sessions/{id}/ping`, `/sessions/{id}/pong` resources, `requireRole` wiring |
| `sample/Frank.Alps.Sample/README.md` | Modify | Document the new ping/pong endpoints alongside the existing game ones |
| `sample/Frank.JsonHome.Sample/ApiKeyAuth.fs` | Reference only, not modified | Existing pattern for fake per-request principal/claims in a sample |
| `test/Frank.Alps.Tests/SampleIntegrationTests.fs` | Modify | New ping/pong end-to-end test (doc-linking + state-gating + role-projection) |
| `RELEASE_NOTES.md` | Modify | Note the `Serialization.toJson` signature change |

---

### Task 1: Cross-document reference resolution in `Serialization.fs`

**Files:** `src/Frank.Alps/Serialization.fs`, `src/Frank.Alps/Serialization.fsi`, `test/Frank.Alps.Tests/SerializationTests.fs`.

**Interfaces:**
- Consumes: existing `DescriptorTree.flattenAll: Descriptor list -> Descriptor list` (already used identically in `AlpsDocument.fs:65,78`).
- Produces: `Serialization.toJson: rootUri: Uri -> profile: Descriptor list -> string` (was `profile: Descriptor list -> string`).

**Exact change** to `src/Frank.Alps/Serialization.fs`:

Add, above `resolveHref`:

```fsharp
let private idsIn (profile: Descriptor list) : Set<string> =
    DescriptorTree.flattenAll profile |> List.map (fun d -> d.Id) |> Set.ofList

let private resolveRef (rootUri: Uri) (present: Set<string>) (id: string) : string =
    if Set.contains id present then "#" + id
    else rootUri.ToString() + "#" + id
```

Change `resolveHref` (currently module-level, called only from `writeDescriptor`) to take `present`/`rootUri` and use `resolveRef`:

```fsharp
let private resolveHref (rootUri: Uri) (present: Set<string>) (r: DescriptorRef) : string =
    match r with
    | DescriptorRef.Local target -> resolveRef rootUri present target.Id
    | DescriptorRef.External uri -> uri.ToString()
```

`stateExtPairs` (currently `from_: Descriptor list -> Ext list`, hardcodes `"#" + state.Id"` at line 59) becomes:

```fsharp
let private stateExtPairs (rootUri: Uri) (present: Set<string>) (from_: Descriptor list) : Ext list =
    from_
    |> List.collect (fun state ->
        let value = Some(resolveRef rootUri present state.Id)
        [ { Id = ProtocolStateExtId; Href = None; Value = value; Tag = [] }
          { Id = AvailableInStatesExtId; Href = None; Value = value; Tag = [] } ])
```

`writeDescriptor` (currently `Utf8JsonWriter -> Descriptor -> unit`) becomes `Utf8JsonWriter -> Uri -> Set<string> -> Descriptor -> unit`, threading `rootUri`/`present` through its one recursive call (line 105, the `d.Descriptors` loop) and using them at:
- line 84: `stateExtPairs rootUri present d.From` (was `stateExtPairs d.From`)
- line 91: `resolveHref rootUri present r` (was `resolveHref r`)
- line 92: `"rt", resolveRef rootUri present target.Id` (was `"rt", "#" + target.Id`)

`toJson`:

```fsharp
let toJson (rootUri: Uri) (profile: Descriptor list) : string =
    let present = idsIn profile
    use stream = new MemoryStream()

    (use writer = new Utf8JsonWriter(stream)
     writer.WriteStartObject()
     writer.WriteStartObject("alps")
     writer.WriteString("version", "1.0")
     writer.WriteStartArray("descriptor")
     profile |> List.iter (writeDescriptor writer rootUri present)
     writer.WriteEndArray()
     writer.WriteEndObject()
     writer.WriteEndObject())

    Encoding.UTF8.GetString(stream.ToArray())
```

**Exact change** to `src/Frank.Alps/Serialization.fsi`: update `toJson`'s signature and doc comment to state the two-case resolution (`#id` if the referenced descriptor is present in `profile`'s own tree, else `rootUri#id`) — this is the load-bearing contract the rest of the plan depends on, so word it precisely; see design doc's *The fix* section for the exact reasoning to paraphrase.

**Tests to update in `SerializationTests.fs`:** add `let private testRootUri = Uri("/.well-known/alps.json", UriKind.Relative)` near the top; every existing `Serialization.toJson [ ... ]` call becomes `Serialization.toJson testRootUri [ ... ]` (10 call sites — see Global Constraints' grep). No existing assertion should change value: every existing test's `profile` list is already self-contained (referenced descriptor always included), so `present` always contains every id referenced and every existing `#id`/`https://example.org/other#thing` assertion is unaffected.

**New tests to add:**
- `"href to a descriptor NOT in profile resolves to rootUri#id"`: `let shared = semantic "shared"` (never added to the list passed to `toJson`), `let local = semantic "local" |> href shared`; call `Serialization.toJson testRootUri [ local ]`; assert `local`'s `href` equals `"/.well-known/alps.json#shared"`.
- `"rt to a descriptor NOT in profile resolves to rootUri#id"`: same shape for `rt`.
- `"from-state NOT in profile resolves ext value to rootUri#id"`: same shape for `stateExtPairs`'s output.

**Verification:** `dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj` passes on all three TFMs. `grep -rn "Serialization.toJson" src/ test/ sample/` shows zero remaining single-argument call sites.

---

### Task 2: Thread `rootUri` through both HTTP exposures

**Files:** `src/Frank.Alps/AlpsDocument.fs`, `src/Frank.Alps/Excerpt.fs`.

**Interfaces:**
- Consumes: `Serialization.toJson` from Task 1 (must land first — this task doesn't compile without it).
- Produces: both `documentHandler` (full doc) and `Alps.excerpt` (per-resource) call `toJson` with a real `rootUri`, no behavior change to what's *served* when nothing is filtered out (full-document requests are unaffected — everything they reference is always present in `profile`, so `resolveRef` always takes the `#id` branch for them, identical output to before this plan).

**Exact change** to `src/Frank.Alps/AlpsDocument.fs`:

`documentHandler` gains an `options` parameter and builds its own `rootUri`:

```fsharp
let private documentHandler (options: AlpsOptions) (profile: Descriptor list) (ctx: HttpContext) : Task =
    task {
        ...
        ctx.Response.ContentType <- MediaType
        do! ctx.Response.WriteAsync(Serialization.toJson (Uri(options.Path, UriKind.Relative)) served)
    }
```

`documentResource` passes `options` through: `resource alpsOptions.Path { get (RequestDelegate(documentHandler alpsOptions profile)) }` (was `documentHandler profile`).

`install` (in `WebHostBuilderExtensions`) registers `AlpsOptions` as a DI singleton so `Excerpt.fs` can read it back:

```fsharp
Services =
    spec.Services
    >> fun services ->
        services.AddSingleton<IStartupFilter>(AlpsDocument.ValidationStartupFilter profile)
        services.AddSingleton<AlpsOptions>(options)
```

**Exact change** to `src/Frank.Alps/Excerpt.fs`:

Add, near `routePatternOf`:

```fsharp
let private rootUriFor (ctx: HttpContext) : Uri =
    let options =
        match ctx.RequestServices.GetService<AlpsOptions>() with
        | null -> AlpsOptions.Default
        | opts -> opts
    Uri(options.Path, UriKind.Relative)
```

(needs `open Microsoft.Extensions.DependencyInjection` for `GetService<'T>` — check it isn't already implicitly available via another open; add if missing.)

Change the final line: `return! ctx.Response.WriteAsync(Serialization.toJson (rootUriFor ctx) served)` (was `Serialization.toJson served`).

**Fallback rationale:** an app calling `Alps.excerpt` without ever calling `useAlps` (no full document registered) still gets a working excerpt — cross-doc refs resolve against `AlpsOptions.Default.Path`, matching the path a full document would use if one were added later. Document this reasoning in a comment at `rootUriFor`, not just in this plan.

**Tests:** no new tests required for this task specifically — Task 1's new `SerializationTests.fs` cases cover the resolution logic directly; `AlpsDocumentIntegrationTests.fs` and `ExcerptTests.fs`'s existing HTTP-level tests exercise this task's wiring incidentally (they'll fail to compile/pass if `rootUri` isn't threaded correctly) and should be run, not modified, unless something in them asserts on `documentHandler`'s or `Alps.excerpt`'s internal signature directly rather than through HTTP.

**Verification:** `dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj` passes on all three TFMs, all existing tests included (`AlpsDocumentIntegrationTests.fs`, `ExcerptTests.fs`, `FilteringIntegrationTests.fs`, `SampleIntegrationTests.fs` especially — they hit the two HTTP handlers this task changes).

---

### Task 3: Ping/pong sample (`sample/Frank.Alps.Sample`)

**Files:** `sample/Frank.Alps.Sample/Program.fs`, `sample/Frank.Alps.Sample/README.md`, `sample/Frank.Alps.Sample/Frank.Alps.Sample.fsproj` (add `Frank.Auth` project reference if not already present — check first).

**Interfaces:**
- Consumes: `Frank.Auth`'s `resource { requireRole "..." }` (`Frank.Auth/ResourceBuilderExtensions.fsi`), the fixed `Serialization`/`Excerpt`/`AlpsDocument` behavior from Tasks 1–2, `Frank.Provenance`'s `MailboxProcessorProvenanceStore` (already used by the existing `gameResource` — reuse the same store instance, don't stand up a second one, unless session isolation requires it — check `stateResolver`'s existing convention (`ActivityType` = the state's own `Def` IRI) extends cleanly to two alternating states before assuming a second store is needed).
- Produces: `/sessions`, `/sessions/{id}`, `/sessions/{id}/ping`, `/sessions/{id}/pong` resources registered alongside the existing `gameResource`; a `PingPong` descriptor catalog; two test principals (`pinger`, `ponger`) following `sample/Frank.JsonHome.Sample/ApiKeyAuth.fs`'s existing pattern.

**Exact descriptors** (add as a new `PingPong` module in `Program.fs`, alongside the existing `Catalog` module — do not rename or restructure `Catalog`):

```fsharp
module PingPong =
    let participant = semantic "participant" |> doc "A session participant"

    let awaitingPing =
        semantic "awaitingPing" |> doc "Waiting for a ping"
        |> def "https://pingpong.example/states/awaitingPing"
    let awaitingPong =
        semantic "awaitingPong" |> doc "Waiting for a pong"
        |> def "https://pingpong.example/states/awaitingPong"
    let session = semantic "session" |> doc "A ping-pong session"

    let listSessions = safe "listSessions" |> rt session
    let createSession = unsafe "createSession" |> rt session
    let viewSession = safe "viewSession" |> rt session

    let ping = unsafe "ping" |> from [ awaitingPing ] |> rt awaitingPong |> href participant
    let pong = unsafe "pong" |> from [ awaitingPong ] |> rt awaitingPing |> href participant
```

**Resources** — see design doc's route table. `/sessions/{id}/ping` and `/sessions/{id}/pong` each `requireRole "pinger"` / `requireRole "ponger"` respectively (Frank.Auth `ResourceBuilderExtensions`). Each POST handler appends a Provenance activity typed with the *target* state's `Def` (mirrors the existing `makeMoveHandler`'s convention exactly — `ping`'s handler types the activity `awaitingPong.Def`, `pong`'s types it `awaitingPing.Def`), and the session's initial state (before any move) is `awaitingPing`.

`useAlps` at the bottom of `main` gains every `PingPong` descriptor appended to the existing list.

**Test principals:** two `ClaimsPrincipal`s, one with role `pinger`, one with role `ponger`, selected per-request the same way `ApiKeyAuth.fs` selects a principal from a request header/key — read that file before implementing, match its pattern exactly rather than inventing a new auth scheme.

**Tests:** none in this task — covered by Task 4.

**Verification:** `dotnet run --project sample/Frank.Alps.Sample/` starts without error; manual `curl` (or the sample's own README instructions) against `/sessions`, `/sessions/{id}`, `/sessions/{id}/ping`, `/sessions/{id}/pong` returns expected `application/alps+json` and `application/json` bodies.

---

### Task 4: End-to-end ping/pong test

**Files:** `test/Frank.Alps.Tests/SampleIntegrationTests.fs`.

**Interfaces:**
- Consumes: the sample from Task 3, running (check this file's existing tests — likely already start the sample via `WebApplicationFactory`/`TestServer`, mirror that setup rather than adding a second harness).

**Test to add**, mirroring the existing tests' structure (`createServer`, `request`, assertions on both JSON and ALPS-JSON bodies):

1. `POST /sessions` as either principal → create a session, capture `id`.
2. As `pinger`: `GET /sessions/{id}/ping?Accept=application/alps+json` → assert the `ping` descriptor is present and its `href` resolves to `/.well-known/alps.json#participant` (proves Task 1's cross-doc fix, since `participant` is unbound and never appears in this excerpt).
3. As `ponger`: same request to `/sessions/{id}/ping` → assert 403 (role-gating).
4. As `pinger`: `POST /sessions/{id}/ping` → 200. As `pinger` again immediately after → 409 or the transition absent from a follow-up excerpt fetch (state-gating: it's no longer `awaitingPing`).
5. As `ponger`: `POST /sessions/{id}/pong` → 200, session state returns to `awaitingPing`.
6. `GET /.well-known/alps.json` as `pinger` → assert `ping` present, `pong` absent. Same request as `ponger` → assert the reverse. (Role-projection via the full document, per design doc.)

**Verification:** `dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj --filter SampleIntegrationTests` passes on all three TFMs. Full suite (`dotnet test test/Frank.Alps.Tests/Frank.Alps.Tests.fsproj`) passes with no regressions from Tasks 1–3.
