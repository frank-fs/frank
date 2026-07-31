# Frank.Auth Handler-Level Authorization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a single HTTP method within a Frank resource carry its own authorization requirements (additive) or opt out of inherited ones entirely (`allowAnonymous`), and make `Frank.JsonHome`'s `hints.allow` reflect that per method instead of merging across a whole resource.

**Architecture:** `Frank.Auth` gains `HandlerBuilder` operations mirroring its existing `ResourceBuilder` ones, targeting `HandlerDefinition.Metadata` instead of `ResourceSpec.Metadata` — no Frank core change needed, since `ResourceBuilder.AddHandlerDefinition` already scopes a `HandlerDefinition`'s metadata to just its own HTTP method. `Frank.JsonHome`'s `ApiSurface` starts retaining metadata per method instead of merging it, and `AuthorizationFilter` evaluates and filters per method instead of per resource.

**Tech Stack:** F# 8.0+, multi-targeting `net8.0;net9.0;net10.0` (`Frank.Auth`) and the same for `Frank.JsonHome`; ASP.NET Core `Microsoft.AspNetCore.Authorization`; Expecto for tests.

## Global Constraints

- Every `.fs` file under `src/Frank.*/` gets a matching `.fsi` signature file, placed directly above it in `<Compile>` order in the `.fsproj`. (Project convention, `CLAUDE.md`.)
- Verify every `.fsi` change with a real build across all three targeted TFMs (`net8.0`, `net9.0`, `net10.0`), not just the default — signature mismatches only surface at compile time. (Project convention, `CLAUDE.md`.)
- No Frank core changes. The per-method metadata-scoping mechanism (`HandlerDefinition`, `ResourceBuilder.AddHandlerDefinition`, `AddMethodMetadata`) already exists and is already tested. (Design doc `docs/superpowers/specs/2026-07-29-handler-auth-design.md`, "Non-goals".)
- `allowAnonymous` is a binary bypass of *all* authorization on that endpoint (resource- and handler-level alike) — not a policy downgrade. Implemented purely via stock ASP.NET Core `AllowAnonymousAttribute`/`IAllowAnonymous`; no custom bypass logic. (Design doc, "Composition semantics".)
- A resource left with zero visible methods after per-method filtering is dropped from the JSON Home document entirely, not emitted with degraded hints. (Design doc, "Frank.JsonHome changes".)

---

## Task 1: `EndpointAuth.fs` refactor — shared metadata-object construction

**Files:**
- Modify: `src/Frank.Auth/EndpointAuth.fsi` (whole file, currently 7 lines)
- Modify: `src/Frank.Auth/EndpointAuth.fs` (whole file, currently 41 lines)
- Create: `test/Frank.Auth.Tests/EndpointAuthTests.fs`
- Modify: `test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj:11-14` (add new `<Compile>` item)

**Interfaces:**
- Consumes: `Frank.Auth.AuthRequirement` (`Authenticated | Claim of string * string list | Policy of string | Role of string`), `Frank.Auth.AuthConfig` (`{ Requirements: AuthRequirement list }`, `AuthConfig.empty`, `AuthConfig.addRequirement`, `AuthConfig.isEmpty`), `Frank.Builder.HandlerDefinition` (`{ Handler: RequestDelegate; Metadata: obj list }`, `HandlerDefinition.Empty`, `HandlerDefinition.addMetadata`), `Frank.Builder.ResourceSpec`, `Frank.Builder.ResourceBuilder.AddMetadata`.
- Produces: `EndpointAuth.toMetadataObjects : requirement:AuthRequirement -> obj list` and `EndpointAuth.applyAuthToHandler : config:AuthConfig -> def:HandlerDefinition -> HandlerDefinition`, both consumed by Task 2. `EndpointAuth.applyAuth` keeps its existing signature (`config:AuthConfig -> spec:ResourceSpec -> ResourceSpec`) and behavior — this task is a pure refactor of it, not a behavior change.

- [ ] **Step 1: Write the failing test**

Create `test/Frank.Auth.Tests/EndpointAuthTests.fs`:

