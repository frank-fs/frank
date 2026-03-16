---
work_package_id: WP06
title: Tests
lane: planned
dependencies:
- WP01
- WP02
subtasks:
- T031
- T032
- T033
- T034
- T035
- T036
- T037
- T038
phase: Phase 5 - Tests
assignee: ''
agent: ''
shell_pid: ''
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-15T23:59:04Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-004
- FR-005
- FR-006
---

# Work Package Prompt: WP06 -- Tests

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

This WP depends on all previous WPs:

```bash
spec-kitty implement WP06 --base WP05
```

---

## Objectives & Success Criteria

1. FuzzyMatch tests verify Levenshtein distance correctness and suggestion ranking.
2. StalenessChecker tests verify fresh/stale/indeterminate detection.
3. HelpContent tests verify every command has complete metadata (SC-002).
4. HelpSubcommand tests verify command/topic lookup and fuzzy matching (SC-004).
5. StatusCommand tests verify all project states (SC-003).
6. HelpRenderer tests verify output format matches contracts (SC-005).
7. Existing ValidateCommandTests still pass after refactoring.
8. `dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj` passes with all tests green.

## Context & Constraints

- **Test Framework**: Expecto 10.2.3 with YoloDev.Expecto.TestSdk 0.14.3 (existing setup).
- **Test Patterns**: See existing test files in `test/Frank.Cli.Core.Tests/` for Expecto usage patterns.
- **Spec Success Criteria**: SC-002, SC-003, SC-004, SC-005 must be verified by tests.
- **Contracts**: `kitty-specs/016-frank-cli-help-system/contracts/cli-outputs.md` for expected output formats.
- **Data Model**: `kitty-specs/016-frank-cli-help-system/data-model.md` for state transition logic.

**Expecto Pattern** (from existing tests):

```fsharp
module MyTests =
    open Expecto

    [<Tests>]
    let tests =
        testList "Module Name" [
            test "test description" {
                let result = someFunction()
                Expect.equal result expected "failure message"
            }
        ]
```

## Subtasks & Detailed Guidance

### Subtask T031 -- Create FuzzyMatchTests.fs

**Purpose**: Verify the Levenshtein distance implementation and suggestion ranking are correct.

**Steps**:

1. Create `test/Frank.Cli.Core.Tests/Help/FuzzyMatchTests.fs`.

2. Use namespace and open:

```fsharp
module Frank.Cli.Core.Tests.Help.FuzzyMatchTests

open Expecto
open Frank.Cli.Core.Help
```

3. Test cases for Levenshtein distance:

```fsharp
[<Tests>]
let fuzzyMatchTests =
    testList "FuzzyMatch" [
        testList "levenshteinDistance" [
            test "identical strings have distance 0" {
                Expect.equal (FuzzyMatch.levenshteinDistance "compile" "compile") 0 ""
            }
            test "single insertion" {
                Expect.equal (FuzzyMatch.levenshteinDistance "compil" "compile") 1 ""
            }
            test "single deletion" {
                Expect.equal (FuzzyMatch.levenshteinDistance "compilee" "compile") 1 ""
            }
            test "single substitution" {
                Expect.equal (FuzzyMatch.levenshteinDistance "compilx" "compile") 1 ""
            }
            test "transposition counts as 2 edits" {
                // Levenshtein doesn't have transposition as primitive
                Expect.equal (FuzzyMatch.levenshteinDistance "comiple" "compile") 2 ""
            }
            test "empty string vs non-empty" {
                Expect.equal (FuzzyMatch.levenshteinDistance "" "hello") 5 ""
            }
            test "both empty" {
                Expect.equal (FuzzyMatch.levenshteinDistance "" "") 0 ""
            }
            test "completely different strings" {
                let d = FuzzyMatch.levenshteinDistance "abc" "xyz"
                Expect.equal d 3 ""
            }
        ]

        testList "suggest" [
            test "exact match returns distance 0" {
                let results = FuzzyMatch.suggest "compile" ["compile"; "extract"; "validate"] 3
                Expect.isNonEmpty results ""
                let (name, dist) = results.[0]
                Expect.equal name "compile" ""
                Expect.equal dist 0 ""
            }
            test "typo returns close match" {
                let results = FuzzyMatch.suggest "comiple" ["compile"; "extract"; "validate"] 3
                Expect.isNonEmpty results ""
                Expect.equal (fst results.[0]) "compile" ""
            }
            test "prefix match" {
                let results = FuzzyMatch.suggest "ext" ["compile"; "extract"; "validate"] 3
                Expect.isTrue (results |> List.exists (fun (n, _) -> n = "extract")) ""
            }
            test "no match beyond threshold" {
                let results = FuzzyMatch.suggest "zzzzzzzz" ["compile"; "extract"] 3
                Expect.isEmpty results ""
            }
            test "results sorted by distance" {
                let results = FuzzyMatch.suggest "compil" ["compile"; "compile2"; "extract"] 3
                match results with
                | (first, d1) :: (second, d2) :: _ -> Expect.isTrue (d1 <= d2) ""
                | _ -> ()
            }
            test "case insensitive" {
                let results = FuzzyMatch.suggest "COMPILE" ["compile"; "extract"] 3
                Expect.isNonEmpty results ""
                Expect.equal (fst results.[0]) "compile" ""
            }
        ]
    ]
```

