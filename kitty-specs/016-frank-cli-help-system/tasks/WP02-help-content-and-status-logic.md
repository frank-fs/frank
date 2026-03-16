---
work_package_id: "WP02"
subtasks:
  - "T007"
  - "T008"
  - "T009"
  - "T010"
  - "T011"
  - "T012"
title: "Help Content and Status Command Logic"
phase: "Phase 2 - Content and Logic"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs:
  - "FR-004"
  - "FR-005"
  - "FR-018"
  - "FR-019"
  - "FR-020"
  - "FR-021"
  - "FR-022"
  - "FR-024"
history:
  - timestamp: "2026-03-15T23:59:04Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- Help Content and Status Command Logic

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

This WP depends on WP01:

```bash
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

1. Every command (extract, clarify, validate, diff, compile, help, status) has a complete `CommandHelp` record with non-empty summary, at least one example, valid workflow position, and context description.
2. Both help topics ("workflows" and "concepts") have complete `HelpTopic` records.
3. All prerequisite references in `WorkflowPosition` point to valid command names.
4. StatusCommand.execute correctly identifies all project states (NotExtracted, Current, Stale, UpToDate, Unreadable).
5. StatusCommand detects artifact files (ontology.owl.xml, shapes.shacl.ttl, manifest.json).
6. `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` succeeds.

## Context & Constraints

- **Spec**: `kitty-specs/016-frank-cli-help-system/spec.md` (FR-001 through FR-006 content model, FR-018 through FR-024 status command)
- **Data Model**: `kitty-specs/016-frank-cli-help-system/data-model.md` (ProjectStatus state transition logic)
- **Contracts**: `kitty-specs/016-frank-cli-help-system/contracts/cli-outputs.md` (exact text for command descriptions, workflow content, topic content)
- **Research**: `kitty-specs/016-frank-cli-help-system/research.md` (R5: compiled artifact detection)
- **Quickstart**: `kitty-specs/016-frank-cli-help-system/quickstart.md` (implementation order)
- **Constitution**: Principle VII (No Silent Exception Swallowing) -- StatusCommand must surface errors from corrupt state files.

## Subtasks & Detailed Guidance

### Subtask T007 -- Create HelpContent.fs with CommandHelp Records

**Purpose**: Define the complete, hardcoded help metadata for all 7 frank-cli commands. This is the primary documentation surface for LLM agents.

**Steps**:

1. Create `src/Frank.Cli.Core/Help/HelpContent.fs`.

2. Use namespace `Frank.Cli.Core.Help`.

3. Define `CommandHelp` records for all 7 commands. The summaries must match the existing `Description` strings in Program.fs:

```fsharp
module HelpContent =

    let extractHelp: CommandHelp =
        { Name = "extract"
          Summary = "Extract semantic definitions from F# source"
          Examples =
            [ { Invocation = "frank-cli extract --project MyApp/MyApp.fsproj --base-uri http://example.org/"
                Description = "Extract semantic definitions from the MyApp project." }
              { Invocation = "frank-cli extract --project MyApp/MyApp.fsproj --base-uri http://example.org/ --vocabularies schema.org,foaf"
                Description = "Extract with multiple vocabulary alignments." } ]
          Workflow =
            { StepNumber = 1
              Prerequisites = []
              NextSteps = [ "clarify"; "validate" ]
              IsOptional = false }
          Context =
            "The extract command analyzes F# source code to derive an OWL ontology and SHACL shapes. It maps F# record types to OWL classes, record fields to OWL properties, and route definitions to resource identities. The extraction state is saved to obj/frank-cli/state.json for use by subsequent commands (clarify, validate, compile)." }