```fsharp
module Frank.Auth.Tests.EndpointAuthTests

open Microsoft.AspNetCore.Authorization
open Expecto
open Frank.Builder
open Frank.Auth

let private emptyDef = HandlerDefinition.Empty

[<Tests>]
let tests =
    testList
        "EndpointAuth.applyAuthToHandler"
        [ test "empty config leaves the handler definition's metadata unchanged" {
              let result = EndpointAuth.applyAuthToHandler AuthConfig.empty emptyDef
              Expect.isEmpty result.Metadata "No metadata added"
          }

          test "Authenticated requirement adds a single bare AuthorizeAttribute" {
              let config = AuthConfig.empty |> AuthConfig.addRequirement AuthRequirement.Authenticated
              let result = EndpointAuth.applyAuthToHandler config emptyDef
              Expect.hasLength result.Metadata 1 "One metadata object"
              Expect.isTrue (result.Metadata.[0] :? AuthorizeAttribute) "It's an AuthorizeAttribute"
          }

          test "Claim requirement adds an AuthorizeAttribute and a built policy" {
              let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Claim("scope", [ "admin" ]))
              let result = EndpointAuth.applyAuthToHandler config emptyDef
              Expect.hasLength result.Metadata 2 "Two metadata objects"
              Expect.isTrue (result.Metadata |> List.exists (fun m -> m :? AuthorizeAttribute)) "Has an AuthorizeAttribute"
              Expect.isTrue (result.Metadata |> List.exists (fun m -> m :? AuthorizationPolicy)) "Has a built policy"
          }

          test "Role requirement adds an AuthorizeAttribute and a built policy" {
              let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Role "admin")
              let result = EndpointAuth.applyAuthToHandler config emptyDef
              Expect.hasLength result.Metadata 2 "Two metadata objects"
              Expect.isTrue (result.Metadata |> List.exists (fun m -> m :? AuthorizeAttribute)) "Has an AuthorizeAttribute"
              Expect.isTrue (result.Metadata |> List.exists (fun m -> m :? AuthorizationPolicy)) "Has a built policy"
          }

          test "Policy requirement adds a single named AuthorizeAttribute" {
              let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Policy "CanViewReports")
              let result = EndpointAuth.applyAuthToHandler config emptyDef
              Expect.hasLength result.Metadata 1 "One metadata object"
              match result.Metadata.[0] with
              | :? AuthorizeAttribute as attr -> Expect.equal attr.Policy "CanViewReports" "Policy name carried through"
              | _ -> failtest "Expected an AuthorizeAttribute"
          }

          test "multiple requirements accumulate across calls" {
              let config =
                  AuthConfig.empty
                  |> AuthConfig.addRequirement AuthRequirement.Authenticated
                  |> AuthConfig.addRequirement (AuthRequirement.Role "admin")

              let result = EndpointAuth.applyAuthToHandler config emptyDef
              Expect.hasLength result.Metadata 3 "Authenticated (1 object) + Role (2 objects)"
          } ]
```

Add it to the test project's compile list. Edit `test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj`:

```xml
  <ItemGroup>
    <Compile Include="EndpointAuthTests.fs" />
    <Compile Include="AuthorizationTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>
```

(Replaces the existing two-item `<ItemGroup>` at lines 11-14.)

- [ ] **Step 2: Run the test to verify it fails to build**

