---
work_package_id: "WP02"
title: "Migrate JsonParser to Shared AST"
lane: "done"
dependencies: ["WP01"]
requirement_refs:
  - "FR-001"
  - "FR-002"
  - "FR-003"
  - "FR-004"
  - "FR-005"
  - "FR-006"
  - "FR-008"
  - "FR-009"
  - "FR-010"
  - "FR-011"
subtasks:
  - "T004"
  - "T005"
  - "T006"
  - "T007"
  - "T008"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "claude-opus"
history:
  - timestamp: "2026-03-16T19:13:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- Migrate JsonParser to Shared AST

## Important: Review Feedback Status

- **Has review feedback?**: Check the `review_status` field above.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Objectives & Success Criteria

- `parseAlpsJson` returns `ParseResult` (with `Document: StatechartDocument`, `Errors: ParseFailure list`, `Warnings: ParseWarning list`) instead of `Result<AlpsDocument, AlpsParseError list>`.
- The parser identifies states using the same 3-part heuristic currently in `Mapper.fs`.
- Transitions are extracted with correct source, target, event, guard, parameters, and ALPS-specific annotations.
- ALPS-specific metadata (documentation, links, extensions, data descriptors, version) is stored in `AlpsMeta` annotations on the correct AST nodes.
- Both golden files (tic-tac-toe and onboarding) parse correctly to `StatechartDocument` with the expected states and transitions.
- `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds.

## Context & Constraints

- **Spec**: `kitty-specs/023-alps-shared-ast-migration/spec.md` (FR-001 through FR-011, User Story 1 + 2)
- **Plan**: `kitty-specs/023-alps-shared-ast-migration/plan.md` (Parser Design: Heuristic Absorption)
- **Data Model**: `kitty-specs/023-alps-shared-ast-migration/data-model.md` (annotation ordering rules, state classification heuristic)
- **Research**: `kitty-specs/023-alps-shared-ast-migration/research.md` (Section 1: Mapper heuristics, Section 2: AlpsMeta extensions)
- **Reference implementation**: `src/Frank.Statecharts/Alps/Mapper.fs` contains the heuristics to absorb.
- **WSD migration pattern**: `src/Frank.Statecharts/Wsd/Parser.fs` shows how a format parser produces `ParseResult` directly.
- **Key constraint**: The parser must still `open Frank.Statecharts.Alps.Types` temporarily (Mapper.fs is not deleted until WP04). But JsonParser.fs should stop referencing `AlpsDocument` and `AlpsParseError` -- use `ParseResult` and `ParseFailure` instead.

## Implementation Command

```bash
spec-kitty implement WP02 --base WP01
```

## Subtasks & Detailed Guidance

### Subtask T004 -- Define private intermediate parser types

- **Purpose**: Keep the JSON-to-record parsing pass clean by using private intermediate types (same shape as current `Descriptor` but not exposed). This preserves the existing JSON parsing logic while making the module self-contained.

- **File**: `src/Frank.Statecharts/Alps/JsonParser.fs`

- **Steps**:
  1. Add private type definitions at the top of the module (after the `open` statements):
     ```fsharp
     /// Private intermediate type for JSON parsing pass.
     type private ParsedDescriptor =
         { Id: string option
           Type: string option   // raw string, not DU
           Href: string option
           ReturnType: string option
           DocFormat: string option
           DocValue: string option
           Children: ParsedDescriptor list
           Extensions: ParsedExtension list
           Links: ParsedLink list }

     and private ParsedExtension =
         { Id: string
           Href: string option
           Value: string option }

     and private ParsedLink =
         { Rel: string
           Href: string }
     ```
  2. Note: `Type` is `string option` (raw JSON string) rather than a DU, because classification happens in Pass 2.
  3. Note: Documentation is stored as `DocFormat` and `DocValue` directly on the descriptor (rather than a nested record), keeping the intermediate type flat.

- **Notes**: These types mirror the current `Alps.Types.Descriptor`/`AlpsExtension`/`AlpsLink` shapes but are private and scoped to the parser module only.

### Subtask T005 -- Implement state classification heuristics

- **Purpose**: Absorb the state identification logic from `Mapper.fs` into the parser, so the parser can classify descriptors into states vs. data descriptors during the second pass.

- **File**: `src/Frank.Statecharts/Alps/JsonParser.fs`

- **Steps**:
  1. Port `isTransitionType` (Mapper.fs line 11-16): classify a raw type string as transition or not.
     ```fsharp
     let private isTransitionTypeStr (typeStr: string option) =
         match typeStr with
         | Some "safe" | Some "unsafe" | Some "idempotent" -> true
         | _ -> false
     ```
  2. Port `collectRtTargets` (Mapper.fs line 59-72): recursively walk all descriptors to find all `rt` target IDs (strip `#` prefix).
     ```fsharp
     let private collectRtTargets (descriptors: ParsedDescriptor list) : Set<string> =
         let rec collect (acc: Set<string>) (descs: ParsedDescriptor list) =
             descs |> List.fold (fun s d ->
                 let s' =
                     match d.ReturnType with
                     | Some rt ->
                         let target = if rt.StartsWith("#") then rt.Substring(1) else rt
                         Set.add target s
                     | None -> s
                 collect s' d.Children) acc
         collect Set.empty descriptors
     ```
  3. Port `isStateDescriptor` (Mapper.fs line 76-84): a top-level semantic descriptor is a state if:
     - It has transition-type children, OR
     - Its `id` is in the set of `rt` targets, OR
     - It has href-only children (children with `Href.IsSome && Id.IsNone`)
     ```fsharp
     let private isStateDescriptor (rtTargets: Set<string>) (d: ParsedDescriptor) =
         (d.Type.IsNone || d.Type = Some "semantic")
         && d.Id.IsSome
         && (d.Children |> List.exists (fun c -> isTransitionTypeStr c.Type)
             || Set.contains d.Id.Value rtTargets
             || d.Children |> List.exists (fun c -> c.Href.IsSome && c.Id.IsNone))
     ```
  4. Port `buildDescriptorIndex` (Mapper.fs line 91-104): build a map from descriptor `id` to `ParsedDescriptor` for href resolution.
     ```fsharp
     let private buildDescriptorIndex (descriptors: ParsedDescriptor list) : Map<string, ParsedDescriptor> =
         let rec collectAll acc descs =
             descs |> List.fold (fun m d ->
                 let m' = match d.Id with Some id -> Map.add id d m | None -> m
                 collectAll m' d.Children) acc
         collectAll Map.empty descriptors
     ```

