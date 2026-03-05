---
work_package_id: WP06
title: CLI Commands — Validate, Diff, Compile
lane: "doing"
dependencies: [WP05]
base_branch: 001-semantic-resources-phase1-WP05
base_commit: abe61520e8fdba26efb99e1078b280a99e998dc6
created_at: '2026-03-05T23:39:06.552927+00:00'
subtasks:
- T029
- T030
- T031
- T032
- T033
- T034
phase: Phase 1 - CLI
assignee: ''
agent: "claude-opus"
shell_pid: "10839"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-04T22:10:13Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-009, FR-010, FR-011, FR-012]
---

# WP06: CLI Commands — Validate, Diff, Compile

> **Review Feedback Status**: No review feedback yet.

## Review Feedback

_No feedback recorded._

> **Markdown Formatting Note**: Use ATX headings (`#`), fenced code blocks with language tags, and standard bullet lists. Do not use HTML tags or custom directives.

## Implementation Command

```
spec-kitty implement WP06 --base WP05
```

## Objectives & Success Criteria

Complete the `frank-cli` tool with the `validate`, `diff`, and `compile` subcommands, wire all five subcommands through a single `Program.fs` entry point, and package the tool as a `dotnet tool` NuGet package.

Success criteria:
- `frank-cli validate` reports coverage metrics and consistency issues per the contract schema
- `frank-cli diff` computes a structured diff between two `ExtractionState` snapshots
- `frank-cli compile` writes `ontology.owl.xml`, `shapes.shacl.ttl`, and `manifest.json` to `obj/frank-cli/`
- `frank-cli --help` lists all five subcommands with their options
- `dotnet pack` produces a valid `.nupkg`; `dotnet tool install --global --add-source ./nupkg frank-cli` succeeds; the installed tool responds to `frank-cli --help`
- An end-to-end integration test runs the full pipeline (extract → clarify → validate → diff → compile) without error

## Context & Constraints

- These commands complete the CLI workflow: `extract` (WP05) → `clarify` (WP05) → `validate` → `diff` → `compile`
- `CompileCommand` generates the final OWL/XML and SHACL artifacts consumed by `Frank.LinkedData` at runtime (WP07)
- `Program.fs` uses `System.CommandLine` (same version added in WP01/WP02) to compose all five subcommands into one root command
- Output schemas for all three new commands are normative — see `contracts/cli-commands.md`
- The `DiffEngine` used by `DiffCommand` was implemented in WP02; import it from `Frank.Cli.Core.State.DiffEngine`
- The JSON and text output helpers from WP05 (`JsonOutput`, `TextOutput`) must be reused here — do not duplicate serialisation logic

## Subtasks & Detailed Guidance

### T029: ValidateCommand.fs

Module: `Frank.Cli.Core.Commands.ValidateCommand`

Steps:
1. Load `ExtractionState` from `obj/frank-cli/extraction-state.json`
2. Compute coverage metrics:
   - `mappedTypes` / `unmappedTypes` counts
   - `mappedRoutes` / `unmappedRoutes` counts
   - `coveragePercent` — `mappedTypes / (mappedTypes + unmappedTypes) * 100`, rounded to one decimal place
3. Consistency checks (each failure is a separate entry in a `issues` array):
   - Every object property's `domain` and `range` must reference a class that exists in the ontology graph; report missing class name
   - Every resource capability must reference a route URI that exists in the route map; report capability name and missing route
   - No orphaned blank nodes (nodes with no incoming or outgoing named-property edges)
4. Vocabulary alignment metrics:
   - Count ontology concepts that have a `owl:equivalentClass` or `owl:equivalentProperty` to schema.org or hydra
   - Report `alignedCount` and `unalignedCount`
5. Staleness check:
   - For each source file recorded in `ExtractionState.sourceFiles`, compute the current SHA-256 hash and compare to the stored hash
   - If any file has changed since extraction, add a `stale-source` warning to the issues array

Return type: `ValidateResult` per `contracts/cli-commands.md` validate schema.