Run: `dotnet test test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj`
Expected: Build error — `EndpointAuth.applyAuthToHandler` is not defined (it doesn't exist yet). This is the statically-typed equivalent of a failing test: the code that should exist doesn't.

- [ ] **Step 3: Refactor `EndpointAuth.fsi`**

Replace the full contents of `src/Frank.Auth/EndpointAuth.fsi`:

```fsharp
namespace Frank.Auth

open Frank.Builder

module EndpointAuth =
    /// The stock ASP.NET Core metadata objects a single requirement
    /// contributes: a bare AuthorizeAttribute for Authenticated/Policy, or an
    /// AuthorizeAttribute plus an explicit built AuthorizationPolicy for
    /// Claim/Role. Shared by the resource-level and handler-level paths so
    /// both produce identical metadata shapes.
    val toMetadataObjects: requirement: AuthRequirement -> obj list

    val applyAuth: config: AuthConfig -> spec: ResourceSpec -> ResourceSpec

    /// Handler-level counterpart to applyAuth: appends each requirement's
    /// metadata objects directly onto the HandlerDefinition. ResourceBuilder
    /// .AddHandlerDefinition later scopes them to just that handler's HTTP
    /// method -- this function does not need to know about scoping at all.
    val applyAuthToHandler: config: AuthConfig -> def: HandlerDefinition -> HandlerDefinition
```

- [ ] **Step 4: Refactor `EndpointAuth.fs`**

Replace the full contents of `src/Frank.Auth/EndpointAuth.fs`:

```fsharp
namespace Frank.Auth

open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Builder
open Frank.Builder

module EndpointAuth =
    let toMetadataObjects (requirement: AuthRequirement) : obj list =
        match requirement with
        | AuthRequirement.Authenticated -> [ AuthorizeAttribute() ]
        | AuthRequirement.Claim(claimType, claimValues) ->
            let policy =
                let pb = AuthorizationPolicyBuilder()
                if claimValues |> List.isEmpty then
                    pb.RequireClaim(claimType) |> ignore
                else
                    pb.RequireClaim(claimType, claimValues |> List.toArray) |> ignore
                pb.Build()
            [ AuthorizeAttribute(); policy ]
        | AuthRequirement.Role name ->
            let policy =
                let pb = AuthorizationPolicyBuilder()
                pb.RequireRole(name) |> ignore
                pb.Build()
            [ AuthorizeAttribute(); policy ]
        | AuthRequirement.Policy name -> [ AuthorizeAttribute(name) ]

    let private toConvention (requirement: AuthRequirement) : EndpointBuilder -> unit =
        let metadataObjects = toMetadataObjects requirement
        fun b -> metadataObjects |> List.iter b.Metadata.Add

    let applyAuth (config: AuthConfig) (spec: ResourceSpec) : ResourceSpec =
        if AuthConfig.isEmpty config then
            spec
        else
            config.Requirements
            |> List.fold (fun s req -> ResourceBuilder.AddMetadata(s, toConvention req)) spec

    let applyAuthToHandler (config: AuthConfig) (def: HandlerDefinition) : HandlerDefinition =
        if AuthConfig.isEmpty config then
            def
        else
            config.Requirements
            |> List.collect toMetadataObjects
            |> List.fold (fun d m -> HandlerDefinition.addMetadata m d) def
```

- [ ] **Step 5: Run the new test to verify it passes**

Run: `dotnet test test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj --filter "FullyQualifiedName~EndpointAuth"`
Expected: PASS, all 6 assertions in `EndpointAuthTests.fs`.

- [ ] **Step 6: Verify no regression in the existing resource-level suite**

Run: `dotnet test test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj`
Expected: PASS, all existing tests in `AuthorizationTests.fs` unchanged (this step proves `applyAuth`'s behavior is unaffected by the refactor).

- [ ] **Step 7: Verify the `.fsi` change builds across every targeted TFM**

Run: `dotnet build src/Frank.Auth/Frank.Auth.fsproj -f net8.0` then `-f net9.0` then `-f net10.0`
Expected: All three succeed.

- [ ] **Step 8: Commit**

```bash
git add src/Frank.Auth/EndpointAuth.fs src/Frank.Auth/EndpointAuth.fsi test/Frank.Auth.Tests/EndpointAuthTests.fs test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj
git commit -m "refactor(auth): extract EndpointAuth.toMetadataObjects, add applyAuthToHandler"
```

---

## Task 2: `HandlerBuilder` gets `requireAuth`/`requireClaim`/`requireRole`/`requirePolicy`/`allowAnonymous`

**Files:**
- Create: `src/Frank.Auth/HandlerBuilderExtensions.fsi`
- Create: `src/Frank.Auth/HandlerBuilderExtensions.fs`
- Modify: `src/Frank.Auth/Frank.Auth.fsproj:9-20`
- Modify: `test/Frank.Auth.Tests/AuthorizationTests.fs` (append after line 341, the last line)

**Interfaces:**
- Consumes: `EndpointAuth.applyAuthToHandler` and `EndpointAuth.toMetadataObjects` (Task 1), `Frank.Builder.HandlerBuilder`/`HandlerDefinition`/`HandlerDefinition.addMetadata`, `Frank.Auth.AuthConfig`/`AuthRequirement`, `Microsoft.AspNetCore.Authorization.AllowAnonymousAttribute`.
- Produces: `HandlerBuilder` custom operations `requireAuth`, `requireClaim` (two overloads), `requireRole`, `requirePolicy`, `allowAnonymous`, usable inside any `handler { ... }` block anywhere `Frank.Auth` is opened. No later task consumes these directly by name — they're exercised end-to-end via `resource { ... get (handler { ... }) ... }` composition, including in Task 4's integration test.

- [ ] **Step 1: Write the failing tests**

Append to `test/Frank.Auth.Tests/AuthorizationTests.fs`, after the existing `edgeCaseTests` list (after line 341):

```fsharp

// ===== Handler-Level Authorization (#476) =====

[<Tests>]
let handlerLevelTests =
    testList "Handler-Level Authorization" [
        testTask "resource is public but one handler requires a role -> other methods unaffected" {
            let r =
                resource "/widgets" {
                    name "Widgets"
                    get simpleHandler
                    delete (handler {
                        requireRole "admin"
                        handle (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("deleted"))
                    })
                }
            let client = createAuthTestServer [ r ] ignore

            let! (getResponse: HttpResponseMessage) = sendRequest client HttpMethod.Get "/widgets" None None None
            Expect.equal getResponse.StatusCode HttpStatusCode.OK "GET stays public"

            let! (deleteAnon: HttpResponseMessage) = sendRequest client HttpMethod.Delete "/widgets" None None None
            Expect.equal deleteAnon.StatusCode HttpStatusCode.Unauthorized "Unauthenticated DELETE is rejected"

            let! (deleteWrongRole: HttpResponseMessage) =
                sendRequest client HttpMethod.Delete "/widgets" (Some "alice") None (Some "user")
            Expect.equal deleteWrongRole.StatusCode HttpStatusCode.Forbidden "DELETE without admin role is rejected"

            let! (deleteAdmin: HttpResponseMessage) =
                sendRequest client HttpMethod.Delete "/widgets" (Some "alice") None (Some "admin")
            Expect.equal deleteAdmin.StatusCode HttpStatusCode.OK "DELETE with admin role succeeds"
        }

        testTask "resource-level requireAuth composes (AND) with handler-level requireRole" {
            let r =
                resource "/reports" {
                    name "Reports"
                    requireAuth
                    get simpleHandler
                    delete (handler {
                        requireRole "admin"
                        handle (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("deleted"))
                    })
                }
            let client = createAuthTestServer [ r ] ignore

            let! (getAnon: HttpResponseMessage) = sendRequest client HttpMethod.Get "/reports" None None None
            Expect.equal getAnon.StatusCode HttpStatusCode.Unauthorized "GET still requires resource-level auth"

            let! (getAuthed: HttpResponseMessage) = sendRequest client HttpMethod.Get "/reports" (Some "alice") None None
            Expect.equal getAuthed.StatusCode HttpStatusCode.OK "Authenticated GET succeeds, no role needed"

            let! (deleteAuthedNoRole: HttpResponseMessage) =
                sendRequest client HttpMethod.Delete "/reports" (Some "alice") None None
            Expect.equal deleteAuthedNoRole.StatusCode HttpStatusCode.Forbidden "Authenticated but non-admin DELETE is rejected"

            let! (deleteAdmin: HttpResponseMessage) =
                sendRequest client HttpMethod.Delete "/reports" (Some "alice") None (Some "admin")
            Expect.equal deleteAdmin.StatusCode HttpStatusCode.OK "Authenticated admin DELETE succeeds"
        }

        testTask "handler-level allowAnonymous overrides resource-level requireAuth for that method only" {
            let r =
                resource "/profile" {
                    name "Profile"
                    requireAuth
                    get (handler {
                        allowAnonymous
                        handle (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("public-summary"))
                    })
                    put simpleHandler
                }
            let client = createAuthTestServer [ r ] ignore

            let! (getAnon: HttpResponseMessage) = sendRequest client HttpMethod.Get "/profile" None None None
            Expect.equal getAnon.StatusCode HttpStatusCode.OK "allowAnonymous opens GET despite resource-level requireAuth"

            let! (putAnon: HttpResponseMessage) = sendRequest client HttpMethod.Put "/profile" None None None
            Expect.equal putAnon.StatusCode HttpStatusCode.Unauthorized "PUT still requires resource-level auth"
        }

        testTask "allowAnonymous wins outright even alongside a handler-level requireRole on the same handler" {
            // Documents ASP.NET Core's real behavior: IAllowAnonymous
            // short-circuits before any IAuthorizeData/policy is evaluated,
            // so a co-declared requireRole never runs. Not a "reset and
            // reapply" -- a full bypass.
            let r =
                resource "/mixed" {
                    name "Mixed"
                    get (handler {
                        allowAnonymous
                        requireRole "admin"
                        handle (fun (ctx: HttpContext) -> ctx.Response.WriteAsync("open"))
                    })
                }
            let client = createAuthTestServer [ r ] ignore

            let! (response: HttpResponseMessage) = sendRequest client HttpMethod.Get "/mixed" None None None
            Expect.equal response.StatusCode HttpStatusCode.OK "AllowAnonymous bypasses the co-declared role requirement"
        }
    ]
```

- [ ] **Step 2: Run the tests to verify they fail to build**

Run: `dotnet test test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj --filter "FullyQualifiedName~Handler-Level"`
Expected: Build error — `requireRole`/`allowAnonymous` are not recognized as custom operations inside `handler { ... }` (the `HandlerBuilder` type has no such members yet).

- [ ] **Step 3: Create `HandlerBuilderExtensions.fsi`**

```fsharp
namespace Frank.Auth

open Frank.Builder

[<AutoOpen>]
module HandlerBuilderExtensions =
    type HandlerBuilder with
        [<CustomOperation("requireAuth")>]
        member RequireAuth: def: HandlerDefinition -> HandlerDefinition

        [<CustomOperation("requireClaim")>]
        member RequireClaim: def: HandlerDefinition * claimType: string * claimValue: string -> HandlerDefinition

        member RequireClaim: def: HandlerDefinition * claimType: string * claimValues: string list -> HandlerDefinition

        [<CustomOperation("requireRole")>]
        member RequireRole: def: HandlerDefinition * role: string -> HandlerDefinition

        [<CustomOperation("requirePolicy")>]
        member RequirePolicy: def: HandlerDefinition * policyName: string -> HandlerDefinition

        /// Bypasses all authorization for this one handler -- resource-level
        /// and handler-level requirements alike -- via stock ASP.NET Core
        /// IAllowAnonymous semantics. See CLAUDE.md and the design doc for
        /// why this is a full bypass rather than a policy downgrade.
        [<CustomOperation("allowAnonymous")>]
        member AllowAnonymous: def: HandlerDefinition -> HandlerDefinition
```

- [ ] **Step 4: Create `HandlerBuilderExtensions.fs`**

```fsharp
namespace Frank.Auth

open Microsoft.AspNetCore.Authorization
open Frank.Builder

[<AutoOpen>]
module HandlerBuilderExtensions =
    type HandlerBuilder with
        [<CustomOperation("requireAuth")>]
        member _.RequireAuth(def: HandlerDefinition) : HandlerDefinition =
            let config = AuthConfig.empty |> AuthConfig.addRequirement AuthRequirement.Authenticated
            EndpointAuth.applyAuthToHandler config def

        [<CustomOperation("requireClaim")>]
        member _.RequireClaim(def: HandlerDefinition, claimType: string, claimValue: string) : HandlerDefinition =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Claim(claimType, [ claimValue ]))
            EndpointAuth.applyAuthToHandler config def

        member _.RequireClaim(def: HandlerDefinition, claimType: string, claimValues: string list) : HandlerDefinition =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Claim(claimType, claimValues))
            EndpointAuth.applyAuthToHandler config def

        [<CustomOperation("requireRole")>]
        member _.RequireRole(def: HandlerDefinition, role: string) : HandlerDefinition =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Role role)
            EndpointAuth.applyAuthToHandler config def

        [<CustomOperation("requirePolicy")>]
        member _.RequirePolicy(def: HandlerDefinition, policyName: string) : HandlerDefinition =
            let config = AuthConfig.empty |> AuthConfig.addRequirement (AuthRequirement.Policy policyName)
            EndpointAuth.applyAuthToHandler config def

        [<CustomOperation("allowAnonymous")>]
        member _.AllowAnonymous(def: HandlerDefinition) : HandlerDefinition =
            HandlerDefinition.addMetadata (AllowAnonymousAttribute()) def
```

- [ ] **Step 5: Add both files to `Frank.Auth.fsproj`**

Edit `src/Frank.Auth/Frank.Auth.fsproj`, inserting the two new items between `ResourceBuilderExtensions.fs` and `WebHostBuilderExtensions.fsi`:

```xml
  <ItemGroup>
    <Compile Include="AuthRequirement.fsi" />
    <Compile Include="AuthRequirement.fs" />
    <Compile Include="AuthConfig.fsi" />
    <Compile Include="AuthConfig.fs" />
    <Compile Include="EndpointAuth.fsi" />
    <Compile Include="EndpointAuth.fs" />
    <Compile Include="ResourceBuilderExtensions.fsi" />
    <Compile Include="ResourceBuilderExtensions.fs" />
    <Compile Include="HandlerBuilderExtensions.fsi" />
    <Compile Include="HandlerBuilderExtensions.fs" />
    <Compile Include="WebHostBuilderExtensions.fsi" />
    <Compile Include="WebHostBuilderExtensions.fs" />
  </ItemGroup>
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj --filter "FullyQualifiedName~Handler-Level"`
Expected: PASS, all 4 scenarios.

- [ ] **Step 7: Run the full `Frank.Auth.Tests` suite to check for regressions**

Run: `dotnet test test/Frank.Auth.Tests/Frank.Auth.Tests.fsproj`
Expected: PASS, every test (US1-US6, edge cases, `EndpointAuthTests`, and the new handler-level list).

- [ ] **Step 8: Verify the new `.fsi` builds across every targeted TFM**

Run: `dotnet build src/Frank.Auth/Frank.Auth.fsproj -f net8.0` then `-f net9.0` then `-f net10.0`
Expected: All three succeed.

- [ ] **Step 9: Commit**

```bash
git add src/Frank.Auth/HandlerBuilderExtensions.fs src/Frank.Auth/HandlerBuilderExtensions.fsi src/Frank.Auth/Frank.Auth.fsproj test/Frank.Auth.Tests/AuthorizationTests.fs
git commit -m "feat(auth): add handler-level requireAuth/requireClaim/requireRole/requirePolicy/allowAnonymous (#476)"
```

---

## Task 3: `Frank.JsonHome` — retain endpoint metadata per HTTP method

**Files:**
- Modify: `src/Frank.JsonHome/ApiSurface.fsi:1-29` (whole file)
- Modify: `src/Frank.JsonHome/ApiSurface.fs:58-108` (the `ResourceDescription` record and its construction in `ofApiDescriptions`)
- Modify: `test/Frank.JsonHome.Tests/ApiSurfaceTests.fs` (append after line 78, the last line)
- Modify: `test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs:15-29` (the `describe` helper — compile fix only, no behavior change in this task)

**Interfaces:**
- Consumes: `Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription`, the existing private `metadataOf` function in `ApiSurface.fs`.
- Produces: a new field on `ResourceDescription`: `MethodMetadata: (string * obj list) list` — one `(httpMethod, endpointMetadata)` pair per `ApiDescription` in the route-template group, in group order. Consumed by Task 4's `AuthorizationFilter` rewrite.

- [ ] **Step 1: Write the failing test**

Append to `test/Frank.JsonHome.Tests/ApiSurfaceTests.fs`, after line 78 (inside the `tests` list — add a comma after the previous entry's closing `}` and before the closing `]`):

```fsharp

          test "method metadata is retained per HTTP method, not merged" {
              let getMetadata: obj list = [ { Rel = "tag:example.com,2026:products" }; box "get-marker" ]
              let postMetadata: obj list = [ { Rel = "tag:example.com,2026:products" }; box "post-marker" ]

              let surface =
                  ApiSurface.ofApiDescriptions
                      [ describe "products" "GET" getMetadata
                        describe "products" "POST" postMetadata ]

              Expect.hasLength surface 1 "One resource"

              let getEntry = surface.[0].MethodMetadata |> List.find (fun (m, _) -> m = "GET") |> snd
              let postEntry = surface.[0].MethodMetadata |> List.find (fun (m, _) -> m = "POST") |> snd

              Expect.contains getEntry (box "get-marker") "GET keeps its own marker"
              Expect.isFalse (getEntry |> List.contains (box "post-marker")) "GET does not see POST's marker"
              Expect.contains postEntry (box "post-marker") "POST keeps its own marker"
              Expect.isFalse (postEntry |> List.contains (box "get-marker")) "POST does not see GET's marker"
          } ]
```

(This replaces the file's final `} ]` with `} ...new test... } ]` — the trailing `]` moves to the end of the new test.)

- [ ] **Step 2: Run the test to verify it fails to build**

Run: `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter "FullyQualifiedName~ApiSurface"`
Expected: Build error — `ResourceDescription` has no field `MethodMetadata`.

- [ ] **Step 3: Add the field to `ApiSurface.fsi`**

In `src/Frank.JsonHome/ApiSurface.fsi`, add the field after `Metadata`:

```fsharp
      /// Endpoint metadata, retained for authorization filtering.
      Metadata: obj list
      /// Endpoint metadata for each HTTP method registered on this resource,
      /// retained separately from Metadata so authorization can be evaluated
      /// (and Methods/Accepts/Formats filtered) per method rather than merged
      /// across the whole resource.
      MethodMetadata: (string * obj list) list }
```

- [ ] **Step 4: Add the field to `ApiSurface.fs`**

In `src/Frank.JsonHome/ApiSurface.fs`, the `ResourceDescription` type gets the same field added (mirroring the `.fsi`), and the record construction inside `ofApiDescriptions` (around line 107, right after `Metadata = metadata`) gets:

```fsharp
                      Metadata = metadata
                      MethodMetadata = group |> List.map (fun d -> d.HttpMethod, metadataOf d) }
```

- [ ] **Step 5: Fix the compile break in `AuthorizationFilterTests.fs`**

`ResourceDescription` record literals elsewhere in the codebase now need the new field. Edit the `describe` helper in `test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs` (lines 15-29):

```fsharp
let private describe rel (metadata: obj list) =
    { Rel = rel
      Href = "/" + rel
      IsTemplated = false
      HrefVars = []
      Methods = [ "GET" ]
      Formats = []
      Accepts = []
      AcceptRanges = []
      AcceptPrefer = []
      PreconditionRequired = []
      AuthSchemes = []
      Docs = None
      Status = None
      Metadata = metadata
      MethodMetadata = [ "GET", metadata ] }
```

This is a compile fix only — `AuthorizationFilter.fs` doesn't read `MethodMetadata` yet (that's Task 4), so this file's existing tests must still pass unchanged after this edit.

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter "FullyQualifiedName~ApiSurface"`
Expected: PASS, including the new "method metadata is retained per HTTP method" test.

- [ ] **Step 7: Run the full `Frank.JsonHome.Tests` suite to check for regressions**

Run: `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`
Expected: PASS, everything green (the `AuthorizationFilterTests.fs` edit was a pure compile fix, so its existing 4 tests must be unaffected).

- [ ] **Step 8: Verify the new `.fsi` builds across every targeted TFM**

Run: `dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj -f net8.0` then `-f net9.0` then `-f net10.0`
Expected: All three succeed.

- [ ] **Step 9: Commit**

```bash
git add src/Frank.JsonHome/ApiSurface.fs src/Frank.JsonHome/ApiSurface.fsi test/Frank.JsonHome.Tests/ApiSurfaceTests.fs test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs
git commit -m "feat(jsonhome): retain endpoint metadata per HTTP method in ApiSurface"
```

---

## Task 4: `AuthorizationFilter` — per-method evaluation, `IAllowAnonymous`, drop-on-empty

**Files:**
- Modify: `src/Frank.JsonHome/AuthorizationFilter.fsi` (whole file, currently 15 lines)
- Modify: `src/Frank.JsonHome/AuthorizationFilter.fs` (whole file, currently 62 lines)
- Modify: `test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs` (refactor `describe` into `describeMethods` + `describe`, append new tests after line 29 (post-Task-3) content)
- Modify: `test/Frank.JsonHome.Tests/IntegrationTests.fs` (append after line 276, the last line)

**Interfaces:**
- Consumes: `ResourceDescription.MethodMetadata` (Task 3), `Frank.Auth`'s `requireRole`/`handler`/`handle` (Tasks 1-2) for the integration test, `Microsoft.AspNetCore.Authorization.IAllowAnonymous`/`IAuthorizeData`/`AuthorizationPolicy`/`IAuthorizationService`.
- Produces: `AuthorizationFilter.apply` keeps its existing signature (`ctx: HttpContext -> resources: ResourceDescription list -> Task<ResourceDescription list>`) but changes behavior: filters `Methods`/`Accepts`/`Formats` per method instead of including/excluding whole resources, and drops a resource entirely once its `Methods` becomes empty. `varies` keeps its existing signature and behavior unchanged.

- [ ] **Step 1: Write the failing tests**

Replace the `describe` helper in `test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs` (the version from Task 3, lines 15-30) with:

```fsharp
let private describeMethods rel (methodMetadata: (string * obj list) list) =
    { Rel = rel
      Href = "/" + rel
      IsTemplated = false
      HrefVars = []
      Methods = methodMetadata |> List.map fst
      Formats = []
      Accepts = []
      AcceptRanges = []
      AcceptPrefer = []
      PreconditionRequired = []
      AuthSchemes = []
      Docs = None
      Status = None
      Metadata = methodMetadata |> List.collect snd
      MethodMetadata = methodMetadata }

let private describe rel (metadata: obj list) =
    describeMethods rel [ "GET", metadata ]
```

Then append these tests to the `tests` list in the same file (after the existing "evaluation failures deny rather than throw or fail open" test, before its closing `] `):

```fsharp

          testTask "methods are filtered independently -- a mixed resource keeps only what the principal can reach" {
              let ctx = contextFor []
              let mixed = describeMethods "widgets" [ "GET", []; "DELETE", [ AuthorizeAttribute(); adminOnly ] ]

              let! result = AuthorizationFilter.apply ctx [ mixed ]

              Expect.hasLength result 1 "Resource still present"
              Expect.equal result.[0].Methods [ "GET" ] "Only the public method survives"
          }

          testTask "a resource with every method hidden is dropped entirely" {
              let ctx = contextFor []
              let guarded = describeMethods "admin-only" [ "GET", [ AuthorizeAttribute(); adminOnly ] ]

              let! result = AuthorizationFilter.apply ctx [ guarded ]

              Expect.isEmpty result "Resource with zero visible methods does not appear"
          }

          testTask "AllowAnonymous on one method keeps it visible even when the resource is otherwise restricted" {
              let ctx = contextFor []
              let mixed =
                  describeMethods
                      "settings"
                      [ "GET", [ AllowAnonymousAttribute() ]
                        "PUT", [ AuthorizeAttribute(); adminOnly ] ]

              let! result = AuthorizationFilter.apply ctx [ mixed ]

              Expect.hasLength result 1 "Resource still present"
              Expect.equal result.[0].Methods [ "GET" ] "AllowAnonymous method survives, restricted one doesn't"
          }

          testTask "Accepts entries are filtered to the methods that remain visible" {
              let ctx = contextFor []
              let mixed =
                  { describeMethods "orders" [ "GET", []; "POST", [ AuthorizeAttribute(); adminOnly ] ] with
                      Accepts = [ "GET", [ "text/html" ]; "POST", [ "application/json" ] ] }

              let! result = AuthorizationFilter.apply ctx [ mixed ]

              Expect.equal result.[0].Methods [ "GET" ] "Only GET remains"
              Expect.equal result.[0].Accepts [ "GET", [ "text/html" ] ] "POST's accept entry is dropped with it"
          }

          testTask "Formats is cleared once GET is no longer visible" {
              let ctx = contextFor []
              let mixed =
                  { describeMethods "orders" [ "GET", [ AuthorizeAttribute(); adminOnly ]; "POST", [] ] with
                      Formats = [ "application/json" ] }

              let! result = AuthorizationFilter.apply ctx [ mixed ]

              Expect.equal result.[0].Methods [ "POST" ] "Only POST remains"
              Expect.isEmpty result.[0].Formats "Formats was derived from the now-hidden GET"
          } ]
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter "FullyQualifiedName~AuthorizationFilter"`
Expected: The pre-existing 4 tests still pass (resource-wide behavior, driven by `Metadata`), but the 5 new ones FAIL — today's `apply` evaluates `resource.Metadata` as a whole and never looks at `resource.MethodMetadata`, so mixed-visibility resources currently come back with all methods still present (or the whole resource dropped), not filtered per method.

- [ ] **Step 3: Rewrite `AuthorizationFilter.fsi`**

Replace the full contents of `src/Frank.JsonHome/AuthorizationFilter.fsi`:

```fsharp
namespace Frank.JsonHome

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

module AuthorizationFilter =

    /// True when any resource declares authorization requirements, meaning the
    /// document varies by principal and must not be cached by a shared cache.
    val varies: resources: ResourceDescription list -> bool

    /// Filters each resource's Methods -- and the Accepts/Formats hints
    /// derived from them -- down to what the current principal can call,
    /// evaluating authorization per HTTP method rather than per resource. A
    /// method carrying IAllowAnonymous metadata is always kept. A resource
    /// left with no visible methods is dropped entirely. Evaluation failures
    /// deny that method rather than throw or fail open.
    val apply: ctx: HttpContext -> resources: ResourceDescription list -> Task<ResourceDescription list>
```

- [ ] **Step 4: Rewrite `AuthorizationFilter.fs`**

Replace the full contents of `src/Frank.JsonHome/AuthorizationFilter.fs`:

```fsharp
namespace Frank.JsonHome

open Microsoft.AspNetCore.Authorization
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.DependencyInjection

module AuthorizationFilter =

    let private authorizeData (metadata: obj list) =
        metadata
        |> List.choose (fun m ->
            match m with
            | :? IAuthorizeData as d -> Some d
            | _ -> None)

    let private policies (metadata: obj list) =
        metadata
        |> List.choose (fun m ->
            match m with
            | :? AuthorizationPolicy as p -> Some p
            | _ -> None)

    let private isAnonymous (metadata: obj list) =
        metadata |> List.exists (fun m -> m :? IAllowAnonymous)

    let varies (resources: ResourceDescription list) =
        resources |> List.exists (fun r -> not (List.isEmpty (authorizeData r.Metadata)))

    let private resolvePolicy (ctx: HttpContext) (metadata: obj list) =
        task {
            match policies metadata with
            | [] ->
                let provider = ctx.RequestServices.GetRequiredService<IAuthorizationPolicyProvider>()
                return! AuthorizationPolicy.CombineAsync(provider, authorizeData metadata)
            | explicitPolicies -> return AuthorizationPolicy.Combine(explicitPolicies)
        }

    let private isMethodAllowed (ctx: HttpContext) (metadata: obj list) =
        task {
            if isAnonymous metadata then
                return true
            elif List.isEmpty (authorizeData metadata) then
                return true
            else
                try
                    match! resolvePolicy ctx metadata with
                    | null -> return true
                    | policy ->
                        let service = ctx.RequestServices.GetRequiredService<IAuthorizationService>()
                        let! result = service.AuthorizeAsync(ctx.User, box metadata, policy)
                        return result.Succeeded
                with _ ->
                    // Fail closed: an evaluation error must never widen access.
                    return false
        }

    let private allowedMethods (ctx: HttpContext) (resource: ResourceDescription) =
        task {
            let allowed = ResizeArray()

            for httpMethod, metadata in resource.MethodMetadata do
                let! ok = isMethodAllowed ctx metadata
                if ok then allowed.Add httpMethod

            return Set.ofSeq allowed
        }

    let private filterResource (ctx: HttpContext) (resource: ResourceDescription) =
        task {
            let! allowed = allowedMethods ctx resource
            let methods = resource.Methods |> List.filter (fun m -> Set.contains m allowed)

            if List.isEmpty methods then
                return None
            else
                return
                    Some
                        { resource with
                            Methods = methods
                            Accepts = resource.Accepts |> List.filter (fun (m, _) -> Set.contains m allowed)
                            Formats = if Set.contains "GET" allowed then resource.Formats else [] }
        }

    let apply (ctx: HttpContext) (resources: ResourceDescription list) =
        task {
            let kept = ResizeArray()

            for resource in resources do
                match! filterResource ctx resource with
                | Some filtered -> kept.Add filtered
                | None -> ()

            return List.ofSeq kept
        }
```

- [ ] **Step 5: Run the `AuthorizationFilter` tests to verify they pass**

Run: `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter "FullyQualifiedName~AuthorizationFilter"`
Expected: PASS, all 9 tests (4 pre-existing + 5 new).

- [ ] **Step 6: Write the end-to-end integration test**

Append to `test/Frank.JsonHome.Tests/IntegrationTests.fs`, after line 276 (the last line, inside the `tests` list — add a comma after the previous entry and before the closing `]`):

```fsharp

          testTask "hints.allow reflects only the methods the current principal can call" {
              let widgets =
                  resource "/widgets" {
                      rel "tag:example.com,2026:widgets"
                      get ok
                      delete (handler {
                          requireRole "admin"
                          handle (fun (ctx: HttpContext) -> ctx.Response.WriteAsync "OK")
                      })
                  }

              let client = createServer options [ widgets ]
              let allowFor (response: HttpResponseMessage) =
                  task {
                      let! body = response.Content.ReadAsStringAsync()
                      let root = JsonDocument.Parse(body).RootElement
                      let resource = root.GetProperty("resources").GetProperty("tag:example.com,2026:widgets")
                      let allow = resource.GetProperty("hints").GetProperty("allow")
                      return [ for e in allow.EnumerateArray() -> e.GetString() ]
                  }

              let! (anonymous: HttpResponseMessage) = client.GetAsync options.Path
              let! anonymousAllow = allowFor anonymous
              Expect.equal anonymousAllow [ "GET" ] "Anonymous request sees only GET"

              let request = new HttpRequestMessage(HttpMethod.Get, options.Path)
              request.Headers.Add("X-Test-User", "alice")
              request.Headers.Add("X-Test-Roles", "admin")
              let! (asAdmin: HttpResponseMessage) = client.SendAsync request
              let! adminAllow = allowFor asAdmin
              Expect.equal adminAllow [ "GET"; "DELETE" ] "Admin request sees both methods"
          } ]
