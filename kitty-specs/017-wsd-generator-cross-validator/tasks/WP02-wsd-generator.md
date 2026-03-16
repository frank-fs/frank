---
work_package_id: WP02
title: WSD Generator
lane: "for_review"
dependencies: []
base_branch: master
base_commit: 26152db61a6bfc6f2f54b873ecb7e6522997e677
created_at: '2026-03-16T04:02:51.698098+00:00'
subtasks:
- T007
- T008
- T009
- T010
- T011
- T012
phase: Phase 1b - Generator
assignee: ''
agent: "claude-opus"
shell_pid: "98705"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:06Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-001, FR-003, FR-004, FR-005, FR-006, FR-007, FR-009, FR-010, FR-011]
---

# Work Package Prompt: WP02 -- WSD Generator

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

No dependencies -- can run in parallel with WP01:
```
spec-kitty implement WP02
```

---

## Objectives & Success Criteria

- Create `src/Frank.Statecharts/Wsd/Generator.fs` that transforms `StateMachineMetadata` into a `Diagram` AST
- Generator must be a **pure function** returning `Result<Diagram, GeneratorError>` (SC-008, FR-011)
- Must extract states from `StateHandlerMap` keys and initial state from `InitialStateKey` (FR-003, FR-004)
- Must emit messages for each (state, HTTP method) handler pair (FR-005)
- Must use default `Solid + Forward` arrow style for all transitions (FR-006, DD-03)
- Must extract guards from boxed `Machine: obj` via reflection and emit `note over` annotations (FR-007)
- Must set `Diagram.Title` from `GenerateOptions.ResourceName` (FR-009)
- Must return `GeneratorError.UnrecognizedMachineType` for unrecognized boxed types (FR-011)
- The module is `internal` to `Frank.Statecharts` assembly (DD-06)
- All tests pass via `dotnet test test/Frank.Statecharts.Tests/`

## Context & Constraints

- **Spec**: `kitty-specs/017-wsd-generator-cross-validator/spec.md` -- FR-001 through FR-011
- **Plan**: `kitty-specs/017-wsd-generator-cross-validator/plan.md` -- DD-01 (two-phase pipeline), DD-02 (reflection strategy), DD-03 (default arrow), DD-04 (guard emission), DD-05 (state discovery), DD-06 (internal visibility)
- **Data Model**: `kitty-specs/017-wsd-generator-cross-validator/data-model.md` -- `GeneratorError`, `GenerateOptions`, `Generator.generate` signatures, field mapping table
- **Research**: `kitty-specs/017-wsd-generator-cross-validator/research.md` -- R-01 (extraction strategy), R-03 (guard annotation), R-04 (edge cases)

**Key Source Files** (read these for context):
- `src/Frank.Statecharts/Wsd/Types.fs` -- AST types the generator constructs
- `src/Frank.Statecharts/Types.fs` -- `StateMachine<'S,'E,'C>`, `Guard<'S,'E,'C>`, `StateInfo`, `GuardResult`, etc.
- `src/Frank.Statecharts/StatefulResourceBuilder.fs` -- `StateMachineMetadata` type definition (lines 38-56), how metadata is constructed from `StatefulResourceSpec` (lines 253-263)

**Key Constraints**:
- Generator depends on `Wsd.Types` AND `StateMachineMetadata` (which is in `Frank.Statecharts` namespace, defined in `StatefulResourceBuilder.fs`)
- The `Machine: obj` field is a boxed `StateMachine<'S,'E,'C>` -- use reflection to access the `Guards` field
- The `Transition` function is opaque (a closure) -- do NOT attempt to call or inspect it
- Guards are machine-wide (not per-transition) -- emit as `note over <initialState>`
- Guard values are unknown at generation time -- use `"*"` as wildcard: `[guard: role=*]`
- All `SourcePosition` fields use synthetic `{ Line = 0; Column = 0 }`

## Subtasks & Detailed Guidance

### Subtask T007 -- Create `Wsd/Generator.fs` module skeleton

**Purpose**: Establish the module file with error types, options, and the `generate` function signature.

**Steps**:
1. Create `src/Frank.Statecharts/Wsd/Generator.fs`
2. Use `module internal Frank.Statecharts.Wsd.Generator`
3. Open `Frank.Statecharts.Wsd.Types` and `Frank.Statecharts`
4. Define the error DU:
   ```fsharp
   type GeneratorError =
       | UnrecognizedMachineType of typeName: string
       | NoStatesFound of resourceName: string
   ```
5. Define the options record:
   ```fsharp
   type GenerateOptions =
       { ResourceName: string }
   ```