### T030: DiffCommand.fs

Module: `Frank.Cli.Core.Commands.DiffCommand`

Accepted parameters:
- `projectPath` — used to locate `obj/frank-cli/extraction-state.json` (the current state)
- `--previous` — optional explicit path to an older state file; if omitted, auto-detect the most recently dated backup in `obj/frank-cli/backups/`

Steps:
1. Load current `ExtractionState` from `obj/frank-cli/extraction-state.json`
2. Load previous `ExtractionState` from `--previous` path or auto-detected backup
3. Call `DiffEngine.compute previousState currentState` (from WP02) to obtain a `StateDiff`
4. Format as `DiffResult` per contract schema:
   - `added` — list of added entries (each with `kind`, `name`, `details`)
   - `removed` — list of removed entries
   - `modified` — list of modified entries with `before` and `after` sub-records
   - `summary` — record with `addedCount`, `removedCount`, `modifiedCount`

If no previous state can be found (no backup and no `--previous` flag), emit an informational message and return an empty diff result rather than an error.

### T031: CompileCommand.fs

Module: `Frank.Cli.Core.Commands.CompileCommand`

Steps:
1. Load `ExtractionState`; if the state file is missing, emit `"No extraction state found. Run 'frank-cli extract' first."` and return error
2. Back up the existing state file (if any) to `obj/frank-cli/backups/extraction-state-<ISO8601timestamp>.json` before overwriting
3. Generate `ontology.owl.xml`:
   - Construct a `dotNetRdf` `IGraph` from the ontology recorded in `ExtractionState`
   - Write using `PrettyRdfXmlWriter` to `obj/frank-cli/ontology.owl.xml`
4. Generate `shapes.shacl.ttl`:
   - Construct an `IGraph` from the SHACL shapes recorded in `ExtractionState`
   - Write using `CompressingTurtleWriter` to `obj/frank-cli/shapes.shacl.ttl`
5. Generate `manifest.json`:
   - Fields: `version` (semantic version string from `Directory.Build.props` VersionPrefix), `baseUri`, `sourceHash` (SHA-256 of all source file hashes concatenated), `vocabularies` (list of vocabulary URIs used), `generatedAt` (ISO 8601 UTC timestamp)
   - Write to `obj/frank-cli/manifest.json`
6. Verify all three files are parseable after writing (round-trip check): re-parse each file and confirm no exception is thrown

Return type: `CompileResult` per contract schema:
- `ontologyPath` — absolute path to `ontology.owl.xml`
- `shapesPath` — absolute path to `shapes.shacl.ttl`
- `manifestPath` — absolute path to `manifest.json`
- `embeddedResourceNames` — the three names that `Frank.LinkedData` will look for when loading these as embedded resources (e.g., `"Frank.Semantic.ontology.owl.xml"`)

### T032: Program.fs (CLI entry point)

Module: `Frank.Cli` (console application, not `Frank.Cli.Core`)

Structure:
```fsharp
open System.CommandLine

[<EntryPoint>]
let main argv =
    let rootCommand = RootCommand("Frank semantic definition tool")
    // add subcommands
    rootCommand.Invoke(argv)
```

Subcommands to wire:
- `extract` — options: `--project` (required, FileInfo), `--base-uri` (required, Uri), `--vocabularies` (optional, string list), `--scope` (optional, enum: project/file/resource), `--file` (optional, FileInfo), `--resource` (optional, string)
- `clarify` — options: `--project` (required, FileInfo)
- `validate` — options: `--project` (required, FileInfo)
- `diff` — options: `--project` (required, FileInfo), `--previous` (optional, FileInfo)
- `compile` — options: `--project` (required, FileInfo)

Common option shared by all subcommands: `--text` (flag, bool, default false) — switch output from JSON to human-readable text.

Each subcommand handler:
1. Invokes the corresponding `Command` module function
2. Selects `JsonOutput` or `TextOutput` based on `--text` flag
3. Writes the formatted output to `Console.Out`
4. Returns exit code `0` on success, `1` on any error

