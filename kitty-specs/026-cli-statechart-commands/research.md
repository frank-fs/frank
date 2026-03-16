# Research: CLI Statechart Commands (Spec 026)

**Date**: 2026-03-16
**Status**: Complete
**Researcher**: Claude (spec-kitty.research)

## Executive Summary

This research investigates six areas critical to implementing the CLI statechart commands: (1) the existing frank-cli architecture, (2) host-based assembly loading for metadata extraction, (3) how `StateMachineMetadata` is populated and what data it contains, (4) the existing format generators/parsers and their public APIs, (5) the validation pipeline from spec 021, and (6) XState JSON serialization. The primary architectural risk is assembly loading and host isolation. All other areas are well-supported by existing infrastructure.

---

## Research Area 1: Existing frank-cli Architecture

### Source Files
- `src/Frank.Cli/Program.fs` -- CLI entrypoint, System.CommandLine wiring
- `src/Frank.Cli.Core/Commands/` -- Command modules (ExtractCommand, ValidateCommand, etc.)
- `src/Frank.Cli.Core/Output/JsonOutput.fs` -- JSON output formatting
- `src/Frank.Cli.Core/Output/TextOutput.fs` -- Text output formatting with color support
- `src/Frank.Cli.Core/Help/HelpContent.fs` -- Help metadata registry

### Findings

**Command Registration Pattern**: Program.fs uses System.CommandLine 2.0.3 directly (not the older `System.CommandLine.DragonFruit`). Each command is created imperatively:

```fsharp
let extractCmd = Command("extract")
extractCmd.Description <- "..."
let projectOpt = Option<string>("--project")
projectOpt.Required <- true
extractCmd.Options.Add(projectOpt)
extractCmd.SetAction(fun parseResult -> ...)
root.Subcommands.Add(extractCmd)
```

There is no abstraction layer or command factory. Commands are registered one at a time on the root command. The `statechart` parent command would follow this same pattern: create a `Command("statechart")`, add four subcommands (`extract`, `generate`, `validate`, `import`), and add the parent to the root.

**Note on naming collision**: The existing frank-cli already has `extract` and `validate` commands (for the RDF/OWL semantic extraction pipeline). The statechart commands with the same names (`extract`, `validate`) go under the `statechart` parent command, so there is no conflict: `frank-cli extract` (RDF) vs `frank-cli statechart extract` (statecharts).

**Output Pattern**: All commands follow a consistent pattern:
1. Execute command logic, returning `Result<T, string>`
2. Match on Ok/Error
3. Format output based on `--format` flag (`text` or `json`)
4. Write to stdout (Ok) or stderr (Error) with appropriate exit code

**JsonOutput conventions**: Uses `Utf8JsonWriter` with `JsonWriterOptions(Indented = true)`. Follows camelCase naming via manual `writer.WriteString("fieldName", value)` calls (not relying on `JsonNamingPolicy`). Status field is always first (`"status": "ok"` or `"status": "error"`).

**TextOutput conventions**: Uses `System.Text.StringBuilder`. Color via `isColorEnabled()` which checks `NO_COLOR` env var and `Console.IsOutputRedirected`. Helper functions: `bold`, `yellow`, `red`, `green`.

**Help System**: `HelpContent.fs` has hardcoded `CommandHelp` records with `Name`, `Summary`, `Examples`, `Workflow`, and `Context` fields. The `allCommands` list is the source of truth. New statechart commands need help entries.

**Project Structure**: `Frank.Cli` (exe, net10.0) references `Frank.Cli.Core` (library, net10.0). `Frank.Cli.Core` has the heavy dependencies (dotNetRdf, FSharp.Compiler.Service, etc.). The CLI exe only has `System.CommandLine 2.0.3` and the core project reference.

**Decision D-001**: Follow the existing imperative command registration pattern in Program.fs. Add a `statechart` parent Command with four subcommands. No abstraction layer needed -- consistency with existing code is more valuable than DRY here.

---

## Research Area 2: Host-Based Assembly Loading for Metadata Extraction

### The Challenge

