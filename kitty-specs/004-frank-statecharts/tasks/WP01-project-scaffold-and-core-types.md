---
work_package_id: "WP01"
subtasks:
  - "T001"
  - "T002"
  - "T003"
  - "T004"
  - "T005"
title: "Project Scaffold & Core Types"
phase: "Phase 1 - Foundation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: []
requirement_refs: ["FR-002", "FR-006"]
history:
  - timestamp: "2026-03-06T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP01 -- Project Scaffold & Core Types

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP01
```

No dependencies -- this is the starting package.

---

## Objectives & Success Criteria

- Create `Frank.Statecharts` library project and `Frank.Statecharts.Tests` test project
- Define all core DU types: `StateMachine<'S,'E,'C>`, `TransitionResult`, `BlockReason`, `Guard`, `GuardContext`, `GuardResult`, `StateInfo`
- Define `IStateMachineStore<'S,'C>` interface
- Both projects compile; types are usable from test project
- All projects added to `Frank.sln`

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/003-statecharts-feasibility-research/plan.md` -- Project structure, design decisions
- `kitty-specs/003-statecharts-feasibility-research/data-model.md` -- Entity definitions and relationships
- `kitty-specs/003-statecharts-feasibility-research/research.md` -- Proposed API surface

**Key constraints**:
- Follow `src/Frank.Auth/Frank.Auth.fsproj` structure for project configuration
- Multi-target: `net8.0;net9.0;net10.0` for library, `net10.0` for tests
- Project reference to Frank (NOT NuGet), like other extension libraries
- `[<Struct>]` on `BlockReason` and `GuardResult` (matching Frank core patterns)
- `'State : equality` constraint for Map-based lookup
- Types.fs must be first in compilation order (all other files depend on it)

---

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `Frank.Statecharts.fsproj`

**Purpose**: Scaffold the library project with correct multi-targeting and project references.

**Steps**:
1. Create directory `src/Frank.Statecharts/`
2. Create `Frank.Statecharts.fsproj` with this structure:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>
    <PackageTags>statecharts;state-machine;resource</PackageTags>
    <Description>Statechart-based resource state machine extensions for Frank web framework</Description>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Types.fs" />
    <Compile Include="Store.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../Frank/Frank.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <FrameworkReference Include="Microsoft.AspNetCore.App" />
  </ItemGroup>

</Project>
```

3. Add the project to `Frank.sln`:
   ```bash
   dotnet sln Frank.sln add src/Frank.Statecharts/Frank.Statecharts.fsproj
   ```

**Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
**Notes**: The `<Compile>` list will be extended in later WPs as files are added. Start with just `Types.fs` and `Store.fs`. Check that `src/Directory.Build.props` applies (it has shared packaging props like VersionPrefix).

### Subtask T002 -- Create `Types.fs` with core DUs

**Purpose**: Define all core discriminated union types and records that the entire library depends on.

**Steps**:
1. Create `src/Frank.Statecharts/Types.fs`
2. Use namespace `Frank.Statecharts`
3. Define the following types in this order:

```fsharp
namespace Frank.Statecharts

open System.Security.Claims

/// Why a guard blocked a transition. Maps to HTTP status codes in middleware.
[<Struct>]
type BlockReason =
    | NotAllowed          // 403 Forbidden
    | NotYourTurn         // 409 Conflict
    | InvalidTransition   // 400 Bad Request
    | PreconditionFailed  // 412 Precondition Failed
    | Custom of code: int * message: string

/// Result of evaluating a guard predicate.
[<Struct>]
type GuardResult =
    | Allowed
    | Blocked of reason: BlockReason