**Files**: `test/Frank.Cli.Core.Tests/Help/FuzzyMatchTests.fs` (new, ~70 lines)
**Parallel?**: Yes -- independent of other test files.

---

### Subtask T032 -- Create StalenessCheckerTests.fs

**Purpose**: Verify the shared staleness detection logic produces correct results.

**Steps**:

1. Create `test/Frank.Cli.Core.Tests/Commands/StalenessCheckerTests.fs`.

2. Test cases:
   - Fresh: source files unchanged -> `Fresh`
   - Stale: source files changed -> `Stale`
   - Indeterminate: no source files in SourceMap -> `Indeterminate`

3. These tests need to create temporary files and ExtractionState records. Use `System.IO.Path.GetTempPath()` for temp files:

```fsharp
[<Tests>]
let stalenessTests =
    testList "StalenessChecker" [
        test "computeFileHash returns consistent hash" {
            let tmpFile = System.IO.Path.GetTempFileName()
            try
                System.IO.File.WriteAllText(tmpFile, "hello world")
                let hash1 = StalenessChecker.computeFileHash tmpFile
                let hash2 = StalenessChecker.computeFileHash tmpFile
                Expect.equal hash1 hash2 "Same file should produce same hash"
                Expect.isTrue (hash1.Length > 0) "Hash should not be empty"
            finally
                System.IO.File.Delete(tmpFile)
        }

        test "indeterminate when SourceMap is empty" {
            // Create a minimal ExtractionState with empty SourceMap
            let state = { ... SourceMap = Map.empty ... }
            let result = StalenessChecker.checkStaleness state
            Expect.equal result StalenessChecker.Indeterminate ""
        }

        // Additional tests for Fresh and Stale require creating
        // an ExtractionState with matching/mismatching SourceHash
    ]
```

4. Note: Creating full `ExtractionState` records for tests requires RDF graphs. You may need to use minimal/empty graphs (`new VDS.RDF.Graph() :> IGraph`).

**Files**: `test/Frank.Cli.Core.Tests/Commands/StalenessCheckerTests.fs` (new, ~70 lines)
**Parallel?**: Yes -- independent of other test files.

---

### Subtask T033 -- Create HelpContentTests.fs (Completeness Validation)

**Purpose**: Verify that every registered command has complete help metadata. This directly tests SC-002.

**Steps**:

1. Create `test/Frank.Cli.Core.Tests/Help/HelpContentTests.fs`.

2. Test cases:

