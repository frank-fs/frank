# Tasks: Frank.Datastar Streaming HTML Generation

**Input**: Design documents from `/specs/015-datastar-streaming-html/`
**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/api-surface.md

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (e.g., US1, US2, US3)
- Include exact file paths in descriptions

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Project structure changes to support the new SseDataLineWriter file

- [X] T001 Add `SseDataLineWriter.fs` compile entry to `src/Frank.Datastar/Frank.Datastar.fsproj` between `ServerSentEvent.fs` and `ServerSentEventGenerator.fs` per plan D-001. The ItemGroup should read: `Consts.fs`, `Types.fs`, `ServerSentEvent.fs`, `SseDataLineWriter.fs`, `ServerSentEventGenerator.fs`, `Frank.Datastar.fs`.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Core internal type that ALL stream-based overloads depend on

**CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 Widen visibility of required write helpers in `src/Frank.Datastar/ServerSentEvent.fs` from `private` to `internal`: `writeUtf8Literal`, `writeSpace`, `writeNewline`. These are needed by `SseDataLineWriter` in a separate file. Also widen the `Bytes` module's `dataPrefix` constant (the `"data: "B` byte literal) to internal visibility. Keep all other helpers (e.g., `writeUtf8String`, `writeUtf8Segment`, `sendEventType`, `sendDataStringLine`, `sendDataSegmentLine`) as private — they are not needed outside ServerSentEvent.fs.

- [X] T003 Create `src/Frank.Datastar/SseDataLineWriter.fs` — an internal `TextWriter` subclass that bridges caller text output to SSE-formatted `data:` lines on `IBufferWriter<byte>`. Implementation per data-model.md and research R-001/R-002/R-003:
  - **Constructor**: `(bufferWriter: IBufferWriter<byte>, dataLineType: byte[], cancellationToken: CancellationToken)`. Rent initial `char[256]` from `ArrayPool<char>.Shared`. Set `position = 0`.
  - **Encoding property**: Return `System.Text.Encoding.UTF8`.
  - **Write(char)**: Buffer char. If char is `\n`, call `emitLine()`. If char is `\r`, skip it (will be followed by `\n` or treated as line boundary). If buffer full, call `ensureCapacity()`.
  - **Write(string)**: Scan string for `\n` characters. For each segment between newlines, buffer chars and call `emitLine()` at boundaries. Handle `\r\n` and `\r` as line boundaries per validation rules.
  - **Flush()**: If `position > 0`, call `emitLine()` to emit remaining buffered content as a final data line.
  - **Dispose(bool)**: Call `Flush()`. Return `charBuffer` to `ArrayPool<char>.Shared.Return()`.
  - **Private emitLine()**: If `position = 0`, skip (empty line filtering per data-model.md). Otherwise: check `cancellationToken.ThrowIfCancellationRequested()`. Then write to `bufferWriter` using `ServerSentEvent.writeUtf8Literal` for the `dataPrefix` (`"data: "B`), `dataLineType`, and `ServerSentEvent.writeSpace`. Encode `charBuffer[0..position-1]` to UTF-8 directly into `bufferWriter.GetSpan()` using `Encoding.UTF8.GetBytes(ReadOnlySpan<char>, Span<byte>)`. Call `ServerSentEvent.writeNewline`. Reset `position = 0`.
  - **Private ensureCapacity()**: Rent larger buffer from `ArrayPool<char>.Shared` (double size), copy existing content, return old buffer.

**Checkpoint**: Foundation ready — SseDataLineWriter compiles and is available for ServerSentEventGenerator overloads

---

## Phase 3: User Story 1 — Stream HTML Directly to SSE Response (Priority: P1) MVP

**Goal**: Add stream-based overloads for all SSE operations that accept `TextWriter -> Task` writer callbacks, enabling direct-to-buffer HTML generation without string materialization.

**Independent Test**: Send HTML via stream-based API, verify SSE output is byte-for-byte identical to string-based equivalent. Verify multi-line content splits correctly into SSE `data:` lines.

### Implementation for User Story 1

- [X] T004 [US1] Add stream-based `PatchElementsAsync` overloads (3 methods) to `src/Frank.Datastar/ServerSentEventGenerator.fs`. Follow the existing 3-level overload pattern per contracts/api-surface.md. The full-signature method should: (1) call `startServerEventStream`, (2) get `httpResponse.BodyWriter`, (3) write event type `Bytes.EventTypePatchElements`, (4) write optional fields from `PatchElementsOptions` (same as string-based), (5) create `SseDataLineWriter(writer, Bytes.DatalineElements, cancellationToken)` using `use`, (6) call `do! writer(sseWriter)`, (7) call `sseWriter.Flush()`, (8) write blank line terminator, (9) flush `BodyWriter`. Add shorthand overloads: one using `httpResponse.HttpContext.RequestAborted` as cancellation token, one using `PatchElementsOptions.Defaults`.