`StateMachineMetadata` is populated at endpoint registration time, inside `StatefulResourceBuilder.Run()`. It is stored as endpoint metadata via `builder.Metadata.Add(metadata)`. To extract it from a compiled assembly, we need to:

1. Load the assembly
2. Build a WebApplication host that triggers endpoint registration
3. Collect `StateMachineMetadata` from the registered endpoints
4. Shut down the host

This is the pattern used by ASP.NET's own `dotnet-getdocument` tool for OpenAPI document generation.

### How Endpoints Are Registered in Frank

In Frank, resources are built as `Resource = { Endpoints: Endpoint[] }` structs. These are collected by the `WebHostBuilder` CE into a `WebHostSpec.Endpoints` array. At runtime, `WebHostBuilder.Run()` creates a `ResourceEndpointDataSource(spec.Endpoints)` and adds it to the `IEndpointRouteBuilder.DataSources`.

The key insight: `StatefulResourceBuilder.Run()` is called during CE evaluation (not at runtime). It creates the endpoints immediately, adding `StateMachineMetadata` to each endpoint's metadata. So by the time `WebHostSpec.Endpoints` is assembled, the metadata is already present on the `Endpoint` objects.

### Assembly Loading Approach

**Option A: Build a minimal WebApplication from the target assembly (selected)**

Load the target assembly, find its entry point or a known type, invoke endpoint registration to collect `Resource` values, then inspect their `Endpoint.Metadata` for `StateMachineMetadata` instances.

The challenge is that Frank applications define their endpoints in `Program.fs` using the `webHost` CE. We cannot easily "replay" this CE. Instead, we need a different approach.

**Option B: Reflection-based endpoint collection (selected, refined)**

Rather than building a full WebApplication, we can:

1. Load the target assembly into an isolated `AssemblyLoadContext`
2. Search for all `Resource` instances or `StatefulResourceBuilder` results
3. Inspect the `Endpoint.Metadata` directly

However, `Resource` instances are created at CE evaluation time. They are typically `let` bindings at module level or inside the `webHost` CE. Without invoking the application code, we cannot access them.

**Option C: Use dotnet-getdocument pattern -- invoke the application's entry point**

The `dotnet-getdocument` tool works by:
1. Loading the target assembly
2. Finding the entry point (`Main` method)
3. Calling it with modified service configuration (intercepting `IServiceCollection` to inject hooks)
4. Collecting the data before the host starts listening

This is the most reliable approach because it uses the application's own code to register endpoints. The pattern in ASP.NET:

```
// Pseudo-code for the dotnet-getdocument approach:
// 1. Set env vars to signal "metadata extraction mode"
// 2. Call the application's entry point
// 3. Intercept WebApplication.Build() via IHostedService or similar
// 4. Read endpoint metadata
// 5. Exit before Run() blocks
```

**Risk Assessment**:
- The `dotnet-getdocument` approach requires calling the application's `Main`. This means all startup code runs, including database connections, external service setup, etc. For CLI metadata extraction, this is undesirable.
- Frank applications use the `webHost` CE which calls `app.Run()` at the end, blocking the process. We'd need to intercept before `Run()`.

**Option D: Reflection on module-level `Resource` values (pragmatic, selected)**

Frank `statefulResource` CEs produce `Resource` values (containing `Endpoint[]`). In typical Frank applications, these are module-level `let` bindings. We can:

1. Load the assembly into an isolated `AssemblyLoadContext`
2. Scan all types for fields/properties of type `Frank.Builder.Resource`
3. Read the `Endpoint[]` from each `Resource`
4. Extract `StateMachineMetadata` from `Endpoint.Metadata`

This avoids calling `Main` and avoids building a WebApplication. The trade-off is that it only works for module-level `Resource` bindings, not for resources constructed dynamically.

**However**, there's a critical problem: `Resource` values that are defined inside the `webHost` CE's `resource` operation are not accessible as module-level values. They're typically ephemeral values passed to the CE builder. The typical pattern is:

```fsharp
webHost [||] {
    resource (statefulResource "/games/{id}" { ... })
    // The statefulResource result is immediately consumed by the CE
}
```

So Option D doesn't work for the common case.

**Decision D-002**: Use a hybrid approach. The `StatechartExtractor` module should:

1. Load the target assembly in an isolated `AssemblyLoadContext`
2. Find all types that are `Resource` (check static fields on module types)
3. For each `Resource`, iterate its `Endpoints` and look for `StateMachineMetadata` in metadata
4. If no resources are found via reflection, fall back to invoking the entry point with intercepted services

For the initial implementation, option D (reflection on module-level `Resource` bindings) is the simplest path. The entry-point-invocation fallback can be added later if needed.

**Critical issue**: `ResourceEndpointDataSource` is `internal`. We cannot directly use it from the CLI. However, `Endpoint` objects and their `Metadata` property are public ASP.NET types. Once we have the `Endpoint[]` array, we can read metadata normally.

**Alternative pragmatic approach**: Since `StatefulResourceBuilder.Run()` returns a `Resource` which is a struct containing `Endpoint[]`, and the CE evaluation happens eagerly when the module is loaded, we can force module initialization by accessing any type in the module. F# module `let` bindings are lazy-initialized on first access to the module.

**Decision D-003**: The assembly loading approach needs further prototyping during implementation. The research identifies three viable strategies (entry point invocation, reflection on module-level bindings, forced module initialization). The implementer should try forced module initialization first (simplest), fall back to entry point invocation if needed.

### AssemblyLoadContext Isolation

`AssemblyLoadContext` provides the isolation mechanism to prevent the loaded application's dependencies from conflicting with frank-cli's own dependencies. Key considerations:

- Create a custom `AssemblyLoadContext` with `isCollectible: true` for cleanup
- Use `AssemblyDependencyResolver` to resolve the target assembly's dependency chain from its `.deps.json`
- The loaded assembly's `Frank.Statecharts.StateMachineMetadata` type must be the SAME type as frank-cli sees. This means frank-cli must reference `Frank.Statecharts` (or share the assembly). If the types don't match, the `Metadata.GetMetadata<StateMachineMetadata>()` call will return null.

**Critical dependency observation**: `Frank.Cli.Core` does NOT currently reference `Frank.Statecharts`. For the CLI to read `StateMachineMetadata` from loaded assemblies, one of these must be true:
- `Frank.Cli.Core` adds a project reference to `Frank.Statecharts`
- The CLI uses `Endpoint.Metadata` with untyped access (cast by type name, not by type identity)