/// Context passed to guard predicates for evaluation.
type GuardContext<'State, 'Event> =
    { User: ClaimsPrincipal
      CurrentState: 'State
      Event: 'Event }

/// A named guard predicate.
type Guard<'State, 'Event, 'Context> =
    { Name: string
      Predicate: GuardContext<'State, 'Event> -> GuardResult }

/// Metadata about a single state (HTTP configuration).
type StateInfo =
    { AllowedMethods: string list
      IsFinal: bool
      Description: string option }

/// The outcome of a transition attempt.
type TransitionResult<'State, 'Context> =
    | Transitioned of state: 'State * context: 'Context
    | Blocked of reason: BlockReason
    | Invalid of message: string

/// Compile-time definition of a state machine.
type StateMachine<'State, 'Event, 'Context when 'State : equality> =
    { Initial: 'State
      Transition: 'State -> 'Event -> 'Context -> TransitionResult<'State, 'Context>
      Guards: Guard<'State, 'Event, 'Context> list
      StateMetadata: Map<'State, StateInfo> }
```

**Files**: `src/Frank.Statecharts/Types.fs`
**Notes**:
- `BlockReason` and `GuardResult` are `[<Struct>]` to avoid heap allocation on the hot path
- `'State : equality` constraint is required for `Map<'State, StateInfo>` lookup
- `GuardContext` includes `ClaimsPrincipal` for per-user discrimination (bridge to Frank.Auth)
- `TransitionResult.Blocked` uses `BlockReason` directly, not `GuardResult`, since middleware converts after guard evaluation
- Note the name collision: `TransitionResult.Blocked` and `GuardResult.Blocked` both exist. F# handles this via qualified access. Consider adding `[<RequireQualifiedAccess>]` on `TransitionResult` if ambiguity arises in practice.

### Subtask T003 -- Create `Store.fs` with `IStateMachineStore` interface

**Purpose**: Define the store abstraction that WP02 will implement.

**Steps**:
1. Create `src/Frank.Statecharts/Store.fs`
2. Define the interface:

```fsharp
namespace Frank.Statecharts

open System
open System.Threading.Tasks

/// Abstraction for state machine instance persistence.
type IStateMachineStore<'State, 'Context when 'State : equality> =
    /// Retrieve the current state and context for an instance.
    /// Returns None if the instance doesn't exist yet.
    abstract GetState: instanceId: string -> Task<('State * 'Context) option>

    /// Persist a state change for an instance.
    abstract SetState: instanceId: string -> state: 'State -> context: 'Context -> Task<unit>

    /// Subscribe to state changes for an instance.
    /// Returns an IDisposable that unsubscribes when disposed.
    /// BehaviorSubject semantics: new subscribers immediately receive current state.
    abstract Subscribe: instanceId: string -> observer: IObserver<'State * 'Context> -> IDisposable
```

**Files**: `src/Frank.Statecharts/Store.fs`
**Notes**:
- The interface is generic with the same `'State : equality` constraint
- `Subscribe` returns `IDisposable` -- callers MUST use `use` (disposal discipline from constitution)
- `GetState` returns `Task<option>` -- `None` means new instance, middleware will use `StateMachine.Initial`
- The actual `MailboxProcessorStore` implementation is WP02

### Subtask T004 -- Create test project

**Purpose**: Scaffold the test project so types can be validated.

**Steps**:
1. Create directory `test/Frank.Statecharts.Tests/`
2. Create `Frank.Statecharts.Tests.fsproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
    <GenerateProgramFile>false</GenerateProgramFile>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="TypeTests.fs" />
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
    <PackageReference Include="Expecto" Version="10.2.3" />
    <PackageReference Include="YoloDev.Expecto.TestSdk" Version="0.14.3" />
    <PackageReference Include="Microsoft.AspNetCore.TestHost" Version="10.0.0" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="../../src/Frank.Statecharts/Frank.Statecharts.fsproj" />
  </ItemGroup>

</Project>
```

3. Create `test/Frank.Statecharts.Tests/Program.fs`:

```fsharp
module Program

open Expecto

[<EntryPoint>]
let main args =
    runTestsInAssemblyWithCLIArgs [] args
```

4. Add to solution:
   ```bash
   dotnet sln Frank.sln add test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
   ```

**Files**: `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`, `test/Frank.Statecharts.Tests/Program.fs`
**Parallel?**: Yes -- can proceed in parallel with T002/T003 once T001 is done.

### Subtask T005 -- Create `TypeTests.fs` with unit tests

**Purpose**: Validate that all core types compile and behave correctly.

**Steps**:
1. Create `test/Frank.Statecharts.Tests/TypeTests.fs`
2. Write Expecto tests covering:

**a. BlockReason struct tests**:
- Verify each case can be constructed and pattern matched
- Verify `Custom` carries code and message

**b. GuardResult struct tests**:
- Construct `Allowed` and `Blocked` values
- Pattern match to extract BlockReason from Blocked

**c. TransitionResult tests**:
- Construct `Transitioned`, `Blocked`, and `Invalid`
- Pattern match to extract state/context from Transitioned

**d. StateMachine construction**:
- Define a simple 2-state machine (e.g., `Locked | Unlocked` with `Coin | Push` events)
- Verify transition function works: `Locked + Coin = Unlocked`, `Unlocked + Push = Locked`
- Verify `StateMetadata` map lookup by state key

**e. Guard evaluation**:
- Create a guard that checks `ClaimsPrincipal` for a specific claim
- Verify it returns `Allowed` for matching principal
- Verify it returns `Blocked NotAllowed` for non-matching principal

**Example test structure**:

```fsharp
module TypeTests

open Expecto
open Frank.Statecharts
open System.Security.Claims

type TurnstileState = Locked | Unlocked
type TurnstileEvent = Coin | Push

let transition state event (_ctx: unit) =
    match state, event with
    | Locked, Coin -> Transitioned(Unlocked, ())
    | Unlocked, Push -> Transitioned(Locked, ())
    | Locked, Push -> TransitionResult.Blocked(BlockReason.InvalidTransition)
    | Unlocked, Coin -> Transitioned(Unlocked, ())

let turnstileMachine : StateMachine<TurnstileState, TurnstileEvent, unit> =
    { Initial = Locked
      Transition = transition
      Guards = []
      StateMetadata = Map.ofList [
          Locked, { AllowedMethods = ["POST"]; IsFinal = false; Description = Some "Waiting for coin" }
          Unlocked, { AllowedMethods = ["POST"]; IsFinal = false; Description = Some "Ready to push" }
      ] }

[<Tests>]
let typeTests =
    testList "Core Types" [
        test "BlockReason Custom carries code and message" {
            let reason = Custom(429, "Rate limited")
            match reason with
            | Custom(code, msg) ->
                Expect.equal code 429 "code"
                Expect.equal msg "Rate limited" "message"
            | _ -> failtest "Expected Custom"
        }

        test "StateMachine transition Locked + Coin = Unlocked" {
            match turnstileMachine.Transition Locked Coin () with
            | Transitioned(Unlocked, ()) -> ()
            | other -> failtestf "Expected Transitioned(Unlocked), got %A" other
        }

        test "StateMachine transition Locked + Push = Blocked" {
            match turnstileMachine.Transition Locked Push () with
            | TransitionResult.Blocked(BlockReason.InvalidTransition) -> ()
            | other -> failtestf "Expected Blocked(InvalidTransition), got %A" other
        }

        test "StateMetadata lookup by state key" {
            let info = turnstileMachine.StateMetadata[Locked]
            Expect.equal info.AllowedMethods ["POST"] "allowed methods"
            Expect.isFalse info.IsFinal "not final"
        }

        test "Guard evaluates ClaimsPrincipal" {
            let adminGuard : Guard<TurnstileState, TurnstileEvent, unit> =
                { Name = "isAdmin"
                  Predicate = fun ctx ->
                      if ctx.User.IsInRole("Admin") then Allowed
                      else GuardResult.Blocked(NotAllowed) }

            let adminIdentity = ClaimsIdentity([Claim(ClaimTypes.Role, "Admin")], "test")
            let adminPrincipal = ClaimsPrincipal(adminIdentity)
            let ctx = { User = adminPrincipal; CurrentState = Locked; Event = Coin }
            Expect.equal (adminGuard.Predicate ctx) Allowed "admin should be allowed"
        }
    ]
```

**Files**: `test/Frank.Statecharts.Tests/TypeTests.fs`
**Parallel?**: Yes -- can proceed once T002 is done.
**Validation**: `dotnet test test/Frank.Statecharts.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build` on both projects to verify compilation
- Run `dotnet test test/Frank.Statecharts.Tests/` to verify all type tests pass
- Verify `dotnet build Frank.sln` succeeds (solution-level build)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| `TransitionResult.Blocked` / `GuardResult.Blocked` name collision | Use `[<RequireQualifiedAccess>]` on `TransitionResult` if F# inference struggles |
| `'State : equality` constraint propagation | Ensure constraint appears on all generic types that contain `'State` |
| `[<Struct>]` DU restrictions (single-case limits) | `BlockReason` and `GuardResult` are multi-case, which is fine for struct DUs in F# 8+ |

---

## Review Guidance

- Verify `.fsproj` files match Frank.Auth pattern (multi-target, project reference, framework reference)
- Verify Types.fs compilation order is first in `.fsproj`
- Verify `[<Struct>]` on `BlockReason` and `GuardResult`
- Verify `'State : equality` constraint on `StateMachine` and `IStateMachineStore`
- Verify test project follows `Frank.Auth.Tests` pattern exactly
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-06T00:00:00Z -- system -- lane=planned -- Prompt created.