- **Notes**: Use raw `string option` for type comparison instead of `DescriptorType` DU (since we use intermediate types now).

### Subtask T006 -- Implement transition extraction

- **Purpose**: Extract `TransitionEdge` records from state descriptor children, resolving href references and extracting guards and parameters.

- **File**: `src/Frank.Statecharts/Alps/JsonParser.fs`

- **Steps**:
  1. Port `resolveRt` (Mapper.fs line 34-35):
     ```fsharp
     let private resolveRt (rt: string option) : string option =
         rt |> Option.map (fun r -> if r.StartsWith("#") then r.Substring(1) else r)
     ```
  2. Port `extractGuard` (Mapper.fs line 38-41):
     ```fsharp
     let private extractGuard (exts: ParsedExtension list) : string option =
         exts |> List.tryFind (fun e -> e.Id = "guard") |> Option.bind (fun e -> e.Value)
     ```
  3. Port `extractParameters` (Mapper.fs line 45-52): extract parameter IDs from href-only children of transition descriptors:
     ```fsharp
     let private extractParameters (children: ParsedDescriptor list) : string list =
         children |> List.choose (fun d ->
             match d.Href, d.Id with
             | Some href, None -> Some(if href.StartsWith("#") then href.Substring(1) else href)
             | _ -> None)
     ```
  4. Port `toTransitionKind`: map raw type string to `AlpsTransitionKind`:
     ```fsharp
     let private toTransitionKind (typeStr: string option) : AlpsTransitionKind =
         match typeStr with
         | Some "safe" -> AlpsTransitionKind.Safe
         | Some "idempotent" -> AlpsTransitionKind.Idempotent
         | _ -> AlpsTransitionKind.Unsafe  // default
     ```
  5. Implement `resolveDescriptor` (Mapper.fs line 116-123): resolve an href-only descriptor to the actual descriptor in the index.
  6. Implement `extractTransitions`: for each state descriptor's children, resolve href refs, check if transition type, build `TransitionEdge` with:
     - `Source` = parent state id
     - `Target` = resolved `rt` value (stripped of `#`)
     - `Event` = child descriptor `id`
     - `Guard` = from `extractGuard`
     - `Parameters` = from `extractParameters`
     - `Annotations` = `[AlpsAnnotation(AlpsTransitionType(kind))]` plus `AlpsAnnotation(AlpsDescriptorHref href)` if original child was href-only