```

4. Define help records for all remaining commands. Use the contracts document for exact text. The pipeline commands and their workflow positions:

   - **extract**: Step 1, no prerequisites, next: clarify/validate, required
   - **clarify**: Step 2, prerequisites: [extract], next: [validate], optional
   - **validate**: Step 3, prerequisites: [extract], next: [compile], required
   - **diff**: Step 4, prerequisites: [extract], next: [], optional
   - **compile**: Step 5, prerequisites: [extract] (validate recommended), next: [], required
   - **help**: Step 0 (non-pipeline), no prerequisites, no next steps, not optional
   - **status**: Step 0 (non-pipeline), no prerequisites, no next steps, not optional

5. The `Context` field for each command should explain its semantic meaning in Frank's model (not implementation details). Reference the contracts document.

**Files**: `src/Frank.Cli.Core/Help/HelpContent.fs` (new, ~120 lines)
**Parallel?**: Yes -- can proceed alongside T010/T011.

**Key content for commands** (match these summaries):
- extract: "Extract semantic definitions from F# source"
- clarify: "Identify ambiguities requiring human input"
- validate: "Validate completeness and consistency of extracted definitions"
- diff: "Compare current extraction state with a previous snapshot"
- compile: "Generate OWL/XML and SHACL artifacts from extraction state"
- status: "Show project extraction and compilation status"
- help: "Show help topics and command documentation"

---

### Subtask T008 -- Add HelpTopic Records

**Purpose**: Define the "workflows" and "concepts" help topics that provide cross-cutting documentation for LLM agents.

**Steps**:

1. In `src/Frank.Cli.Core/Help/HelpContent.fs`, add topic records:

```fsharp
    let workflowsTopic: HelpTopic =
        { Name = "workflows"
          Summary = "End-to-end guide to the extraction pipeline"
          Content = """The frank-cli extraction pipeline transforms F# source code into semantic
definitions (OWL ontology + SHACL shapes) through a series of commands:

  Step 1: extract (required)
    Analyzes F# source code and produces initial semantic definitions.
    Prerequisites: (none)
    Next: clarify, validate

  Step 2: clarify (optional)
    Identifies ambiguities in the extraction and presents questions.
    Prerequisites: extract
    Next: validate

  Step 3: validate (required)
    Checks completeness and consistency of the extracted definitions.
    Prerequisites: extract
    Next: compile

  Step 4: diff (optional)
    Compares current extraction state with a previous snapshot.
    Prerequisites: extract
    Next: (informational only)

  Step 5: compile (required)
    Generates final OWL/XML and SHACL artifact files.
    Prerequisites: extract (validate recommended)
    Next: (end of pipeline)

Typical usage:
  frank-cli extract --project MyApp.fsproj --base-uri http://example.org/
  frank-cli clarify --project MyApp.fsproj
  frank-cli validate --project MyApp.fsproj
  frank-cli compile --project MyApp.fsproj""" }
```

2. Define the "concepts" topic explaining Frank's semantic model:
   - F# record types map to OWL classes
   - Record fields map to OWL properties
   - Route definitions (Frank resources) map to resource identities
   - Type constraints map to SHACL shapes
   - Vocabulary alignment maps F# types to existing vocabularies (schema.org, etc.)

3. Content must be informative for LLM agents -- explain *what* things mean, not *how* the code works.

**Files**: `src/Frank.Cli.Core/Help/HelpContent.fs` (extend, ~80 additional lines)
**Parallel?**: Yes -- same file as T007 but different content.

---

### Subtask T009 -- Add Content Lookup Functions

**Purpose**: Provide functions to query the help content, used by HelpSubcommand and HelpRenderer.

**Steps**:

1. Add to `HelpContent` module:

```fsharp
    /// Total number of steps in the pipeline (for "Step N of M" display).
    let pipelineStepCount = 5

    /// All command help records.
    let allCommands: CommandHelp list =
        [ extractHelp; clarifyHelp; validateHelp; diffHelp; compileHelp; statusHelp; helpHelp ]

    /// All topic records.
    let allTopics: HelpTopic list = [ workflowsTopic; conceptsTopic ]

    /// Find a command by name (case-insensitive).
    let findCommand (name: string) : CommandHelp option =
        allCommands |> List.tryFind (fun c -> c.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase))

    /// Find a topic by name (case-insensitive).
    let findTopic (name: string) : HelpTopic option =
        allTopics |> List.tryFind (fun t -> t.Name.Equals(name, System.StringComparison.OrdinalIgnoreCase))

    /// All known names (commands + topics) for fuzzy matching.
    let allNames: string list =
        (allCommands |> List.map (fun c -> c.Name)) @ (allTopics |> List.map (fun t -> t.Name))
```

**Files**: `src/Frank.Cli.Core/Help/HelpContent.fs` (extend, ~25 additional lines)
**Parallel?**: No -- depends on T007/T008 defining the records.

---

### Subtask T010 -- Create StatusCommand.fs

**Purpose**: Implement the project status inspection logic that determines extraction state, artifact presence, and recommended next action.

**Steps**:

1. Create `src/Frank.Cli.Core/Commands/StatusCommand.fs`.

2. Use namespace `Frank.Cli.Core.Commands`.

3. Open `Frank.Cli.Core.Help` (for ProjectStatus types) and `Frank.Cli.Core.State` (for ExtractionState).

4. Implement the `execute` function:

```fsharp
module StatusCommand =

    let private artifactFileNames =
        [ "ontology.owl.xml"; "shapes.shacl.ttl"; "manifest.json" ]

    let private checkArtifacts (stateDir: string) : ArtifactStatus =
        let missing =
            artifactFileNames
            |> List.filter (fun name ->
                not (System.IO.File.Exists(System.IO.Path.Combine(stateDir, name))))
        if missing.IsEmpty then Present
        else Missing missing

    let private determineAction (extraction: ExtractionStatus) (artifacts: ArtifactStatus) : RecommendedAction =
        match extraction, artifacts with
        | NotExtracted, _ -> RunExtract
        | Unreadable reason, _ -> RecoverExtract reason
        | Stale, _ -> ReExtract
        | Current, Missing _ -> RunCompile
        | Current, Present -> UpToDate

    let execute (projectPath: string) : Result<ProjectStatus, string> =
        if not (System.IO.File.Exists projectPath) then
            Error $"Project file not found: {projectPath}"
        else
            let projectDir = System.IO.Path.GetDirectoryName(projectPath)
            let stateDir = System.IO.Path.Combine(projectDir, "obj", "frank-cli")
            let statePath = ExtractionState.defaultStatePath projectDir

            let extraction =
                if not (System.IO.File.Exists statePath) then
                    NotExtracted
                else
                    match ExtractionState.load statePath with
                    | Error reason -> Unreadable reason
                    | Ok state ->
                        match StalenessChecker.checkStaleness state with
                        | StalenessChecker.Stale -> Stale
                        | StalenessChecker.Fresh -> Current
                        | StalenessChecker.Indeterminate -> Current

            let artifacts = checkArtifacts stateDir
            let action = determineAction extraction artifacts

            Ok
                { ProjectPath = projectPath
                  Extraction = extraction
                  Artifacts = artifacts
                  RecommendedAction = action
                  StateDirectory = stateDir }