```

- [ ] **Step 7: Run the integration test to verify it passes**

Run: `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj --filter "FullyQualifiedName~JSON+Home+integration"`
Expected: PASS.

- [ ] **Step 8: Run the full `Frank.JsonHome.Tests` suite to check for regressions**

Run: `dotnet test test/Frank.JsonHome.Tests/Frank.JsonHome.Tests.fsproj`
Expected: PASS, everything green.

- [ ] **Step 9: Verify the new `.fsi` builds across every targeted TFM**

Run: `dotnet build src/Frank.JsonHome/Frank.JsonHome.fsproj -f net8.0` then `-f net9.0` then `-f net10.0`
Expected: All three succeed.

- [ ] **Step 10: Full-solution regression check**

Run: `dotnet build Frank.sln`
Expected: Succeeds — no other project (`Frank.OpenApi`, sample apps, etc.) constructs a `ResourceDescription` record literal or depends on `AuthorizationFilter`'s old resource-wide behavior.

- [ ] **Step 11: Commit**

```bash
git add src/Frank.JsonHome/AuthorizationFilter.fs src/Frank.JsonHome/AuthorizationFilter.fsi test/Frank.JsonHome.Tests/AuthorizationFilterTests.fs test/Frank.JsonHome.Tests/IntegrationTests.fs
git commit -m "feat(jsonhome): filter hints.allow per HTTP method, honor IAllowAnonymous, drop fully-hidden resources"
```

---

## Post-implementation

Update the design doc's status line (`docs/superpowers/specs/2026-07-29-handler-auth-design.md`, currently "Draft — awaiting review") to "Implemented" once all four tasks are committed, in a small final commit.