```fsharp
[<Tests>]
let helpContentTests =
    testList "HelpContent" [
        test "all commands have non-empty summary" {
            for cmd in HelpContent.allCommands do
                Expect.isNotEmpty cmd.Summary $"Command '{cmd.Name}' has empty summary"
        }

        test "all commands have at least one example" {
            for cmd in HelpContent.allCommands do
                Expect.isNonEmpty cmd.Examples $"Command '{cmd.Name}' has no examples"
        }

        test "all commands have non-empty context" {
            for cmd in HelpContent.allCommands do
                Expect.isNotEmpty cmd.Context $"Command '{cmd.Name}' has empty context"
        }

        test "all prerequisite references are valid command names" {
            let commandNames = HelpContent.allCommands |> List.map (fun c -> c.Name) |> Set.ofList
            for cmd in HelpContent.allCommands do
                for prereq in cmd.Workflow.Prerequisites do
                    Expect.isTrue
                        (commandNames.Contains prereq)
                        $"Command '{cmd.Name}' has invalid prerequisite '{prereq}'"
        }

        test "all nextSteps references are valid command names" {
            let commandNames = HelpContent.allCommands |> List.map (fun c -> c.Name) |> Set.ofList
            for cmd in HelpContent.allCommands do
                for next in cmd.Workflow.NextSteps do
                    Expect.isTrue
                        (commandNames.Contains next)
                        $"Command '{cmd.Name}' has invalid next step '{next}'"
        }

        test "all topics have non-empty content" {
            for topic in HelpContent.allTopics do
                Expect.isNotEmpty topic.Content $"Topic '{topic.Name}' has empty content"
        }

        test "all topics have non-empty summary" {
            for topic in HelpContent.allTopics do
                Expect.isNotEmpty topic.Summary $"Topic '{topic.Name}' has empty summary"
        }

        test "there are exactly 7 commands" {
            Expect.equal HelpContent.allCommands.Length 7 "Expected 7 commands"
        }

        test "there are exactly 2 topics" {
            Expect.equal HelpContent.allTopics.Length 2 "Expected 2 topics"
        }

        test "findCommand returns Some for valid name" {
            Expect.isSome (HelpContent.findCommand "extract") ""
        }

        test "findCommand returns None for invalid name" {
            Expect.isNone (HelpContent.findCommand "nonexistent") ""
        }

        test "findTopic returns Some for valid name" {
            Expect.isSome (HelpContent.findTopic "workflows") ""
        }

        test "findTopic returns None for invalid name" {
            Expect.isNone (HelpContent.findTopic "nonexistent") ""
        }
    ]
```

**Files**: `test/Frank.Cli.Core.Tests/Help/HelpContentTests.fs` (new, ~80 lines)
**Parallel?**: Yes -- independent of other test files.

---

### Subtask T034 -- Create HelpSubcommandTests.fs

**Purpose**: Verify the help argument resolution logic (command lookup, topic lookup, fuzzy matching, index listing).

**Steps**:

1. Create `test/Frank.Cli.Core.Tests/Help/HelpSubcommandTests.fs`.

2. Test cases:

```fsharp
[<Tests>]
let helpSubcommandTests =
    testList "HelpSubcommand" [
        testList "resolve" [
            test "valid command name returns CommandMatch" {
                match HelpSubcommand.resolve "extract" with
                | CommandMatch cmd -> Expect.equal cmd.Name "extract" ""
                | _ -> failtest "Expected CommandMatch"
            }

            test "valid topic name returns TopicMatch" {
                match HelpSubcommand.resolve "workflows" with
                | TopicMatch topic -> Expect.equal topic.Name "workflows" ""
                | _ -> failtest "Expected TopicMatch"
            }

            test "unknown argument returns NoMatch with suggestions" {
                match HelpSubcommand.resolve "comiple" with
                | NoMatch suggestions ->
                    Expect.isTrue (suggestions |> List.contains "compile") "Should suggest compile"
                | _ -> failtest "Expected NoMatch"
            }

            test "completely unknown returns NoMatch with empty suggestions" {
                match HelpSubcommand.resolve "zzzzzzzzzzz" with
                | NoMatch suggestions -> Expect.isEmpty suggestions ""
                | _ -> failtest "Expected NoMatch"
            }

            test "case insensitive command lookup" {
                match HelpSubcommand.resolve "Extract" with
                | CommandMatch cmd -> Expect.equal cmd.Name "extract" ""
                | _ -> failtest "Expected CommandMatch"
            }

            test "case insensitive topic lookup" {
                match HelpSubcommand.resolve "Workflows" with
                | TopicMatch topic -> Expect.equal topic.Name "workflows" ""
                | _ -> failtest "Expected TopicMatch"
            }
        ]

        testList "listAll" [
            test "returns all commands" {
                let index = HelpSubcommand.listAll()
                Expect.equal index.Commands.Length 7 "Expected 7 commands"
            }

            test "returns all topics" {
                let index = HelpSubcommand.listAll()
                Expect.equal index.Topics.Length 2 "Expected 2 topics"
            }

            test "command entries have non-empty summaries" {
                let index = HelpSubcommand.listAll()
                for (name, summary) in index.Commands do
                    Expect.isNotEmpty summary $"Command '{name}' has empty summary"
            }
        ]
    ]
```

**Files**: `test/Frank.Cli.Core.Tests/Help/HelpSubcommandTests.fs` (new, ~70 lines)
**Parallel?**: Yes -- independent of other test files.

---

### Subtask T035 -- Create StatusCommandTests.fs

**Purpose**: Verify the status command correctly identifies all project states.

**Steps**:

1. Create `test/Frank.Cli.Core.Tests/Commands/StatusCommandTests.fs`.

2. Test cases need filesystem fixtures. Create temp directories:

```fsharp
[<Tests>]
let statusCommandTests =
    testList "StatusCommand" [
        test "missing project file returns Error" {
            let result = StatusCommand.execute "/nonexistent/path/Missing.fsproj"
            Expect.isError result "Should return Error for missing project"
        }

        test "no extraction state returns NotExtracted" {
            let tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString())
            System.IO.Directory.CreateDirectory(tmpDir) |> ignore
            let fsproj = System.IO.Path.Combine(tmpDir, "Test.fsproj")
            System.IO.File.WriteAllText(fsproj, "<Project></Project>")
            try
                let result = StatusCommand.execute fsproj
                match result with
                | Ok status ->
                    Expect.equal status.Extraction ExtractionStatus.NotExtracted ""
                    Expect.equal status.RecommendedAction RecommendedAction.RunExtract ""
                | Error e -> failtest $"Unexpected error: {e}"
            finally
                System.IO.Directory.Delete(tmpDir, true)
        }

        test "corrupt state file returns Unreadable" {
            let tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString())
            let stateDir = System.IO.Path.Combine(tmpDir, "obj", "frank-cli")
            System.IO.Directory.CreateDirectory(stateDir) |> ignore
            let fsproj = System.IO.Path.Combine(tmpDir, "Test.fsproj")
            System.IO.File.WriteAllText(fsproj, "<Project></Project>")
            System.IO.File.WriteAllText(System.IO.Path.Combine(stateDir, "state.json"), "not valid json{{{")
            try
                let result = StatusCommand.execute fsproj
                match result with
                | Ok status ->
                    match status.Extraction with
                    | ExtractionStatus.Unreadable _ -> ()
                    | other -> failtest $"Expected Unreadable, got {other}"
                    match status.RecommendedAction with
                    | RecommendedAction.RecoverExtract _ -> ()
                    | other -> failtest $"Expected RecoverExtract, got {other}"
                | Error e -> failtest $"Unexpected error: {e}"
            finally
                System.IO.Directory.Delete(tmpDir, true)
        }

        test "artifacts missing reports Missing" {
            let tmpDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), System.Guid.NewGuid().ToString())
            let stateDir = System.IO.Path.Combine(tmpDir, "obj", "frank-cli")
            System.IO.Directory.CreateDirectory(stateDir) |> ignore
            let fsproj = System.IO.Path.Combine(tmpDir, "Test.fsproj")
            System.IO.File.WriteAllText(fsproj, "<Project></Project>")
            try
                let result = StatusCommand.execute fsproj
                match result with
                | Ok status ->
                    match status.Artifacts with
                    | ArtifactStatus.Missing missing ->
                        Expect.isTrue (missing.Length > 0) "Should list missing files"
                    | _ -> failtest "Expected Missing artifacts"
                | Error e -> failtest $"Unexpected error: {e}"
            finally
                System.IO.Directory.Delete(tmpDir, true)
        }
    ]
```

3. Testing the "Current" and "Stale" states requires creating valid ExtractionState JSON files. This is more complex -- consider creating a helper that writes a minimal valid state.json.

**Files**: `test/Frank.Cli.Core.Tests/Commands/StatusCommandTests.fs` (new, ~120 lines)
**Parallel?**: Yes -- independent of other test files.

---

### Subtask T036 -- Create HelpRendererTests.fs

**Purpose**: Verify text and JSON output formatting matches the contracts.

**Steps**:

1. Create `test/Frank.Cli.Core.Tests/Help/HelpRendererTests.fs`.

2. Test cases:

```fsharp
[<Tests>]
let helpRendererTests =
    testList "HelpRenderer" [
        testList "text rendering" [
            test "WORKFLOW section has correct header" {
                let help = HelpContent.allCommands |> List.find (fun c -> c.Name = "extract")
                let text = HelpRenderer.renderWorkflowText help 5
                Expect.stringContains text "WORKFLOW" ""
            }

            test "WORKFLOW section shows step number" {
                let help = HelpContent.allCommands |> List.find (fun c -> c.Name = "extract")
                let text = HelpRenderer.renderWorkflowText help 5
                Expect.stringContains text "Step 1 of 5" ""
            }

            test "EXAMPLES section has correct header" {
                let help = HelpContent.allCommands |> List.find (fun c -> c.Name = "extract")
                let text = HelpRenderer.renderExamplesText help
                Expect.stringContains text "EXAMPLES" ""
            }

            test "CONTEXT section has correct header" {
                let help = HelpContent.allCommands |> List.find (fun c -> c.Name = "extract")
                let text = HelpRenderer.renderContextText help
                Expect.stringContains text "CONTEXT" ""
            }
        ]

        testList "JSON rendering" [
            test "command JSON is valid" {
                let help = HelpContent.allCommands |> List.find (fun c -> c.Name = "extract")
                let json = HelpRenderer.renderCommandJson help 5
                // Verify it's valid JSON by parsing it
                let doc = System.Text.Json.JsonDocument.Parse(json)
                Expect.equal (doc.RootElement.GetProperty("name").GetString()) "extract" ""
            }

            test "command JSON has examples array" {
                let help = HelpContent.allCommands |> List.find (fun c -> c.Name = "extract")
                let json = HelpRenderer.renderCommandJson help 5
                let doc = System.Text.Json.JsonDocument.Parse(json)
                let examples = doc.RootElement.GetProperty("examples")
                Expect.isTrue (examples.GetArrayLength() > 0) "Should have at least one example"
            }

            test "command JSON has workflow object" {
                let help = HelpContent.allCommands |> List.find (fun c -> c.Name = "extract")
                let json = HelpRenderer.renderCommandJson help 5
                let doc = System.Text.Json.JsonDocument.Parse(json)
                let workflow = doc.RootElement.GetProperty("workflow")
                Expect.equal (workflow.GetProperty("stepNumber").GetInt32()) 1 ""
            }
        ]
    ]
```

**Files**: `test/Frank.Cli.Core.Tests/Help/HelpRendererTests.fs` (new, ~80 lines)
**Parallel?**: Yes -- independent of other test files.

---

### Subtask T037 -- Update Test Project .fsproj

**Purpose**: Add all new test files to the test project's compile list.

**Steps**:

1. Open `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj`.

2. Add the new test file Compile entries. Insert them after the existing test entries but before `Program.fs`:

```xml
<!-- After Commands/CompileCommandTests.fs -->
<Compile Include="Commands/StalenessCheckerTests.fs" />
<Compile Include="Commands/StatusCommandTests.fs" />

<!-- After Output/TextOutputTests.fs -->
<Compile Include="Help/FuzzyMatchTests.fs" />
<Compile Include="Help/HelpContentTests.fs" />
<Compile Include="Help/HelpSubcommandTests.fs" />
<Compile Include="Help/HelpRendererTests.fs" />
```

3. Create the `test/Frank.Cli.Core.Tests/Help/` directory if it doesn't exist.

4. Run `dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj` to verify all tests pass.

**Files**: `test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj` (modify)
**Parallel?**: No -- must be done after T031-T036 create the files.

---

### Subtask T038 -- Verify Existing ValidateCommandTests Pass

**Purpose**: Confirm that the StalenessChecker extraction in WP01 did not break existing validation tests.

**Steps**:

1. Run: `dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj --filter "ValidateCommand"`

2. All existing ValidateCommandTests should pass with unchanged behavior.

3. If any tests fail, investigate whether the StalenessChecker refactoring changed the staleness detection behavior. The mapping from `StalenessResult` to `ValidationIssue` in ValidateCommand must produce the same `Message` string as the original implementation.

**Files**: No file changes -- verification only.
**Parallel?**: No -- depends on all previous work being complete.

**Validation**:
- [ ] `dotnet test test/Frank.Cli.Core.Tests/Frank.Cli.Core.Tests.fsproj` passes all tests
- [ ] All new test files are included in the compile list
- [ ] FuzzyMatch tests cover: exact match, typo, prefix, threshold, case insensitivity
- [ ] HelpContent tests verify: all commands have metadata, valid cross-references
- [ ] StatusCommand tests cover: missing project, no extraction, corrupt state
- [ ] HelpRenderer tests verify: section headers, JSON validity
- [ ] Existing ValidateCommandTests still pass

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| StatusCommand tests need complex ExtractionState fixtures | Use minimal valid state (empty graphs, minimal metadata) |
| Test file ordering in .fsproj matters | Follow existing pattern: test files after their module's tests |
| HelpRenderer output format tests are brittle | Test for key content presence (contains "WORKFLOW"), not exact string equality |

## Review Guidance

- Verify all test files follow Expecto patterns used in existing tests.
- Verify HelpContentTests cover SC-002 (every command has metadata).
- Verify StatusCommandTests cover SC-003 (all four states + error cases).
- Verify fixture cleanup (temp directories deleted in `finally` blocks).
- Run `dotnet test` to confirm all tests pass (green).
- Check that no existing tests are broken.

## Activity Log

- 2026-03-15T23:59:04Z -- system -- lane=planned -- Prompt created.