6. Define the `generate` function signature:
   ```fsharp
   let generate (options: GenerateOptions) (metadata: StateMachineMetadata) : Result<Diagram, GeneratorError> =
       ...
   ```

**Files**:
- `src/Frank.Statecharts/Wsd/Generator.fs` (new, ~100-150 lines when complete)

**Notes**: The module needs `open Frank.Statecharts` because `StateMachineMetadata` is defined in the `Frank.Statecharts` namespace (in `StatefulResourceBuilder.fs`). Also open `System.Reflection` and `FSharp.Reflection` for the guard extraction code.

---

### Subtask T008 -- Implement state discovery

**Purpose**: Extract participant names from `StateHandlerMap` keys, ensuring the initial state appears first (FR-004).

**Steps**:
1. Within `generate`, read `metadata.StateHandlerMap` -- keys are state name strings
2. Read `metadata.InitialStateKey` -- the initial state's string representation
3. Build the participant list:
   - First participant: the initial state
   - Remaining participants: all other states from `StateHandlerMap.Keys`, sorted alphabetically for deterministic output
   - If `InitialStateKey` is not found in `StateHandlerMap` keys, still include it as the first participant (it may exist as a state with no handlers)
4. Create `Participant` records with `Explicit = true` and synthetic position `{ Line = 0; Column = 0 }`
5. Create corresponding `ParticipantDecl` `DiagramElement` entries

**Implementation sketch**:
```fsharp
let syntheticPos = { Line = 0; Column = 0 }

let stateNames = metadata.StateHandlerMap |> Map.toList |> List.map fst

let orderedStates =
    let others = stateNames |> List.filter (fun s -> s <> metadata.InitialStateKey) |> List.sort
    metadata.InitialStateKey :: others

let participants =
    orderedStates
    |> List.map (fun name ->
        { Name = name; Alias = None; Explicit = true; Position = syntheticPos })

let participantElements =
    participants |> List.map ParticipantDecl
```

**Validation**:
- For a turnstile with states `["Locked"; "Unlocked"; "Broken"]` and initial `"Locked"`, participants should be `["Locked"; "Broken"; "Unlocked"]` (Locked first, then others alphabetically)

---

### Subtask T009 -- Implement transition discovery

**Purpose**: Emit `MessageElement` for each (state, HTTP method) handler pair from `StateHandlerMap` values (FR-005).

**Steps**:
1. For each state in `orderedStates`, look up its handlers from `metadata.StateHandlerMap`
2. Each handler is a `(string * RequestDelegate)` tuple where the first element is the HTTP method name (e.g., "GET", "POST")
3. Create a `Message` for each handler:
   - `Sender` = state name (the current state)
   - `Receiver` = state name (same state, since we cannot determine the target state from opaque transitions -- see DD-05)
   - `ArrowStyle` = `Solid` (DD-03)
   - `Direction` = `Forward` (DD-03)
   - `Label` = HTTP method name (e.g., "GET", "POST")
   - `Parameters` = `[]`
   - `Position` = synthetic position

**Important design note**: Since the `Transition` function is opaque, the generator cannot determine transition *targets*. Messages show which HTTP methods are available in each state as self-messages. This is the state-capability diagram described in DD-05 and research R-01. The sender and receiver are the same state.

**Implementation sketch**:
```fsharp
let messageElements =
    orderedStates
    |> List.collect (fun stateName ->
        match Map.tryFind stateName metadata.StateHandlerMap with
        | Some handlers ->
            handlers
            |> List.map (fun (httpMethod, _) ->
                MessageElement
                    { Sender = stateName
                      Receiver = stateName
                      ArrowStyle = Solid
                      Direction = Forward
                      Label = httpMethod
                      Parameters = []
                      Position = syntheticPos })
        | None -> [])
```

**Edge cases**:
- State with no handlers (empty list): No messages emitted for that state (participant only)
- State with multiple handlers: One message per handler

---

### Subtask T010 -- Implement guard extraction via reflection

**Purpose**: Unbox `Machine: obj`, check if it is a `StateMachine<_,_,_>`, extract `Guards` field, and emit guard annotations as `note over` elements (FR-007).

**Steps**:
1. Get the runtime type of `metadata.Machine` via `metadata.Machine.GetType()`
2. Check if the type is a generic type whose definition matches `StateMachine<_,_,_>`:
   ```fsharp
   let machineType = metadata.Machine.GetType()
   let isStateMachine =
       machineType.IsGenericType
       && machineType.GetGenericTypeDefinition() = typedefof<StateMachine<_,_,_>>
   ```
