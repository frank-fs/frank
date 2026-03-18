---
work_package_id: "WP10"
title: "MSBuild Integration"
lane: "doing"
dependencies: ["WP09"]
subtasks:
  - "T062"
  - "T063"
  - "T064"
  - "T065"
  - "T066"
  - "T067"
assignee: ""
agent: "claude-opus"
shell_pid: "15786"
review_status: ""
reviewed_by: ""
history:
  - timestamp: "2026-03-16T19:30:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks (manual completion after agent rate limit)"
---

# Work Package Prompt: WP10 -- MSBuild Integration

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Implementation Command

```bash
spec-kitty implement WP10 --base WP09
```

---

## Objectives & Success Criteria

Create an MSBuild targets file that automatically invokes `frank statechart generate --format all` after build, writing artifacts to `$(IntermediateOutputPath)statecharts/`.

**Success Criteria**:
1. Adding the target to a sample project and running `dotnet build` produces statechart spec files in the intermediate output directory
2. Target runs after compilation (AfterTargets="Build")
3. Target produces no errors on a project without stateful resources (graceful no-op)
4. Target is idempotent: running twice produces identical output
5. Target does not fail build if frank-cli is not installed (warning only)

## Context & Constraints

- **Spec**: `/kitty-specs/026-cli-statechart-commands/spec.md` -- User Story 5 (MSBuild Integration)
- **Plan**: `/kitty-specs/026-cli-statechart-commands/plan.md` -- D-008
- **Depends on**: WP09 (CLI wiring must be complete for `frank statechart generate` to work)

## Subtasks & Detailed Guidance

### Subtask T062 -- Create MSBuild targets file

- **Purpose**: Create the `.targets` file that downstream projects import
- **Files**: `src/Frank.Statecharts/build/Frank.Statecharts.targets` (new)
- **Steps**:
  1. Create directory `src/Frank.Statecharts/build/`
  2. Create targets file with `GenerateStatechartSpecs` target

### Subtask T063 -- Implement GenerateStatechartSpecs target

- **Purpose**: The target runs `frank-cli statechart generate --format all` after build
- **Files**: `src/Frank.Statecharts/build/Frank.Statecharts.targets`
- **Steps**:
  1. Target: `AfterTargets="Build"`
  2. Command: `frank-cli statechart generate --format all $(TargetPath) --output $(IntermediateOutputPath)statecharts/`
  3. Output directory: `$(IntermediateOutputPath)statecharts/` (e.g., `obj/Debug/net10.0/statecharts/`)

### Subtask T064 -- Add condition to skip when no stateful resources

- **Purpose**: Graceful no-op when the project has no stateful resources
- **Files**: `src/Frank.Statecharts/build/Frank.Statecharts.targets`
- **Steps**:
  1. frank-cli should exit cleanly with a message when no stateful resources are found
  2. Target should not fail the build in this case

### Subtask T065 -- Include targets file in fsproj as buildTransitive content

- **Purpose**: Make the targets file automatically available to consuming projects
- **Files**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Steps**:
  1. Add `<None Include="build/Frank.Statecharts.targets" Pack="true" PackagePath="buildTransitive/" />`

### Subtask T066 -- Verify target runs during dotnet build

- **Purpose**: End-to-end verification with a sample project
- **Steps**:
  1. Use or create a sample project with stateful resources
  2. Run `dotnet build`
  3. Verify statechart spec files appear in `$(IntermediateOutputPath)statecharts/`

### Subtask T067 -- Verify graceful behavior without stateful resources

- **Purpose**: Verify target doesn't break builds for non-statechart projects
- **Steps**:
  1. Run `dotnet build` on a project that references Frank.Statecharts but has no stateful resources
  2. Verify build succeeds with no errors
  3. Verify appropriate warning/info message

## Risks & Mitigations

1. **frank-cli must be installed as a dotnet tool**: Document this prerequisite. Target should emit a warning (not error) if frank-cli is not found.
2. **Cross-platform path handling**: Use MSBuild path properties (`$(TargetPath)`, `$(IntermediateOutputPath)`) which handle platform differences.

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

- 2026-03-16T19:30:00Z -- system -- lane=planned -- Prompt generated via /spec-kitty.tasks (manual completion)

---

### Updating Lane Status

To change a work package's lane, either:

1. **Edit directly**: Change the `lane:` field in frontmatter AND append activity log entry (at the end)
2. **Use CLI**: `spec-kitty agent tasks move-task WP10 --to <lane> --note "message"` (recommended)

**Valid lanes**: `planned`, `doing`, `for_review`, `done`
- 2026-03-18T03:18:56Z – unknown – lane=for_review – Ready for review: MSBuild targets file. kitty-specs diffs inherited from WP09 merge topology.
- 2026-03-18T03:18:59Z – claude-opus – shell_pid=15786 – lane=doing – Started review via workflow command