- **Notes**: Follow the same logic as `Mapper.extractTransitions` (lines 127-154) but operating on `ParsedDescriptor` instead of `Descriptor`.

### Subtask T007 -- Implement annotation extraction

- **Purpose**: Extract ALPS-specific metadata from the parsed JSON and store it as `AlpsMeta` annotations on the correct AST nodes.

- **File**: `src/Frank.Statecharts/Alps/JsonParser.fs`

- **Steps**:
  1. **Document-level annotations** (placed on `StatechartDocument.Annotations`):
     - `AlpsVersion(version)` from `alps.version` -- always first
     - `AlpsDocumentation(format, value)` from `alps.doc` -- if present
     - `AlpsLink(rel, href)` from `alps.link[]` -- in document order
     - `AlpsExtension(id, href, value)` from `alps.ext[]` -- in document order
     - `AlpsDataDescriptor(id, doc)` for each top-level semantic descriptor that is NOT a state -- in document order

  2. **State-level annotations** (placed on `StateNode.Annotations`):
     - `AlpsDocumentation(format, value)` from state descriptor's `doc` -- if present
     - `AlpsExtension(id, href, value)` from state descriptor's `ext[]` -- in document order (excluding guards which go on transitions)
     - `AlpsLink(rel, href)` from state descriptor's `link[]` -- if present

  3. **Transition-level annotations** (placed on `TransitionEdge.Annotations`):
     - `AlpsTransitionType(kind)` -- always first
     - `AlpsDescriptorHref(href)` -- if the original child was an href-only reference
     - `AlpsDocumentation(format, value)` from transition descriptor's `doc` -- if present
     - `AlpsExtension(id, href, value)` from transition descriptor's `ext[]` -- in document order (excluding guards which are in `TransitionEdge.Guard`)

  4. **Annotation ordering** must be deterministic (per D-005 and data-model.md). Use the order specified above exactly.

- **Notes**:
  - Guards are NOT stored as `AlpsExtension` annotations -- they are extracted into `TransitionEdge.Guard`. Only non-guard extensions go into annotations.
  - The `AlpsDataDescriptor` annotation carries `doc: (string option * string) option` where the inner tuple is `(format, value)`. If the data descriptor has no documentation, use `None`.

### Subtask T008 -- Change `parseAlpsJson` return type to `ParseResult`

- **Purpose**: Wire up the two-pass architecture into the public `parseAlpsJson` function.

- **File**: `src/Frank.Statecharts/Alps/JsonParser.fs`

