# Tasks: Frank.Datastar Native SSE

**Input**: Design documents from `/specs/014-datastar-native-sse/`
**Prerequisites**: plan.md (required), spec.md (required for user stories), research.md, data-model.md, contracts/

**Tests**: Tests are included for new public APIs (Attributes field, public ServerSentEventGenerator) per constitution requirement "All public APIs MUST have tests." Existing 18 tests validate the replacement implementation.

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

## Path Conventions

- **Library source**: `src/Frank.Datastar/`
- **Tests**: `test/Frank.Datastar.Tests/`
- **Samples**: `sample/Frank.Datastar.Basic/`, `sample/Frank.Datastar.Hox/`, `sample/Frank.Datastar.Oxpecker/`

---

## Phase 1: Setup

**Purpose**: Modify project file to target net10.0 only and remove the StarFederation.Datastar.FSharp dependency

- [X] T001 Update `src/Frank.Datastar/Frank.Datastar.fsproj`: change `<TargetFrameworks>net8.0;net9.0;net10.0</TargetFrameworks>` to `<TargetFramework>net10.0</TargetFramework>`, remove `<PackageReference Include="StarFederation.Datastar.FSharp" ... />`, and add `<Compile Include>` entries for the four new source files in dependency order: `Consts.fs`, `Types.fs`, `ServerSentEvent.fs`, `ServerSentEventGenerator.fs` (all before `Frank.Datastar.fs`)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Implement the internal SSE infrastructure that all user stories depend on — constants, types, low-level write functions, and string utilities

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 [P] Create `src/Frank.Datastar/Consts.fs`: Define `ElementPatchMode` DU (Outer, Inner, Remove, Replace, Prepend, Append, Before, After), `PatchElementNamespace` DU (Html, Svg, MathMl), public constants (`DatastarKey = "datastar"`, `DefaultSseRetryDuration = TimeSpan.FromSeconds(1.0)`, `DefaultElementPatchMode = Outer`, `DefaultPatchElementNamespace = Html`, `DefaultElementsUseViewTransitions = false`, `DefaultPatchSignalsOnlyIfMissing = false`), internal `ScriptDataEffectRemove` literal, and internal `Bytes` module with all pre-allocated byte arrays: event types (`"datastar-patch-elements"B`, `"datastar-patch-signals"B`), data line keys (`"selector"B`, `"mode"B`, `"elements"B`, `"signals"B`, `"useViewTransition"B`, `"namespace"B`, `"onlyIfMissing"B`), boolean values (`"true"B`, `"false"B`), script tags (`"<script>"B`, `"</script>"B`, `"<script data-effect=""el.remove()"">"B`, `"body"B`), ElementPatchMode `toBytes` function, PatchElementNamespace `toBytes` function. Reference: data-model.md Pre-allocated Byte Arrays section and `../datastar-dotnet/src/fsharp/Consts.fs`
- [X] T003 [P] Create `src/Frank.Datastar/Types.fs`: Define all four `[<Struct>]` option record types with exact fields and `static member Defaults` per data-model.md: `PatchElementsOptions` (Selector: string voption, PatchMode: ElementPatchMode, UseViewTransition: bool, Namespace: PatchElementNamespace, EventId: string voption, Retry: TimeSpan), `PatchSignalsOptions` (OnlyIfMissing: bool, EventId: string voption, Retry: TimeSpan), `RemoveElementOptions` (UseViewTransition: bool, EventId: string voption, Retry: TimeSpan), `ExecuteScriptOptions` (AutoRemove: bool, Attributes: string[], EventId: string voption, Retry: TimeSpan). Also define type aliases `Signals = string` and `Selector = string`. Reference: `../datastar-dotnet/src/fsharp/Types.fs`
- [X] T004 Create `src/Frank.Datastar/ServerSentEvent.fs`: Implement internal low-level `IBufferWriter<byte>` write functions as an internal module. Must include: `writeUtf8String` (string to buffer via `Encoding.UTF8.GetBytes` into `GetSpan`), `writeUtf8Literal` (byte[] to buffer via `AsSpan().CopyTo`), `writeUtf8Segment` (StringSegment to buffer), `writeSpace`, `writeNewline`, `sendEventType` (writes `event: <type>\n`), `sendEventId` (writes `id: <id>\n`), `sendRetry` (writes `retry: <ms>\n`), `sendDataBytesLine` (writes `data: <prefix> <bytes>\n`), `sendDataStringLine` (writes `data: <prefix> <string>\n`), `sendDataSegmentLine` (writes `data: <prefix> <segment>\n`), `sendDataStringSeqLine` (writes `data: <prefix> <seq of strings>\n`). All functions must be `inline` and return the writer for chaining. Also include internal `String.splitLinesToSegments` using `StringTokenizer` from `Microsoft.Extensions.Primitives` for zero-allocation newline splitting. Reference: `../datastar-dotnet/src/fsharp/ServerSentEvent.fs` and `../datastar-dotnet/src/fsharp/Utility.fs`

