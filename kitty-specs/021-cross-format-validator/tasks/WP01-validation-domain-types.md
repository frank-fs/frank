---
work_package_id: "WP01"
subtasks:
  - "T001"
  - "T002"
  - "T003"
  - "T004"
  - "T005"
  - "T006"
  - "T007"
  - "T008"
title: "Validation Domain Types"
phase: "Phase 0 - Foundation"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: []
requirement_refs: ["FR-001", "FR-002", "FR-003", "FR-004", "FR-005"]
history:
  - timestamp: "2026-03-15T23:59:11Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP01 -- Validation Domain Types

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````fsharp`, ````bash`

---

## Implementation Command

```bash
spec-kitty implement WP01
```

No dependencies -- this is the starting work package.

---

## Objectives & Success Criteria

- Define all validation domain types in `src/Frank.Statecharts/Validation/Types.fs` matching the contract in `contracts/validation-api.fsi`.
- Types must conform exactly to the data model in `kitty-specs/021-cross-format-validator/data-model.md`.
- All record types must support structural equality (except `ValidationRule` which has a function field).
- The project must compile after `.fsproj` updates.
- **Success**: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds with the new types available in the `Frank.Statecharts.Validation` namespace.

---

## Context & Constraints

- **Spec**: `kitty-specs/021-cross-format-validator/spec.md` -- FR-001 through FR-005
- **Plan**: `kitty-specs/021-cross-format-validator/plan.md` -- project structure, namespace conventions
- **Data Model**: `kitty-specs/021-cross-format-validator/data-model.md` -- exact field definitions
- **Contract**: `kitty-specs/021-cross-format-validator/contracts/validation-api.fsi` -- API surface
- **Research**: `kitty-specs/021-cross-format-validator/research.md` -- decisions D-001 through D-007
- **Constitution**: `.kittify/memory/constitution.md` -- principle II (Idiomatic F#), principle VIII (No Duplicated Logic)

### Key Constraints
- Namespace: `Frank.Statecharts.Validation`
- The types depend on `StatechartDocument` from `Frank.Statecharts.Ast` namespace (spec 020).
- If `src/Frank.Statecharts/Ast/Types.fs` does not yet exist, you MUST create a minimal stub so Validation types compile. See guidance in T001.
- No external NuGet dependencies. No mutable state. Pure F# records and discriminated unions.

---

## Subtasks & Detailed Guidance

### Subtask T001 -- Create `Validation/` directory and verify AST dependency

**Purpose**: Establish the directory structure for validation source files and ensure the shared AST types from spec 020 are available.

**Steps**:
1. Create directory `src/Frank.Statecharts/Validation/` if it does not exist.
2. Check whether `src/Frank.Statecharts/Ast/Types.fs` exists.
   - If it exists: verify it contains `StatechartDocument`, `StateNode`, `TransitionEdge`, `StatechartElement`, `GroupBlock`, `GroupBranch` types.
   - If it does NOT exist: create a minimal stub file at `src/Frank.Statecharts/Ast/Types.fs` with the types needed by the validator. The stub must include at minimum:
     ```fsharp
     namespace Frank.Statecharts.Ast

     [<Struct>]
     type SourcePosition = { Line: int; Column: int }

     type StateKind = Regular | Initial | Final | Parallel | ShallowHistory | DeepHistory | Choice | ForkJoin | Terminate

     type StateActivities = { Entry: string list; Exit: string list; Do: string list }

     type Annotation = | WsdAnnotation of obj  // Stub -- full definition in spec 020

     type StateNode =
         { Identifier: string
           Label: string option
           Kind: StateKind
           Children: StateNode list
           Activities: StateActivities option
           Position: SourcePosition option
           Annotations: Annotation list }

     type TransitionEdge =
         { Source: string
           Target: string option
           Event: string option
           Guard: string option
           Action: string option
           Parameters: string list
           Position: SourcePosition option
           Annotations: Annotation list }

     type DataEntry = { Name: string; Expression: string option; Position: SourcePosition option }
     type NoteContent = { Target: string; Content: string; Position: SourcePosition option; Annotations: Annotation list }
     type GroupKind = Alt | Opt | Loop | Par | Break | Critical | Ref

     type GroupBranch = { Condition: string option; Elements: StatechartElement list }
     and GroupBlock = { Kind: GroupKind; Branches: GroupBranch list; Position: SourcePosition option }
     and Directive =
         | TitleDirective of title: string * position: SourcePosition option
         | AutoNumberDirective of position: SourcePosition option
     and StatechartElement =
         | StateDecl of StateNode
         | TransitionElement of TransitionEdge
         | NoteElement of NoteContent
         | GroupElement of GroupBlock
         | DirectiveElement of Directive

     type StatechartDocument =
         { Title: string option
           InitialStateId: string option
           Elements: StatechartElement list
           DataEntries: DataEntry list
           Annotations: Annotation list }
     ```
   - If creating the stub, also add `Ast/Types.fs` to the `.fsproj` compile order BEFORE `Wsd/Types.fs`.

**Files**: `src/Frank.Statecharts/Validation/` (new directory), optionally `src/Frank.Statecharts/Ast/Types.fs` (new stub file)
**Parallel?**: No -- must be done first.

---

### Subtask T002 -- Define `FormatTag` discriminated union

**Purpose**: Create the format identification type used to tag artifacts and declare rule requirements (FR-002, D-003).

**Steps**:
1. Create file `src/Frank.Statecharts/Validation/Types.fs`.
2. Add namespace declaration: `namespace Frank.Statecharts.Validation`
3. Define `FormatTag`:
   ```fsharp
   /// Identifies which parser produced an artifact.
   type FormatTag =
       | Wsd
       | Alps
       | Scxml
       | Smcat
       | XState
   ```

**Files**: `src/Frank.Statecharts/Validation/Types.fs` (new file)
**Notes**: PascalCase case names per F# conventions. Simple DU with no payload -- supports structural equality and comparison.

---

### Subtask T003 -- Define `FormatArtifact` record

**Purpose**: Create the format-tagged wrapper around a parsed `StatechartDocument` (FR-002).

**Steps**:
1. Add to `Types.fs` after `FormatTag`:
   ```fsharp
   open Frank.Statecharts.Ast

   /// A format-tagged wrapper around a parsed StatechartDocument.
   type FormatArtifact =
       { Format: FormatTag
         Document: StatechartDocument }
   ```

**Files**: `src/Frank.Statecharts/Validation/Types.fs`
**Notes**: The `open Frank.Statecharts.Ast` import must appear before any type that references AST types.

---

### Subtask T004 -- Define `CheckStatus` DU and `ValidationCheck` record

**Purpose**: Model the result status of a single validation check (FR-004).

**Steps**:
1. Add to `Types.fs`:
   ```fsharp
   /// Status of a single validation check.
   type CheckStatus =
       | Pass
       | Fail
       | Skip

   /// A named invariant result from a validation rule.
   type ValidationCheck =
       { Name: string
         Status: CheckStatus
         Reason: string option }
   ```

**Files**: `src/Frank.Statecharts/Validation/Types.fs`
**Notes**: `Reason` is populated for `Skip` (why skipped) and optionally for `Fail` (brief note). Detailed failure info is in `ValidationFailure`.

---

### Subtask T005 -- Define `ValidationFailure` record

**Purpose**: Capture detailed diagnostic information for a single validation failure (FR-005, FR-015).

**Steps**:
1. Add to `Types.fs`:
   ```fsharp
   /// Detailed diagnostic information for a single validation failure.
   type ValidationFailure =
       { Formats: FormatTag list
         EntityType: string
         Expected: string
         Actual: string
         Description: string }
   ```

**Files**: `src/Frank.Statecharts/Validation/Types.fs`
**Notes**:
- `Formats` has 1 element for self-consistency checks, 2 for cross-format checks.
- All string fields are free-form for maximum flexibility (D-007).
- Supports structural equality.

---

### Subtask T006 -- Define `ValidationReport` record

**Purpose**: Top-level aggregated result of a validation run (FR-003, FR-009).

**Steps**:
1. Add to `Types.fs`:
   ```fsharp
   /// Top-level result of a validation run.
   type ValidationReport =
       { TotalChecks: int
         TotalSkipped: int
         TotalFailures: int
         Checks: ValidationCheck list
         Failures: ValidationFailure list }
   ```

**Files**: `src/Frank.Statecharts/Validation/Types.fs`
**Notes**:
- `TotalChecks` = count of Pass + Fail (excludes Skip).
- `TotalFailures` = `Failures.Length` = count of Fail in Checks.
- Empty artifact set produces `{ TotalChecks = 0; TotalSkipped = N; TotalFailures = 0; ... }`.

---

### Subtask T007 -- Define `ValidationRule` record

**Purpose**: Define the validation rule contract (FR-001, FR-017, D-002).

**Steps**:
1. Add to `Types.fs`:
   ```fsharp
   /// A validation rule defined by a format module.
   type ValidationRule =
       { Name: string
         RequiredFormats: FormatTag Set
         Check: FormatArtifact list -> ValidationCheck list }
   ```

**Files**: `src/Frank.Statecharts/Validation/Types.fs`
**Notes**:
- Does NOT support structural equality due to function field -- this is expected and documented in data-model.md.
- `RequiredFormats = Set.empty` means universal rule (runs on any artifact set).
- The `Check` function receives the full artifact list; the orchestrator handles skip logic.

---

### Subtask T008 -- Update `Frank.Statecharts.fsproj` compile order

**Purpose**: Add the new `Validation/Types.fs` file to the project's compile order so it is included in the build.

**Steps**:
1. Open `src/Frank.Statecharts/Frank.Statecharts.fsproj`.
2. Add `<Compile Include="Validation/Types.fs" />` to the `<ItemGroup>` containing source files.
3. The compile order must be:
   - `Ast/Types.fs` (if created as stub in T001)
   - `Wsd/Types.fs`
   - `Wsd/Lexer.fs`
   - `Wsd/GuardParser.fs`
   - `Wsd/Parser.fs`
   - `Validation/Types.fs` <-- NEW (after Ast/, before or after Wsd/ -- must come after Ast/ since it depends on `StatechartDocument`)
   - `Types.fs`
   - ... (remaining existing files)
4. If `Ast/Types.fs` was created as a stub, add `<Compile Include="Ast/Types.fs" />` BEFORE `Wsd/Types.fs`.
5. Verify: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj`

**Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
**Notes**: F# compile order is significant. `Validation/Types.fs` depends on `Ast/Types.fs` (for `StatechartDocument`), so it must come after. It does NOT depend on `Wsd/` files.

---

## Risks & Mitigations

- **Spec 020 AST types not yet implemented**: If `Ast/Types.fs` does not exist, create a minimal stub. The stub may need updating when spec 020 is implemented, but the Validation types will be correct.
- **Compile order errors**: F# is order-sensitive. Verify with `dotnet build` after making changes.
- **Structural equality on ValidationRule**: The function field prevents structural equality. This is expected and documented -- do not try to force `[<CustomEquality>]`.

---

## Review Guidance

- Verify all types match `contracts/validation-api.fsi` exactly (field names, types, order).
- Verify namespace is `Frank.Statecharts.Validation`.
- Verify `FormatTag` has exactly 5 cases: Wsd, Alps, Scxml, Smcat, XState.
- Verify `ValidationReport` field semantics match data-model.md (TotalChecks = Pass + Fail, not Skip).
- Verify `.fsproj` compile order is correct and `dotnet build` succeeds.
- If AST stub was created, verify it contains all types referenced in the data-model for spec 020.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-15T23:59:11Z -- system -- lane=planned -- Prompt created.