- [X] T005 [US1] Add stream-based `RemoveElementAsync` overloads (3 methods) to `src/Frank.Datastar/ServerSentEventGenerator.fs`. Per plan D-004, the writer callback provides the selector value. The full-signature method should: (1) call `startServerEventStream`, (2) write event type `Bytes.EventTypePatchElements`, (3) write optional fields from `RemoveElementOptions`, (4) create `SseDataLineWriter(writer, Bytes.DatalineSelector, cancellationToken)`, (5) call `do! writer(sseWriter)`, (6) flush, (7) write `data: mode remove`, (8) write blank line terminator, (9) flush `BodyWriter`. Add shorthand overloads.

- [X] T006 [US1] Add stream-based `PatchSignalsAsync` overloads (3 methods) to `src/Frank.Datastar/ServerSentEventGenerator.fs`. The full-signature method should: (1) call `startServerEventStream`, (2) write event type `Bytes.EventTypePatchSignals`, (3) write optional fields from `PatchSignalsOptions`, (4) create `SseDataLineWriter(writer, Bytes.DatalineSignals, cancellationToken)`, (5) call `do! writer(sseWriter)`, (6) flush, (7) write blank line terminator, (8) flush `BodyWriter`. Add shorthand overloads.

- [X] T007 [US1] Add stream-based `ExecuteScriptAsync` overloads (3 methods) to `src/Frank.Datastar/ServerSentEventGenerator.fs`. Per plan D-003, the method emits `<script>` open/close tags; the writer callback writes only the script body. The full-signature method should: (1) call `startServerEventStream`, (2) write event type `Bytes.EventTypePatchElements`, (3) write optional fields, (4) write `data: selector body` and `data: mode append`, (5) write `data: elements <script ...>` open tag (with auto-remove and attributes from options), (6) create `SseDataLineWriter(writer, Bytes.DatalineElements, cancellationToken)`, (7) call `do! writer(sseWriter)`, (8) flush, (9) write `data: elements </script>`, (10) write blank line terminator, (11) flush `BodyWriter`. Add shorthand overloads.

- [X] T008 [US1] Add 8 stream-based `inline` module functions to `src/Frank.Datastar/Frank.Datastar.fs` in the `Datastar` module, following the existing curried pattern (`content -> ctx -> Task`). Functions per contracts/api-surface.md: `streamPatchElements`, `streamPatchElementsWithOptions`, `streamRemoveElement`, `streamRemoveElementWithOptions`, `streamPatchSignals`, `streamPatchSignalsWithOptions`, `streamExecuteScript`, `streamExecuteScriptWithOptions`. Each delegates to the corresponding `ServerSentEventGenerator` static method. Add `open System.IO` if not already present.

- [X] T009 [US1] Add test in `test/Frank.Datastar.Tests/DatastarTests.fs`: byte-for-byte equivalence (SC-004). Create a test that sends identical multi-line HTML (`"<div id=\"test\">\n  <p>Hello</p>\n</div>"`) via both `Datastar.patchElements html ctx` and `Datastar.streamPatchElements (fun tw -> tw.Write(html); Task.CompletedTask) ctx`, then asserts both response bodies are identical byte-for-byte. Use the existing `createMockContext`/`getResponseBody` test helpers.

- [X] T010 [US1] Add test in `test/Frank.Datastar.Tests/DatastarTests.fs`: multi-line stream splitting (FR-003). Create a test that streams multi-line HTML via `streamPatchElements` and verifies each line becomes a separate `data: elements <line>` entry in the SSE output. Also test with `\r\n` line endings.

- [X] T011 [US1] Add tests in `test/Frank.Datastar.Tests/DatastarTests.fs` for stream-based `patchSignals`, `removeElement`, and `executeScript`. For `streamPatchSignals`: verify JSON content appears as `data: signals <json>`. For `streamRemoveElement`: verify selector appears as `data: selector <value>` with `data: mode remove`. For `streamExecuteScript`: verify script body is wrapped in `<script>` tags in `data: elements` lines.

- [X] T012 [US1] Add test in `test/Frank.Datastar.Tests/DatastarTests.fs`: exception propagation (FR-005). Create a test where the writer callback throws an exception, verify the exception propagates to the caller without being swallowed. Use `Expect.throws` or `Expect.throwsT<exn>`.

- [X] T013 [US1] Build and run all tests to verify US1 implementation. Run `dotnet build src/Frank.Datastar/` to confirm compilation, then run `dotnet test test/Frank.Datastar.Tests/` to verify all tests pass (both new streaming tests and all existing tests).

**Checkpoint**: Stream-based API is fully functional. All 4 operations have stream overloads. Tests verify byte-for-byte equivalence, multi-line splitting, and error handling.

---

## Phase 4: User Story 3 — Backward Compatibility (Priority: P1)

**Goal**: Verify the string-based API remains unchanged and all existing consumers work without modification.

**Independent Test**: Run existing test suite and build all sample projects with zero code changes.