**Checkpoint**: Foundation ready — all internal types, constants, and write primitives available for ServerSentEventGenerator

---

## Phase 3: User Story 1 — Send Datastar SSE Events Without External Dependencies (Priority: P1) MVP

**Goal**: Implement the public `ServerSentEventGenerator` with all ADR-compliant operations (PatchElements, PatchSignals, RemoveElement, ExecuteScript, ReadSignals) and rewire `Frank.Datastar.fs` to use it instead of StarFederation.Datastar.FSharp

**Independent Test**: Send each event type through the implementation and verify SSE output conforms to the Datastar SDK ADR specification

### Implementation for User Story 1

- [X] T005 [US1] Create `src/Frank.Datastar/ServerSentEventGenerator.fs`: Implement `ServerSentEventGenerator` type with static methods per contracts/api-surface.md. Must include: (1) `StartServerEventStreamAsync(httpResponse, ?cancellationToken)` — set `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive` (HTTP/1.1 only), call `httpResponse.StartAsync()` then flush `BodyWriter`. MUST use a lock-protected guard (e.g., `lock` + mutable flag) to ensure headers are set and flushed exactly once per request, even if called multiple times (FR-010/SC-006); (2) `PatchElementsAsync(httpResponse, elements, ?options, ?cancellationToken)` — write `event: datastar-patch-elements`, optional id/retry, conditional data lines for selector/mode/useViewTransition/namespace (only non-defaults), split elements on newlines via `String.splitLinesToSegments` and write each as `data: elements <segment>`, write blank line, flush; (3) `RemoveElementAsync(httpResponse, selector, ?options, ?cancellationToken)` — write `event: datastar-patch-elements` with `data: mode remove` and `data: selector <selector>`, optional useViewTransition; (4) `PatchSignalsAsync(httpResponse, signals, ?options, ?cancellationToken)` — write `event: datastar-patch-signals`, optional onlyIfMissing, split signals on newlines and write each as `data: signals <segment>`; (5) `ExecuteScriptAsync(httpResponse, script, ?options, ?cancellationToken)` — write `event: datastar-patch-elements` with `data: selector body`, `data: mode append`, build `<script>` tag with optional `data-effect="el.remove()"` and verbatim `Attributes`, split script on newlines for body, close with `</script>`; (6) `ReadSignalsAsync(httpRequest, ?cancellationToken)` — GET: read from query param `datastar`, others: read body; (7) `ReadSignalsAsync<'T>(httpRequest, ?jsonSerializerOptions, ?cancellationToken)` — deserialize via `System.Text.Json`, return `ValueSome`/`ValueNone`. All write methods MUST accept and propagate `CancellationToken` (defaulting to `httpResponse.HttpContext.RequestAborted`) and must not throw unhandled exceptions when the token is cancelled mid-write (FR-011). All write methods must use `httpResponse.BodyWriter` as `IBufferWriter<byte>` and flush after each event (FR-013). Reference: `../datastar-dotnet/src/fsharp/ServerSentEventGenerator.fs`, data-model.md SSE Event Format section, and Datastar SDK ADR at `../datastar/sdk/ADR.md`
- [X] T006 [US1] Rewrite `src/Frank.Datastar/Frank.Datastar.fs`: Remove `open StarFederation.Datastar.FSharp`. Update `DatastarExtensions` module to call `ServerSentEventGenerator.StartServerEventStreamAsync(ctx.Response)` (now the local type). Update all `Datastar` module functions to delegate to the local `ServerSentEventGenerator` static methods instead of the external ones. Preserve all function signatures, XML doc comments, and `inline` markers exactly as they are. The only changes should be removing the StarFederation import and updating the internal call targets.

**Checkpoint**: Frank.Datastar builds with zero external dependencies; all SSE event types emit ADR-compliant output

---

## Phase 4: User Story 2 — Preserve Existing Public API (Priority: P1)

**Goal**: Verify that all existing consumers (samples and tests) compile and work correctly without source changes (except the test import fix), and add tests for new public APIs

**Independent Test**: Build all sample projects and run the full test suite against the new implementation

### Implementation for User Story 2

