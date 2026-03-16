---
work_package_id: "WP05"
title: "Build Verification and Cleanup"
phase: "Phase 4 - Verification"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP04"]
requirement_refs: ["FR-022", "FR-023"]
subtasks:
  - "T019"
  - "T020"
  - "T021"
  - "T022"
history:
  - timestamp: "2026-03-16T19:13:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP05 -- Build Verification and Cleanup

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Objectives & Success Criteria

- `dotnet build` succeeds for `Frank.Statecharts` across net8.0, net9.0, net10.0 (SC-008)
- `dotnet test` passes for all smcat test scenarios (SC-007)
- Zero references to deleted types: `SmcatDocument`, `SmcatState`, `SmcatTransition`, `SmcatElement`, smcat-specific `ParseResult`/`ParseFailure`/`ParseWarning`, `StateType`, `StateActivity` (SC-005)
- `Mapper.fs` does not exist in the project (SC-006)
- Cross-format validator tests pass without modification (SC-009)

## Context & Constraints

- **Spec**: Success Criteria SC-001 through SC-009
- **Prerequisite**: WP01-WP04 must all be complete
- This is primarily a validation WP -- no new code expected
- If issues are found, fix them in this WP

## Implementation Command

```bash
spec-kitty implement WP05 --base WP04
```

## Subtasks & Detailed Guidance

### Subtask T019 -- Multi-target build verification

**Purpose**: Confirm the project builds successfully across all target frameworks (FR-022).

**Steps**:

1. Run from repository root:
   ```bash
   dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj -c Release
   ```
   This should build for all targets (net8.0, net9.0, net10.0) defined in the project.

2. If there are build errors:
   - Read each error message carefully
   - Fix the issue in the relevant source file
   - Re-run the build

3. Also build the test project:
   ```bash
   dotnet build test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
   ```

**Files**: No files expected to change (validation only).
**Parallel?**: Yes, can run alongside T021.

**Validation**:
- [ ] `dotnet build` exits with code 0
- [ ] No warnings related to smcat migration

### Subtask T020 -- Full test suite verification

**Purpose**: Confirm all smcat tests pass (FR-021, SC-007).

**Steps**:

1. Run from repository root:
   ```bash
   dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat"
   ```

2. Verify the test count matches expectations:
   - LexerTests: unchanged count
   - LabelParserTests: unchanged count
   - ParserTests: unchanged count
   - ErrorTests: unchanged count
   - GeneratorTests: may have fewer tests if `formatLabel`/`formatTransition` tests were removed
   - RoundTripTests: unchanged count

3. If any tests fail:
   - Read the failure message
   - Identify the root cause (type mismatch, assertion mismatch, etc.)
   - Fix the relevant test file or source file
   - Re-run

4. Also run the full test suite to catch any regressions:
   ```bash
   dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
   ```

**Files**: No files expected to change (validation only).

**Validation**:
- [ ] All smcat tests pass
- [ ] No regressions in non-smcat tests

### Subtask T021 -- Deleted type reference scan

**Purpose**: Confirm zero references to deleted types remain anywhere in the codebase (SC-005, SC-006).

**Steps**:

1. Run grep searches for each deleted type:
   ```bash
   grep -r "SmcatDocument" src/ test/ --include="*.fs" --include="*.fsx"
   grep -r "SmcatState" src/ test/ --include="*.fs" --include="*.fsx"
   grep -r "SmcatTransition" src/ test/ --include="*.fs" --include="*.fsx"
   grep -r "SmcatElement" src/ test/ --include="*.fs" --include="*.fsx"
   grep -r "StateDeclaration" src/ test/ --include="*.fs" --include="*.fsx"
   grep -r "CommentElement" src/ test/ --include="*.fs" --include="*.fsx"
   grep -r "Smcat\.Mapper" src/ test/ --include="*.fs" --include="*.fsx"
   ```

   Note: `StateDeclaration` was the smcat-specific DU case (now `StateDecl`). `CommentElement` was also smcat-specific.

2. Check that `Mapper.fs` does not exist:
   ```bash
   ls src/Frank.Statecharts/Smcat/Mapper.fs
   ```
   Should return "No such file or directory".

3. Check the fsproj for any stale references:
   ```bash
   grep "Mapper" src/Frank.Statecharts/Frank.Statecharts.fsproj
   ```
   Should return nothing.

4. If any references are found, fix them:
   - In source files: update to use shared AST types
   - In test files: update assertions
   - In project files: remove stale entries

**Files**: No files expected to change (validation only).
**Parallel?**: Yes, can run alongside T019.

**Validation**:
- [ ] Zero grep matches for `SmcatDocument` in `.fs` files
- [ ] Zero grep matches for `SmcatState` in `.fs` files (except `SmcatStateLabel` in Ast/Types.fs which is correct)
- [ ] Zero grep matches for `SmcatTransition` in `.fs` files
- [ ] Zero grep matches for `SmcatElement` in `.fs` files
- [ ] Zero grep matches for `StateDeclaration` in `.fs` files (now `StateDecl`)
- [ ] Zero grep matches for `Smcat.Mapper` in `.fs` files
- [ ] `Mapper.fs` does not exist on disk

### Subtask T022 -- Cross-format validator verification

**Purpose**: Confirm the cross-format validator works with smcat artifacts without the Mapper (SC-009).

**Steps**:

1. Check `test/Frank.Statecharts.Tests/Validation/CrossFormatTests.fs` for any references to `Smcat.Mapper`:
   ```bash
   grep "Mapper\|SmcatDocument\|toStatechartDocument" test/Frank.Statecharts.Tests/Validation/CrossFormatTests.fs
   ```

2. The cross-format validator tests construct `FormatArtifact` values with `StatechartDocument` directly (not via the Mapper). Verify they still compile and pass:
   ```bash
   dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "CrossFormat"
   ```

3. If any cross-format tests reference the Mapper, update them to use the parser directly:
   ```fsharp
   // OLD: let doc = Smcat.Mapper.toStatechartDocument (parseSmcat text)
   // NEW: let result = parseSmcat text; let doc = result.Document
   ```

4. Run the full validation test suite:
   ```bash
   dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Validation"
   ```

**Files**: Possibly `test/Frank.Statecharts.Tests/Validation/CrossFormatTests.fs` (if it references Mapper).

**Validation**:
- [ ] No references to `Smcat.Mapper` in cross-format tests
- [ ] All cross-format validator tests pass
- [ ] All validation tests pass

## Risks & Mitigations

- **Risk**: A non-obvious reference to a deleted type exists in a file not checked. **Mitigation**: The grep searches cover all `.fs` files in `src/` and `test/`.
- **Risk**: Cross-format tests depend on Mapper behavior. **Mitigation**: From code review, cross-format tests use `StatechartDocument` directly, not via Mapper.

## Review Guidance

- Verify all `dotnet build` and `dotnet test` commands pass
- Verify grep output shows zero matches for deleted types
- This is a "green light" WP -- if everything passes, the migration is complete

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