- [X] T014 [US3] Verify all existing tests in `test/Frank.Datastar.Tests/DatastarTests.fs` pass without any modifications. The new stream-based overloads MUST NOT break any existing string-based test. Run `dotnet test test/Frank.Datastar.Tests/` and confirm zero failures in pre-existing tests (separate from new streaming tests added in Phase 3).

- [X] T015 [P] [US3] Build all three sample projects to verify they compile without changes: `dotnet build sample/Frank.Datastar.Basic/`, `dotnet build sample/Frank.Datastar.Hox/`, `dotnet build sample/Frank.Datastar.Oxpecker/`. All three MUST succeed with zero code changes.

**Checkpoint**: Backward compatibility confirmed. Existing string-based API is unchanged.

---

## Phase 5: User Story 2 — View Engine Integration (Priority: P2)

**Goal**: Verify that string-based and stream-based APIs coexist without interference, and that view engines without streaming support continue to work unchanged.

**Independent Test**: Verify sample projects work, and test mixing both API styles in the same handler.

- [X] T016 [US2] Add test in `test/Frank.Datastar.Tests/DatastarTests.fs`: mixed string-based and stream-based calls in the same SSE handler. Create a test that calls `Datastar.patchElements` followed by `Datastar.streamPatchElements` in the same handler, verify both events appear correctly in the response body with proper SSE framing (each event terminated by blank line).

- [X] T017 [US2] Add test in `test/Frank.Datastar.Tests/DatastarTests.fs`: stream-based WithOptions variants produce correct output. Test `streamPatchElementsWithOptions` with `{ PatchElementsOptions.Defaults with PatchMode = Inner }`, verify output contains `data: mode inner`. Test `streamExecuteScriptWithOptions` with custom `Attributes`, verify attributes appear in the `<script>` tag.

**Checkpoint**: Both APIs coexist. View engines without streaming continue to use string-based API unchanged.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Version bump, final build verification

- [X] T018 Bump `VersionPrefix` from `8.0.0` to `8.1.0` in `src/Frank.Datastar/Frank.Datastar.fsproj` per plan D-005 (minor, backward-compatible addition)

- [X] T019 Build full solution and run all tests as final verification: `dotnet build Frank.sln && dotnet test test/Frank.Datastar.Tests/`

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: No dependencies — can start immediately
- **Foundational (Phase 2)**: Depends on Phase 1 (T001 must complete before T003)
- **US1 (Phase 3)**: Depends on Phase 2 (T003 must complete before T004-T008)
- **US3 (Phase 4)**: Depends on Phase 3 (T013 must complete before T014)
- **US2 (Phase 5)**: Depends on Phase 3 (T008 must complete before T016)
- **Polish (Phase 6)**: Depends on all previous phases

### User Story Dependencies

- **US1 (P1)**: Depends on Foundational phase — no dependencies on other stories
- **US3 (P1)**: Depends on US1 being implemented (verification of backward compat)
- **US2 (P2)**: Depends on US1 being implemented (verification of coexistence)

### Within Each Phase

- T004 → T005 → T006 → T007: Sequential (same file: ServerSentEventGenerator.fs)
- T009 → T010 → T011 → T012: Sequential (same file: DatastarTests.fs)
- T014, T015: Can run in parallel (different verification targets)
- T016, T017: Sequential (same file: DatastarTests.fs)

### Parallel Opportunities

- T014 and T015 can run in parallel (test runner vs build)
- All sample builds in T015 can run in parallel (3 independent projects)

---

## Parallel Example: User Story 3

```
# These can run in parallel:
Task T014: "Run existing test suite"
Task T015: "Build all sample projects"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Complete Phase 1: Setup (T001)
2. Complete Phase 2: Foundational (T002, T003)
3. Complete Phase 3: User Story 1 (T004-T013)
4. **STOP and VALIDATE**: Stream-based API works, byte-for-byte equivalence confirmed
5. This delivers the core value proposition (allocation reduction)

### Incremental Delivery

1. Setup + Foundational → SseDataLineWriter ready
2. US1 → Stream-based overloads functional → **MVP delivers allocation reduction**
3. US3 → Backward compatibility verified → Safe to release
4. US2 → View engine coexistence verified → Feature complete
5. Polish → Version bump, final build → Release 8.1.0

---

## Notes

- Total tasks: 19 (T001-T019)
- US1 tasks: 10 (T004-T013) — core implementation + tests
- US3 tasks: 2 (T014-T015) — backward compatibility verification
- US2 tasks: 2 (T016-T017) — coexistence verification
- Foundational tasks: 2 (T002-T003) — SseDataLineWriter
- Setup tasks: 1 (T001) — .fsproj
- Polish tasks: 2 (T018-T019) — version bump + final build
- New source files: 1 (SseDataLineWriter.fs)
- Modified source files: 3 (ServerSentEvent.fs, ServerSentEventGenerator.fs, Frank.Datastar.fs)
- Modified project file: 1 (Frank.Datastar.fsproj)
- Modified test file: 1 (DatastarTests.fs)
- All tasks target single existing project (Frank.Datastar) — no new projects created