- [X] T007 [US2] Update `test/Frank.Datastar.Tests/DatastarTests.fs`: Remove `open StarFederation.Datastar.FSharp` (line 13). The types `PatchElementsOptions`, `ElementPatchMode`, `PatchSignalsOptions`, `RemoveElementOptions`, `ExecuteScriptOptions` are now available via the existing `open Frank.Datastar` import. No other changes should be needed — verify all 18 existing tests compile.
- [X] T008 [US2] Build all sample projects without changes: Run `dotnet build` on `sample/Frank.Datastar.Basic/Frank.Datastar.Basic.fsproj`, `sample/Frank.Datastar.Hox/Frank.Datastar.Hox.fsproj`, and `sample/Frank.Datastar.Oxpecker/Frank.Datastar.Oxpecker.fsproj`. All must compile successfully with zero source changes. If any fail, investigate — the samples do NOT import StarFederation.Datastar.FSharp directly, so no changes should be needed.
- [X] T009 [US2] Run the full test suite: Execute `dotnet test test/Frank.Datastar.Tests/Frank.Datastar.Tests.fsproj`. All 18 existing tests must pass. Failures indicate either (a) SSE output format differences from the prior implementation, (b) type signature mismatches in the new option types, or (c) ReadSignals behavior differences. Fix any failures by aligning the new implementation with the expected output format.
- [X] T010 [US2] Add test for `ExecuteScriptOptions.Attributes` in `test/Frank.Datastar.Tests/DatastarTests.fs`: Add a test that calls `Datastar.executeScriptWithOptions` with `{ ExecuteScriptOptions.Defaults with Attributes = [| "type=\"module\"" |] }` and verifies the response body contains `<script type="module"` (attribute written verbatim). Follows the existing WithOptions test pattern (e.g., T483 executeScriptWithOptions).
- [X] T011 [US2] Add test for public `ServerSentEventGenerator` API in `test/Frank.Datastar.Tests/DatastarTests.fs`: Add a test that calls `ServerSentEventGenerator.PatchElementsAsync(ctx.Response, html)` directly (not via the `Datastar` module) and verifies well-formed SSE output containing `event: datastar-patch-elements` and the expected HTML content. Confirms FR-014 public accessibility. Uses the existing `createMockContext()` test infrastructure.

**Checkpoint**: All existing consumers verified — zero-change upgrade path confirmed for samples; test import is the only required change; new public APIs have test coverage

---

## Phase 5: User Story 3 — Efficient Resource Usage (Priority: P2)

**Goal**: Verify that the implementation uses zero-copy techniques and matches or exceeds the allocation profile of StarFederation.Datastar.FSharp

**Independent Test**: Code review of hot paths confirms pre-allocated byte arrays, inline functions, IBufferWriter pattern, and StringTokenizer usage; allocation profiling produces measurable evidence

### Implementation for User Story 3

- [X] T012 [P] [US3] Audit `src/Frank.Datastar/ServerSentEvent.fs` for allocation-free write paths: Verify all write functions use `writer.GetSpan()` + `writer.Advance()` pattern (not `writer.Write(new byte[])` or similar allocating patterns). Verify `String.splitLinesToSegments` uses `StringTokenizer` (not `String.Split` which allocates arrays). Verify all SSE prefix writes use pre-allocated byte literals from `Consts.Bytes` module. Fix any allocating patterns found.
- [X] T013 [P] [US3] Audit `src/Frank.Datastar/ServerSentEventGenerator.fs` for allocation-free event composition: Verify PatchElements/PatchSignals iterate `splitLinesToSegments` results without collecting to list/array. Verify ExecuteScript builds the `<script>` tag using byte literals and `sendDataStringSeqLine`/`sendDataBytesLine` (not string concatenation). Verify all functions marked `inline` where appropriate. Fix any allocating patterns found.
- [X] T014 [US3] Run allocation profiling for SC-005 evidence: Create a simple console benchmark (in the scratchpad directory, not committed) that sends 10,000 PatchElements events to a mock `IBufferWriter<byte>` and measures allocations using `GC.GetAllocatedBytesForCurrentThread()` before/after. Compare against the same workload using StarFederation.Datastar.FSharp (reference the existing project at `../datastar-dotnet`). Document results in a comment on the PR. The new implementation must allocate no more than the baseline.

**Checkpoint**: Performance audit complete — all hot paths confirmed allocation-free; allocation profiling evidence produced

---

## Phase 6: User Story 4 — .NET 10 Only Target (Priority: P2)

**Goal**: Confirm the project targets net10.0 only and the dependency graph is clean

**Independent Test**: Inspect the project file and verify the built package has no StarFederation.Datastar.FSharp dependency

### Implementation for User Story 4