```

5. The state transition logic must match data-model.md exactly:
   - No state.json -> NotExtracted + Missing -> RunExtract
   - Unreadable state.json -> Unreadable -> RecoverExtract
   - Current state.json + Missing artifacts -> RunCompile
   - Current state.json + Present artifacts -> UpToDate
   - Stale state.json -> ReExtract

**Files**: `src/Frank.Cli.Core/Commands/StatusCommand.fs` (new, ~55 lines)
**Parallel?**: Yes -- different file from T007-T009.

**Edge Cases**:
- Project path does not exist: return `Error "Project file not found: ..."`
- State directory does not exist: `File.Exists(statePath)` returns false -> `NotExtracted`
- State file is corrupt JSON: `ExtractionState.load` returns `Error` -> `Unreadable`
- Some but not all artifacts present: `Missing` with list of missing file names

---

### Subtask T011 -- Add Artifact Detection to StatusCommand

**Purpose**: The artifact detection is embedded in T010 above (the `checkArtifacts` function). This subtask is a verification step -- ensure the three artifact filenames match what ArtifactSerializer actually writes.

**Steps**:

1. Verify that `artifactFileNames` in StatusCommand matches `ArtifactSerializer.writeArtifacts`:
   - `ontology.owl.xml` (from `Output/ArtifactSerializer.fs` line 55)
   - `shapes.shacl.ttl` (from `Output/ArtifactSerializer.fs` line 56)
   - `manifest.json` (from `Output/ArtifactSerializer.fs` line 57)

2. If any filenames differ, update `StatusCommand.artifactFileNames` to match.

**Files**: `src/Frank.Cli.Core/Commands/StatusCommand.fs` (verify)
**Parallel?**: No -- depends on T010.

---

### Subtask T012 -- Update Frank.Cli.Core.fsproj with New Compile Entries

**Purpose**: Add HelpContent.fs and StatusCommand.fs to the compile list in the correct order.

**Steps**:

1. Add to `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`:

```xml
<!-- After Help/FuzzyMatch.fs -->
<Compile Include="Help/HelpContent.fs" />

<!-- After Commands/CompileCommand.fs -->
<Compile Include="Commands/StatusCommand.fs" />
```

2. The compile order after WP01 + WP02:

```xml
<Compile Include="Help/HelpTypes.fs" />
<Compile Include="Help/FuzzyMatch.fs" />
<Compile Include="Help/HelpContent.fs" />
<Compile Include="Output/ArtifactSerializer.fs" />
<Compile Include="Commands/ExtractCommand.fs" />
<Compile Include="Commands/ClarifyCommand.fs" />
<Compile Include="Commands/StalenessChecker.fs" />
<Compile Include="Commands/ValidateCommand.fs" />
<Compile Include="Commands/DiffCommand.fs" />
<Compile Include="Commands/CompileCommand.fs" />
<Compile Include="Commands/StatusCommand.fs" />
```

3. Run `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` to verify.

**Files**: `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` (modify)
**Parallel?**: No -- must be done after T007-T011 create the files.

**Validation**:
- [ ] `dotnet build src/Frank.Cli.Core/Frank.Cli.Core.fsproj` succeeds
- [ ] All 7 CommandHelp records are defined with non-empty fields
- [ ] All prerequisite references point to valid command names
- [ ] Both topics have non-empty content
- [ ] StatusCommand.execute handles all 5 state combinations correctly

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Help content text drifts from contracts | Use contracts/cli-outputs.md as source of truth; tests in WP06 validate |
| StatusCommand fails with corrupt state.json | ExtractionState.load already returns Error for parse failures; map to Unreadable |
| Artifact filenames change in future | Use constants that can be updated in one place |

## Review Guidance

- Verify all 7 commands have complete CommandHelp records (summary, >= 1 example, context).
- Verify workflow positions form a valid DAG (no circular prerequisites).
- Verify topic content is informative for LLM agents (semantic explanations, not code details).
- Verify StatusCommand state transition logic matches data-model.md.
- Verify StatusCommand returns Error for missing project file (not an exception).
- Run `dotnet build` to confirm compilation.

## Activity Log

- 2026-03-15T23:59:04Z -- system -- lane=planned -- Prompt created.