**Decision D-004**: `Frank.Cli.Core` (or a new `Frank.Cli.Statechart` project) must add a reference to `Frank.Statecharts` for type-safe metadata access. The target assembly loaded via `AssemblyLoadContext` should share the same `Frank.Statecharts` assembly (loaded from the CLI's own context) to ensure type identity match.

---

## Research Area 3: StateMachineMetadata Population and Available Data

### Source File
- `src/Frank.Statecharts/StatefulResourceBuilder.fs` (lines 175-339)

### How StateMachineMetadata is Populated

In `StatefulResourceBuilder.Run()` (line 175), the builder:

1. Extracts the `StateMachine<'S,'E,'C>` from the CE spec
2. Boxes it as `Machine: obj`
3. Builds the `StateHandlerMap: Map<string, (string * RequestDelegate) list>` from the CE's `inState` operations (state key -> list of HTTP method + handler pairs)
4. Computes `InitialStateKey: string` via `StateKeyExtractor.keyOf machine.Initial`
5. Pre-computes `GuardNames: string list` from `machine.Guards` (mapping each to its name)
6. Pre-computes `StateMetadataMap: Map<string, StateInfo>` from `machine.StateMetadata` (converting typed state keys to strings)
7. Creates closure functions for runtime behavior (GetCurrentStateKey, EvaluateGuards, etc.)

The metadata is added to EACH endpoint in the resource via `builder.Metadata.Add(metadata)` (line 336).

### Available Data for CLI Extraction

From `StateMachineMetadata`, the CLI can extract:

| Field | CLI Use | Notes |
|---|---|---|
| `StateHandlerMap` | State names (keys), HTTP methods per state | Primary source of state-capability data |
| `InitialStateKey` | Initial state | String key |
| `GuardNames` | Guard names for display | Pre-computed string list |
| `StateMetadataMap` | IsFinal, Description, AllowedMethods per state | `Map<string, StateInfo>` |
| `Machine` | Boxed `StateMachine<_,_,_>` | Not directly useful (generic type erased) |
| `ResolveInstanceId` | Not needed for CLI | Runtime function |
| `GetCurrentStateKey` | Not needed for CLI | Runtime function (needs IServiceProvider) |
| `EvaluateGuards`/`ExecuteTransition` | Not needed | Runtime functions |

The route template is NOT stored in `StateMachineMetadata`. It must be obtained from the `Endpoint` itself. Each endpoint in the `Resource` is a `RouteEndpoint` whose `RoutePattern` property contains the template.

**Decision D-005**: The `ExtractedStatechart` record should include:
- Route template (from `RouteEndpoint.RoutePattern.RawText`)
- State names (from `StateHandlerMap` keys)
- Initial state key
- Guard names
- Per-state metadata (from `StateMetadataMap`)
- The raw `StateMachineMetadata` reference for downstream generator use

### Important Limitation

The spec notes (assumption): "Transition targets between different states are NOT directly available from `StateMachineMetadata`." `StateHandlerMap` maps state -> HTTP methods, not state -> state transitions. The generated artifacts represent state-capability views (what methods are available per state), not full transition graphs. This is correct and matches the WSD Generator and smcat Generator behavior, which produce self-transitions for each (state, method) pair.

---

## Research Area 4: Format Generators/Parsers and Their Public APIs

### Visibility Problem

**All format-specific modules are `internal`**:
- `module internal Frank.Statecharts.Wsd.Generator`
- `module internal Frank.Statecharts.Wsd.Serializer`
- `module internal Frank.Statecharts.Wsd.Parser`
- `module internal Frank.Statecharts.Alps.JsonParser`
- `module internal Frank.Statecharts.Alps.JsonGenerator`
- `module internal Frank.Statecharts.Alps.Mapper`
- `module internal Frank.Statecharts.Scxml.Parser`
- `module internal Frank.Statecharts.Scxml.Mapper`
- `module internal Frank.Statecharts.Scxml.Generator`
- `module internal Frank.Statecharts.Smcat.Parser`
- `module internal Frank.Statecharts.Smcat.Mapper`
- `module internal Frank.Statecharts.Smcat.Generator`
- `module internal Frank.Statecharts.Smcat.Lexer`
- All `Types.fs` modules in each format subdirectory

The only `InternalsVisibleTo` is `Frank.Statecharts.Tests`.

**Decision D-006**: Either (a) add `InternalsVisibleTo` for the CLI core project, (b) change generators/parsers from `internal` to public, or (c) add public facade functions. Option (b) is cleanest -- these modules are the public API of `Frank.Statecharts` for tooling consumers. Making them public is an intentional API expansion, not an accidental exposure.

### Generator API Summary

**Two patterns exist**:

1. **Generators that take `StateMachineMetadata` directly** (metadata -> text):
   - `Wsd.Generator.generate: GenerateOptions -> StateMachineMetadata -> Result<StatechartDocument, GeneratorError>` (produces AST, not text)
   - `Smcat.Generator.generate: GenerateOptions -> StateMachineMetadata -> string` (produces text directly)

2. **Generators that take format-specific AST types** (AST -> text):
   - `Wsd.Serializer.serialize: StatechartDocument -> string` (shared AST -> WSD text)
   - `Alps.JsonGenerator.generateAlpsJson: AlpsDocument -> string` (ALPS AST -> JSON text)
   - `Scxml.Generator.generate: ScxmlDocument -> string` (SCXML AST -> XML text)

### Generation Pipeline for Each Format

For the CLI `generate` command, the pipeline from `StateMachineMetadata` to text is:

| Format | Pipeline |
|---|---|
| **WSD** | `StateMachineMetadata` -> `Wsd.Generator.generate` -> `StatechartDocument` -> `Wsd.Serializer.serialize` -> WSD text |
| **ALPS** | `StateMachineMetadata` -> `Wsd.Generator.generate` -> `StatechartDocument` -> `Alps.Mapper.fromStatechartDocument` -> `AlpsDocument` -> `Alps.JsonGenerator.generateAlpsJson` -> ALPS JSON |
| **SCXML** | `StateMachineMetadata` -> `Wsd.Generator.generate` -> `StatechartDocument` -> `Scxml.Mapper.fromStatechartDocument` -> `ScxmlDocument` -> `Scxml.Generator.generate` -> SCXML XML |
| **smcat** | `StateMachineMetadata` -> `Smcat.Generator.generate` -> smcat text (direct, no AST intermediate) |
| **XState** | `StateMachineMetadata` -> `Wsd.Generator.generate` -> `StatechartDocument` -> XState JSON serializer (new, spec 026) |

Key observation: The WSD Generator is the central "metadata -> shared AST" converter. ALPS, SCXML, and XState generation all go through the shared AST. Only smcat has a direct metadata-to-text path.

### Parser API Summary

| Format | Entry Point | Input | Output |
|---|---|---|---|
| **WSD** | `Wsd.Parser.parseWsd` | `string` | `Ast.ParseResult` (shared AST directly) |
| **ALPS** | `Alps.JsonParser.parseAlpsJson` | `string` | `Result<AlpsDocument, AlpsParseError list>` |
| **SCXML** | `Scxml.Parser.parseString` | `string` | `ScxmlParseResult` (SCXML-specific) |
| **smcat** | `Smcat.Parser.parseSmcat` | `string` | `Smcat.Types.ParseResult` (smcat-specific) |

For parsers that return format-specific types, mappers convert to the shared AST:
- ALPS: `Alps.Mapper.toStatechartDocument: AlpsDocument -> StatechartDocument`
- SCXML: `Scxml.Mapper.toStatechartDocument: ScxmlParseResult -> Ast.ParseResult`
- smcat: `Smcat.Mapper.toStatechartDocument: Smcat.Types.ParseResult -> Ast.ParseResult`

The WSD parser already returns `Ast.ParseResult` (shared AST) directly -- no mapper needed.

---

## Research Area 5: Validation Pipeline (Spec 021)

### Source Files
- `src/Frank.Statecharts/Validation/Types.fs`
- `src/Frank.Statecharts/Validation/Validator.fs`

### Key Types

```fsharp
type FormatTag = Wsd | Alps | Scxml | Smcat | XState
type FormatArtifact = { Format: FormatTag; Document: StatechartDocument }
type ValidationReport = {
    TotalChecks: int; TotalSkipped: int; TotalFailures: int
    Checks: ValidationCheck list; Failures: ValidationFailure list }
```

### Validator API

```fsharp
Validator.validate: ValidationRule list -> FormatArtifact list -> ValidationReport
```

The validator takes a list of rules and a list of artifacts. Rules declare their `RequiredFormats`; the validator skips rules whose required formats aren't all present in the artifact list. This is exactly what the CLI `validate` command needs.

### Available Rules

Two rule modules provide pre-built rule lists:
- `SelfConsistencyRules.rules: ValidationRule list` -- 5 rules (orphan targets, duplicates, required fields, isolated states, empty statecharts)
- `CrossFormatRules.rules: ValidationRule list` -- All pairwise combinations of (state name, event name, transition target) agreement across all 5 format tags

The CLI should use `SelfConsistencyRules.rules @ CrossFormatRules.rules` as the full rule set.

### CLI Validate Command Flow

Per FR-021, the validate command must:
1. Parse each spec file into a `FormatArtifact` (using the appropriate parser + mapper)
2. Extract `StateMachineMetadata` from the assembly
3. Generate a `StatechartDocument` from the metadata (using `Wsd.Generator.generate`)
4. Wrap it as a `FormatArtifact` with `Format = Wsd` (or a new tag?) to serve as the "code truth"
5. Pass all artifacts to `Validator.validate` with the full rule set
6. Format and display the `ValidationReport`

**Decision D-007**: The code-derived artifact should use `FormatTag.Wsd` since it's generated by the WSD Generator. The cross-format rules will then check spec files against this code-derived WSD artifact, detecting state/event/target discrepancies.

### Visibility

`Validation.Types` and `Validation.Validator` are in the `Frank.Statecharts.Validation` namespace and are NOT marked `internal`. They are public. This is good -- the CLI can use them directly.

---

## Research Area 6: XState JSON Serialization/Deserialization

### Current State

There is NO existing XState serializer or deserializer in `Frank.Statecharts`. The only XState-related code is the `XStateMeta` DU in `Ast/Types.fs`:

```fsharp
type XStateMeta =
    | XStateAction of string
    | XStateService of string

type Annotation =
    | ...
    | XStateAnnotation of XStateMeta
```

And `FormatTag.XState` exists in the validation types.

### What Needs to Be Built

**Serializer** (`StatechartDocument` -> XState v5 JSON):
- The serializer must produce valid XState v5 machine configuration JSON
- Map `StateNode` elements to XState state objects
- Map `TransitionEdge` elements to XState event/transition definitions
- Map `InitialStateId` to XState `initial` property
- Map guard names to XState guard references
- Handle `StateKind.Final` -> XState `type: "final"`
- Handle nested states (children) -> XState compound states

**Deserializer** (`XState v5 JSON` -> `StatechartDocument`):
- Parse XState machine JSON using `System.Text.Json`
- Map XState states to `StateNode` with appropriate `StateKind`
- Map XState transitions to `TransitionEdge` entries
- Extract initial state, guards, actions, services
- Produce a `ParseResult` (with best-effort document + errors/warnings)

### XState v5 JSON Schema (Key Structure)

```json
{
  "id": "machineName",
  "initial": "stateName",
  "states": {
    "stateName": {
      "on": {
        "EVENT_NAME": {
          "target": "targetState",
          "guard": "guardName"
        }
      },
      "type": "final"
    }
  }
}
```

### Complexity Assessment

The XState serializer/deserializer is the only new format-specific code in this feature. Estimated scope:
- **Serializer**: Straightforward mapping from `StatechartDocument` fields to XState JSON structure. ~100-150 lines.
- **Deserializer**: Moderate complexity due to XState's flexible JSON schema (events can be strings, objects, or arrays). ~150-200 lines.
- **Total**: ~250-350 lines of new format code, plus tests.

**Decision D-008**: Implement XState serializer/deserializer as a new `XState/` subdirectory within `Frank.Statecharts`, following the pattern of the existing format modules (Types.fs for format-specific types if needed, Serializer.fs, Mapper.fs). Since the shared AST already has `XStateMeta` annotation types, and the validation system already has `FormatTag.XState`, the integration points are ready.

---

## Cross-Cutting Concerns

### Project References Needed

The new statechart CLI commands require `Frank.Cli.Core` (or `Frank.Cli`) to reference `Frank.Statecharts`. Currently there is no such reference. Options:

1. **Add `Frank.Statecharts` reference to `Frank.Cli.Core`** -- simplest, but adds significant dependencies (ASP.NET Core framework reference) to the CLI core library
2. **Create a new `Frank.Cli.Statechart` project** -- isolates the statechart CLI dependencies, referenced by `Frank.Cli`
3. **Add statechart command modules directly to `Frank.Cli.Core`** with the reference

**Decision D-009**: Option 1 (add reference to `Frank.Cli.Core`) is simplest. `Frank.Cli.Core` already targets net10.0 only (matching `Frank.Statecharts`' multi-target range), and adding `Frank.Statecharts` just pulls in the statechart types that are needed for metadata extraction and format generation.

However, `Frank.Statecharts` has a `FrameworkReference` to `Microsoft.AspNetCore.App`. This means `Frank.Cli.Core` would also need this framework reference, which it doesn't currently have. This is a consideration but likely acceptable since the CLI tool already targets net10.0 and ASP.NET Core types are needed for the assembly loading approach anyway.

### File Extension -> Format Mapping

Per FR-020 and FR-027:
- `.wsd` -> WSD
- `.alps.json` -> ALPS
- `.scxml` -> SCXML
- `.smcat` -> smcat
- `.xstate.json` -> XState JSON
- `.json` -> Ambiguous (needs `--format` flag or try-both-parsers)

The multi-extension detection (`.alps.json`, `.xstate.json`) requires checking suffixes, not just the final extension. `Path.GetExtension()` returns `.json` for `game.alps.json`. Need to check for compound extensions explicitly.

### Naming Convention for Output Files

Per the spec's edge case section: `{resourceSlug}.{format-extension}`. The resource slug derives from the route template (e.g., `/games/{id}` -> `games`, `/orders/{id}` -> `orders`). Format extensions: `.wsd`, `.alps.json`, `.scxml`, `.smcat`, `.xstate.json`.

---

## Risk Assessment

| Risk | Severity | Mitigation |
|---|---|---|
| Assembly loading type identity mismatch (loaded `StateMachineMetadata` type != CLI's type) | High | Ensure shared assembly reference; test with real assemblies early |
| Internal module visibility prevents CLI access to generators/parsers | High | Change modules to public or add `InternalsVisibleTo` |
| Target assembly's startup code has side effects (DB connections, etc.) | Medium | Use reflection-based extraction first; entry-point invocation as fallback |
| XState v5 schema complexity (nested states, parallel, invoke) | Low | Implement basic flat-state mapping first; extend for compound states later |
| Frank.Cli.Core adding ASP.NET Core framework reference | Low | Already targets net10.0; ASP.NET Core types needed for assembly loading anyway |

---

## Evidence Register

| ID | Source | Location | Relevance |
|---|---|---|---|
| E-001 | Program.fs command registration pattern | `src/Frank.Cli/Program.fs` | CLI architecture pattern for new commands |
| E-002 | StateMachineMetadata type definition | `src/Frank.Statecharts/StatefulResourceBuilder.fs:59-85` | Data available for extraction |
| E-003 | StatefulResourceBuilder.Run() metadata population | `src/Frank.Statecharts/StatefulResourceBuilder.fs:175-339` | How metadata gets onto endpoints |
| E-004 | Resource type (Endpoint[] struct) | `src/Frank/Builder.fs:16` | How endpoints are packaged |
| E-005 | All generators/parsers are `internal` | Grep across `src/Frank.Statecharts/` | Visibility barrier for CLI |
| E-006 | InternalsVisibleTo only for Tests | `src/Frank.Statecharts/Frank.Statecharts.fsproj:10` | Need to expand or make public |
| E-007 | Validator.validate API | `src/Frank.Statecharts/Validation/Validator.fs:646` | Validation entry point for CLI |
| E-008 | WSD Generator as central metadata-to-AST converter | `src/Frank.Statecharts/Wsd/Generator.fs:23` | Hub for all AST-based format generation |
| E-009 | No existing XState serializer/deserializer | Grep for XState in src/ | New code needed |
| E-010 | JsonOutput/TextOutput conventions | `src/Frank.Cli.Core/Output/` | Output formatting patterns |
| E-011 | HelpContent registration pattern | `src/Frank.Cli.Core/Help/HelpContent.fs` | Help system integration |
| E-012 | ResourceEndpointDataSource is internal | `src/Frank/Builder.fs:275` | Cannot directly use for endpoint collection |
| E-013 | WebHostBuilder.Run endpoint registration | `src/Frank/Builder.fs:305-353` | How endpoints become live |
| E-014 | StateKeyExtractor is internal | `src/Frank.Statecharts/StatefulResourceBuilder.fs:17` | May need for testing |
| E-015 | Frank.Cli.Core has no Frank.Statecharts reference | `src/Frank.Cli.Core/Frank.Cli.Core.fsproj` | New dependency needed |