- **Steps**:
  1. Change the function signature:
     ```fsharp
     let parseAlpsJson (json: string) : ParseResult =
     ```
  2. Add `open Frank.Statecharts.Ast` at the top of the module. Keep `open Frank.Statecharts.Alps.Types` temporarily (it will be removed in WP04 when Types.fs is deleted).
  3. **Pass 1**: Parse JSON to intermediate `ParsedDescriptor` records (same as current `parseDescriptor` logic but producing `ParsedDescriptor` instead of `Descriptor`).
     - Update `parseDescriptor` to produce `ParsedDescriptor`.
     - Update `parseExtension` to produce `ParsedExtension`.
     - Update `parseLink` to produce `ParsedLink`.
     - Parse version, documentation, links, extensions at the root `alps` level into local variables.
  4. **Pass 2**: Convert intermediate descriptors to `StatechartDocument`:
     - Call `collectRtTargets` to get rt target set.
     - Call `buildDescriptorIndex` for href resolution.
     - Classify top-level descriptors using `isStateDescriptor`.
     - For each state descriptor: create `StateNode` + extract `TransitionEdge` list.
     - For each non-state semantic descriptor: create `AlpsDataDescriptor` annotation.
     - Build document-level annotations list.
     - Assemble `StatechartDocument`.
  5. **Error handling**:
     - `JsonException` catch: return `ParseResult` with a `ParseFailure` in `Errors` and an empty `StatechartDocument` as `Document`.
     - Missing `alps` root: same approach -- `ParseFailure` error.
     - Non-object root: same approach.
     ```fsharp
     let emptyDoc = { Title = None; InitialStateId = None; Elements = []; DataEntries = []; Annotations = [] }
     // Error case:
     { Document = emptyDoc
       Errors = [{ Position = None; Description = "..."; Expected = "..."; Found = "..."; CorrectiveExample = "..." }]
       Warnings = [] }
     ```
  6. **Success case**: return `ParseResult` with the constructed `StatechartDocument`, empty `Errors`, empty `Warnings`.

- **StateNode construction** (from a state descriptor):
  ```fsharp
  { Identifier = d.Id |> Option.defaultValue ""
    Label = match d.DocValue with Some v -> Some v | None -> None
    Kind = StateKind.Regular
    Children = []
    Activities = None
    Position = None
    Annotations = stateAnnotations }  // AlpsDocumentation, AlpsExtension, AlpsLink
  ```

- **Notes**:
  - The `Title` field of `StatechartDocument` should be set from the root-level documentation value (if present).
  - `InitialStateId` is always `None` (ALPS has no initial state concept).
  - After implementing, test with: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`
  - Note that existing tests will fail at this point because they still reference `AlpsDocument` -- that is expected and will be fixed in WP05.

## Risks & Mitigations

- **Risk**: State classification heuristic mismatch with Mapper.fs.
  - **Mitigation**: Port the exact logic from Mapper.fs. The MapperTests in the test suite verify the heuristic produces the correct states. When tests are migrated in WP05, they will catch any discrepancy.
- **Risk**: Annotation ordering inconsistency breaks roundtrip tests.
  - **Mitigation**: Follow the deterministic ordering convention in data-model.md exactly. The ordering is: TransitionType first, then DescriptorHref, then Documentation, then Extensions, then Links.
- **Risk**: `ParseResult` error handling differs from `Result<_, _>` pattern.
  - **Mitigation**: `ParseResult` always has a `Document` field (best-effort). Error tests must check `result.Errors` list instead of `Expect.isError`.

## Review Guidance

- Verify the parser produces the same states as `Mapper.toStatechartDocument` for both golden files.
- Verify transitions have correct source/target/event/guard/parameters.
- Verify `AlpsMeta` annotations are placed on the correct AST nodes (document, state, transition).
- Verify annotation ordering matches data-model.md.
- Verify error handling returns `ParseResult` with `ParseFailure` errors (not exceptions).

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T23:45:00Z -- claude-opus -- lane=done -- Review approved. All 6 objectives pass: parseAlpsJson returns ParseResult, 3-part state heuristic ported faithfully, transitions extracted with correct fields and annotations, AlpsMeta annotations placed on correct AST nodes with deterministic ordering matching data-model.md, golden file tracing confirms correct state/transition classification, build succeeds with 0 errors/0 warnings across net8.0/net9.0/net10.0.