- [X] T015 [US4] Verify `src/Frank.Datastar/Frank.Datastar.fsproj` targets `<TargetFramework>net10.0</TargetFramework>` (singular, not plural), has no `PackageReference` to `StarFederation.Datastar.FSharp`, and retains only the `ProjectReference` to Frank core and the `PackageReference` to `Microsoft.SourceLink.GitHub`. Run `dotnet build src/Frank.Datastar/Frank.Datastar.fsproj` to confirm clean build.

**Checkpoint**: Framework target and dependency graph verified

---

## Phase 7: Polish & Cross-Cutting Concerns

**Purpose**: Version bump, documentation, and final validation

- [X] T016 Update version in `src/Directory.Build.props`: Change `<VersionPrefix>` from `7.0.0` to `8.0.0` to reflect the breaking changes (net10.0 only, Attributes type change)
- [X] T017 Verify ADR compliance end-to-end: Manually compare SSE output for each event type (PatchElements minimal, PatchElements full options, PatchSignals, RemoveElement, ExecuteScript minimal, ExecuteScript with attributes) against the examples in `../datastar/sdk/ADR.md`. Ensure byte-for-byte format match including field ordering, conditional omission of defaults, and line termination.
- [X] T018 Run full solution build and test: Execute `dotnet build Frank.sln` (Frank core) and `dotnet build src/Frank.Datastar/Frank.Datastar.fsproj` and `dotnet test test/Frank.Datastar.Tests/Frank.Datastar.Tests.fsproj` and build all three sample projects. Everything must pass.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (project file must have new Compile entries)
- **User Story 1 (Phase 3)**: Depends on Phase 2 (needs Consts, Types, ServerSentEvent)
- **User Story 2 (Phase 4)**: Depends on Phase 3 (needs working ServerSentEventGenerator)
- **User Story 3 (Phase 5)**: Depends on Phase 3 (needs implementation to audit)
- **User Story 4 (Phase 6)**: Depends on Phase 1 (project file changes)
- **Polish (Phase 7)**: Depends on all prior phases

### User Story Dependencies

- **US1 (P1)**: Depends on Foundational — core implementation
- **US2 (P1)**: Depends on US1 — cannot verify compatibility until implementation exists
- **US3 (P2)**: Depends on US1 — cannot audit until implementation exists; can run in parallel with US2
- **US4 (P2)**: Independent of other stories — can run in parallel with US1 after Setup

### Within Each User Story

- ServerSentEventGenerator (T005) before Frank.Datastar.fs rewire (T006)
- Test import fix (T007) before sample builds (T008) before test run (T009) before new tests (T010, T011)
- Audit tasks (T012, T013) can run in parallel; allocation profiling (T014) follows audits
- T010 and T011 can run in parallel (adding tests to same file but independent test cases)

### Parallel Opportunities

- T002 and T003 can run in parallel (different files, no dependencies)
- T010 and T011 can run in parallel (independent test additions)
- T012 and T013 can run in parallel (auditing different files)
- US3 and US2 can run in parallel after US1 completes
- US4 verification (T015) can run any time after T001

---

## Parallel Example: Phase 2 (Foundational)

```text
# These can run in parallel (different files):
T002: Create Consts.fs (enums, constants, byte arrays)
T003: Create Types.fs (option struct records, type aliases)

# This must follow T002 and T003 (depends on both):
T004: Create ServerSentEvent.fs (write functions use Consts, Types)
```

## Parallel Example: After User Story 1 Completes

```text
# These can run in parallel (independent validation):
US2: T007 → T008 → T009 → T010 + T011 (compatibility + new tests)
US3: T012 + T013 in parallel → T014 (performance audit + profiling)
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002–T004)
3. Complete Phase 3: User Story 1 (T005–T006)
4. **STOP and VALIDATE**: Build the project, manually test SSE output format
5. This delivers a working Frank.Datastar with no external dependencies

### Incremental Delivery

1. Setup + Foundational → Internal infrastructure ready
2. Add US1 (T005–T006) → Core SSE implementation working (MVP!)
3. Add US2 (T007–T011) → Backward compatibility verified + new API tests
4. Add US3 (T012–T014) → Performance validated with profiling evidence
5. Add US4 (T015) → Framework target confirmed
6. Polish (T016–T018) → Version bump, full validation

---

## Notes

- [P] tasks = different files, no dependencies
- [Story] label maps task to specific user story for traceability
- US1 and US2 are both P1 but US2 depends on US1 (can't verify compatibility without implementation)
- US3 and US4 are both P2 and largely independent of each other
- The existing 18 tests in DatastarTests.fs serve as the primary regression gate (SC-001)
- T010 and T011 add tests for new public APIs per constitution requirement "All public APIs MUST have tests"
- T014 produces allocation profiling evidence per constitution requirement "Performance-sensitive changes MUST include benchmark results"
- Sample project builds serve as the secondary compatibility gate (SC-002)