3. If NOT a `StateMachine<_,_,_>`, return `Error (UnrecognizedMachineType (machineType.FullName))`
4. If it IS a `StateMachine<_,_,_>`, extract the `Guards` field using F# reflection:
   ```fsharp
   let fields = FSharp.Reflection.FSharpValue.GetRecordFields(metadata.Machine)
   // StateMachine fields in declaration order:
   // 0: Initial, 1: InitialContext, 2: Transition, 3: Guards, 4: StateMetadata
   let guardsObj = fields.[3]
   ```
5. The `Guards` field is `Guard<'S,'E,'C> list`. Each `Guard` has a `Name: string` field. Extract guard names:
   ```fsharp
   // guardsObj is a boxed list -- use reflection to iterate
   let guardNames =
       match guardsObj with
       | :? System.Collections.IEnumerable as guards ->
           [ for g in guards do
               let nameField = g.GetType().GetProperty("Name")
               yield nameField.GetValue(g) :?> string ]
       | _ -> []
   ```
6. If guard names are non-empty, create a `NoteElement` with guard annotation:
   ```fsharp
   let guardElements =
       if guardNames.IsEmpty then []
       else
           let pairs = guardNames |> List.map (fun name -> (name, "*"))
           let guard = { Pairs = pairs; Position = syntheticPos }
           [ NoteElement
               { NotePosition = Over
                 Target = metadata.InitialStateKey
                 Content = ""
                 Guard = Some guard
                 Position = syntheticPos } ]
   ```
7. Guard notes are placed after participant declarations and before messages (per research R-03).

**Assembling the final Diagram**:
```fsharp
let allElements = participantElements @ guardElements @ messageElements

let diagram =
    { Title = Some options.ResourceName
      AutoNumber = false
      Participants = participants
      Elements = allElements }

Ok diagram
```

**Handle empty states**:
- If `metadata.StateHandlerMap` is empty AND no states found via reflection, return `Error (NoStatesFound options.ResourceName)`
- If only `InitialStateKey` exists (no other states), produce a single-participant diagram

**Files**:
- `src/Frank.Statecharts/Wsd/Generator.fs`

**Edge Cases**:
- Machine with no guards: No `NoteElement` emitted
- Machine with multiple guards: Single `NoteElement` with all guard names as pairs
- Boxed type is not `StateMachine<_,_,_>`: Return `UnrecognizedMachineType` error
- `StateMachineMetadata` with empty `StateHandlerMap` but valid `InitialStateKey`: Emit one participant, no messages

---

### Subtask T011 -- Add `Wsd/Generator.fs` to `.fsproj`

**Purpose**: Register the new file in the F# project's compile items in the correct order.

**Steps**:
1. Edit `src/Frank.Statecharts/Frank.Statecharts.fsproj`
2. Add `<Compile Include="Wsd/Generator.fs" />` AFTER `<Compile Include="Wsd/Serializer.fs" />` and BEFORE `<Compile Include="Types.fs" />`
3. **IMPORTANT**: If WP01 has not yet been merged, you may need to add BOTH `Wsd/Serializer.fs` and `Wsd/Generator.fs`. Check the current state of the `.fsproj` before editing.

**Expected compile order**:
```xml
<Compile Include="Wsd/Types.fs" />
<Compile Include="Wsd/Lexer.fs" />
<Compile Include="Wsd/GuardParser.fs" />
<Compile Include="Wsd/Parser.fs" />
<Compile Include="Wsd/Serializer.fs" />
<Compile Include="Wsd/Generator.fs" />
<Compile Include="Types.fs" />
...
```

**Note**: Generator.fs must come AFTER Serializer.fs because it may reference `Wsd.Serializer` for the top-level API convenience (e.g., a `generateWsd` function that does `generate >> Result.map Serializer.serialize`). Even if it does not reference Serializer directly, the plan specifies this file order.

**Files**:
- `src/Frank.Statecharts/Frank.Statecharts.fsproj`

---

### Subtask T012 -- Create `Wsd/GeneratorTests.fs`

**Purpose**: Comprehensive unit tests for the generator covering happy path, error cases, and edge cases.

**Steps**:
1. Create `test/Frank.Statecharts.Tests/Wsd/GeneratorTests.fs`
2. Module: `module Frank.Statecharts.Tests.Wsd.GeneratorTests`
3. Open `Expecto`, `Frank.Statecharts`, `Frank.Statecharts.Wsd.Types`, `Frank.Statecharts.Wsd.Generator`

**Helper: Constructing test `StateMachineMetadata`**:

Tests need to construct `StateMachineMetadata` instances. This requires creating stub closures for the function fields. Create a helper:

```fsharp
open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// Create a minimal StateMachineMetadata for testing the generator.
let private makeMetadata
    (machine: StateMachine<'S, 'E, 'C>)
    (stateHandlerMap: Map<string, (string * RequestDelegate) list>)
    : StateMachineMetadata =
    let initialKey = machine.Initial.ToString()
    { Machine = box machine
      StateHandlerMap = stateHandlerMap
      ResolveInstanceId = fun _ -> "test"
      TransitionObservers = []
      InitialStateKey = initialKey
      GetCurrentStateKey = fun _ _ _ -> Task.FromResult(initialKey)
      EvaluateGuards = fun _ -> Allowed
      ExecuteTransition = fun _ _ _ -> Task.FromResult(TransitionAttemptResult.NoEvent) }
```

**Test state machine definitions**:

```fsharp
// Turnstile: 3 states
type TurnstileState = Locked | Unlocked | Broken
type TurnstileEvent = Coin | Push | Break

let turnstileMachine : StateMachine<TurnstileState, TurnstileEvent, unit> =
    { Initial = Locked
      InitialContext = ()
      Transition = fun _ _ _ -> TransitionResult.Invalid "test"
      Guards = []
      StateMetadata = Map.empty }

let turnstileHandlerMap =
    Map.ofList [
        "Locked", [ ("GET", RequestDelegate(fun _ -> Task.CompletedTask))
                     ("POST", RequestDelegate(fun _ -> Task.CompletedTask)) ]
        "Unlocked", [ ("GET", RequestDelegate(fun _ -> Task.CompletedTask))
                       ("POST", RequestDelegate(fun _ -> Task.CompletedTask)) ]
        "Broken", [ ("GET", RequestDelegate(fun _ -> Task.CompletedTask)) ]
    ]
```

**Test cases to implement** (minimum):

| Test Name | What It Verifies |
|-----------|-----------------|
| `generate turnstile produces Ok` | `generate { ResourceName = "turnstile" } metadata` returns `Ok diagram` |
| `title is resource name` | `diagram.Title = Some "turnstile"` |
| `initial state is first participant` | `diagram.Participants.[0].Name = "Locked"` |
| `all states present as participants` | `diagram.Participants.Length = 3`, names include Locked, Unlocked, Broken |
| `messages for each handler` | Messages include GET and POST for Locked, GET and POST for Unlocked, GET for Broken |
| `all arrows are solid forward` | All messages have `ArrowStyle = Solid` and `Direction = Forward` |
| `self-messages` | All messages have `Sender = Receiver` (state-capability diagram) |
| `single state no transitions` | Metadata with one state and empty handler list produces 1 participant, 0 messages |
| `machine with guards emits note` | Metadata with guards produces `NoteElement` with `[guard: name=*]` |
| `multiple guards combined` | Two guards produce single note with `[guard: g1=*, g2=*]` |
| `unrecognized machine type` | Metadata with `Machine = box "not a machine"` returns `Error (UnrecognizedMachineType ...)` |
| `empty handler map` | Only initial state as participant, no messages |
| `participants are explicit` | All generated participants have `Explicit = true` |

4. Add `<Compile Include="Wsd/GeneratorTests.fs" />` to test `.fsproj` (after existing Wsd test entries, before `Program.fs`)

**Files**:
- `test/Frank.Statecharts.Tests/Wsd/GeneratorTests.fs` (new, ~200-250 lines)
- `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` (add compile item)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Reflection on `StateMachine<_,_,_>` record fields may break across .NET versions | Use `FSharp.Reflection.FSharpValue.GetRecordFields` which is stable; test with `dotnet build` across TFMs |
| Field indices in `GetRecordFields` depend on declaration order | Verify against `StateMachine` definition in `Types.fs`; add a comment documenting the expected field order |
| Constructing test `StateMachineMetadata` is complex due to closure fields | Use the `makeMetadata` helper that stubs all closures with defaults |
| Guard name extraction from boxed generic list is fragile | Use `System.Collections.IEnumerable` interface for iteration; test with 0, 1, and 2 guards |

## Review Guidance

- Verify `module internal` declaration
- Verify reflection code handles the `StateMachine` record correctly (field indices match `Types.fs` declaration)
- Verify `GeneratorError` cases are distinct and informative
- Verify guard wildcard value is `"*"` per research R-03 decision
- Verify all participants have `Explicit = true` (prevents parser implicit-participant warnings on roundtrip)
- Confirm file order in `.fsproj` is correct
- Verify test helper `makeMetadata` produces valid metadata objects

## Activity Log

- 2026-03-15T23:59:06Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T04:02:51Z – claude-opus – shell_pid=98705 – lane=doing – Assigned agent via workflow command
- 2026-03-16T04:11:37Z – claude-opus – shell_pid=98705 – lane=for_review – Ready for review: Generator.fs implements pure function StateMachineMetadata -> Result<Diagram, GeneratorError>. 18 tests pass. Builds across all TFMs.