### T033: Dotnet tool packaging

In `Frank.Cli.fsproj`:
- Set `<PackAsTool>true</PackAsTool>`
- Set `<ToolCommandName>frank-cli</ToolCommandName>`
- Set `<PackageId>Frank.Cli</PackageId>`
- Set `<Description>Frank semantic definition CLI tool</Description>`
- Set `<Authors>` and `<PackageLicenseExpression>` consistent with other Frank packages (check `src/Directory.Build.props`)
- Do not hardcode `<Version>`; it must derive from `VersionPrefix` in `Directory.Build.props`

Validation steps (document in a comment at the top of the task, not in code):
1. `dotnet pack src/Frank.Cli/Frank.Cli.fsproj -o ./nupkg`
2. `dotnet tool install --global --add-source ./nupkg frank-cli`
3. `frank-cli --help` — must print root command description and list all five subcommands
4. `frank-cli extract --help` — must list all extract-specific options
5. `dotnet tool uninstall --global frank-cli`

### T034: Tests

Location: `test/Frank.Cli.Core.Tests/` (unit tests) and a new `test/Frank.Cli.IntegrationTests/` project (integration).

**ValidateCommand unit tests**:
- Construct a synthetic `ExtractionState` with a known gap (one property referencing a non-existent class)
- Verify that `validate` returns an issue entry describing the missing class
- Verify `coveragePercent` calculation is correct given a fixed set of mapped/unmapped counts
- Verify staleness detection: mutate a source file hash in the state and confirm a `stale-source` warning appears

**DiffCommand unit tests**:
- Construct two `ExtractionState` values differing by one added class and one removed property
- Verify `DiffResult.summary.addedCount == 1` and `DiffResult.summary.removedCount == 1`
- Verify no-previous-state case returns an empty diff without error

**CompileCommand unit tests**:
- Provide a valid minimal `ExtractionState`
- Verify `ontology.owl.xml` is written and can be re-parsed by `RdfXmlParser` without exception
- Verify `shapes.shacl.ttl` is written and can be re-parsed by `TurtleParser` without exception
- Verify `manifest.json` is valid JSON with all required fields

**Integration test**:
- Use a known-good sample project (e.g., the Frank.LinkedData.Sample from WP01)
- Run the full pipeline: `extract` → `validate` → `compile`
- Verify exit code 0 at each step
- Verify the three compiled artifact files exist and are non-empty

## Risks & Mitigations

| Risk | Mitigation |
|---|---|
| `dotNetRdf` `PrettyRdfXmlWriter` may produce non-deterministic output ordering | Run the round-trip parse check (T031 step 6) to catch malformed output; ordering differences are acceptable as long as the graph is semantically equivalent |
| `System.CommandLine` API surface changes between preview versions | Pin to the specific version added in WP01; do not upgrade without updating all call sites |
| Integration test requires a buildable sample project from WP01 | Gate the integration test on WP01 completion; mark the integration test as skipped if the sample assembly is not found |
| Tool install may conflict with a globally installed `frank-cli` from a prior run | Document that CI should run `dotnet tool uninstall --global frank-cli || true` before the packaging validation step |

## Review Guidance

- Run `dotnet test test/Frank.Cli.Core.Tests/` and `dotnet test test/Frank.Cli.IntegrationTests/` — all tests must pass
- Manually run the full pipeline against a real Frank project and inspect each command's JSON output against `contracts/cli-commands.md`
- Verify `frank-cli --help` and each subcommand's `--help` output is correct
- Confirm `dotnet pack` succeeds and the `.nupkg` contains the tool entry point
- Confirm zero behavioral change to existing Frank tests (`dotnet test Frank.sln`)

## Activity Log

| Timestamp | Lane | Agent | Action |
|---|---|---|---|
| 2026-03-04T22:10:13Z | planned | system | Prompt generated via /spec-kitty.tasks |
- 2026-03-05T23:39:06Z – claude-opus – shell_pid=10839 – lane=doing – Assigned agent via workflow command
