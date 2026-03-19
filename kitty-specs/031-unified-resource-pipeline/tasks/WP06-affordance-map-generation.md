---
work_package_id: WP06
title: Affordance Map Generation
lane: "done"
dependencies:
- WP02
base_branch: 031-unified-resource-pipeline-WP02
base_commit: 35175b217c2cce39589a312a0e8317e273430068
created_at: '2026-03-19T03:41:36.075611+00:00'
subtasks:
- T032
- T033
- T034
- T035
- T036
- T037
phase: Phase 1 - Core Pipeline
assignee: ''
agent: "claude-opus-wp06-review"
shell_pid: "23922"
review_status: "approved"
reviewed_by: "Ryan Riley"
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-009
- FR-010
- FR-011
- FR-012
---

# Work Package Prompt: WP06 -- Affordance Map Generation

## IMPORTANT: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately (right below this notice).
- **You must address all feedback** before your work is complete. Feedback items are your implementation TODO list.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.
- **Report progress**: As you address each feedback item, update the Activity Log explaining what you changed.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes. Implementation must address every item listed below before returning for re-review.

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<script>` ``
Use language identifiers in code blocks: ````python`, ````bash`

---

## Implementation Command

Depends on WP02 and WP05:

```bash
spec-kitty implement WP06 --base WP05
```

---

## Objectives & Success Criteria

1. Create `AffordanceMapGenerator.fs` that takes a `UnifiedResource list` and produces the affordance map JSON conforming to `contracts/affordance-map-schema.json`.
2. Generate composite keys using the `"{routeTemplate}|{stateKey}"` convention with `*` for stateless resources.
3. Populate `AffordanceLinkRelation` entries using IANA-precedence relation types from WP05.
4. Include `profileUrl` per entry derived from `--base-uri` and resource slug.
5. Wire `--format affordance-map` in the generate command to output JSON (display) or binary (for embedding).
6. Pass tests verifying the tic-tac-toe affordance map contains entries for each state with correct HTTP methods.

**Success**: `AffordanceMapGenerator.generate` takes a `UnifiedResource list` and `baseUri: string` and returns valid affordance map JSON matching the contract schema.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` -- User Story 3 (FR-009, FR-010, FR-011, FR-012)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` -- Project Structure (`src/Frank.Cli.Core/Output/` for affordance map generation)
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` -- `AffordanceMapEntry`, `AffordanceLinkRelation`, `UnifiedExtractionState`
- **Contract Schema**: `kitty-specs/031-unified-resource-pipeline/contracts/affordance-map-schema.json` -- the normative JSON Schema for the affordance map format
- **Research**: `kitty-specs/031-unified-resource-pipeline/research.md` -- R3 (Affordance Map Key Design: composite key `"{routeTemplate}|{stateKey}"`, `|` separator, `*` wildcard)
- **WP05 Output**: `UnifiedAlpsGenerator.deriveRelationType` -- reuse the IANA-precedence relation derivation function for populating link relations in the affordance map.
- **Version stability**: The affordance map format includes a `"version": "1.0"` field per FR-012. Additive-only schema evolution.

---

## Subtasks & Detailed Guidance

### Subtask T032 -- Create `AffordanceMapGenerator.fs`

- **Purpose**: Implement the core affordance map generation function that transforms a list of unified resources into the affordance map JSON.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Unified/AffordanceMapGenerator.fs`
  2. Module declaration: `module Frank.Cli.Core.Unified.AffordanceMapGenerator`
  3. Open `System`, `System.IO`, `System.Text`, `System.Text.Json`
  4. Define the public function:
     ```fsharp
     let generate (resources: UnifiedResource list) (baseUri: string) : string
     ```
  5. For each `UnifiedResource`, produce affordance map entries:
     - **Stateful resources** (`Statechart = Some sc`): One entry per state. Group `HttpCapabilities` by `StateKey` to determine methods per state.
     - **Stateless resources** (`Statechart = None`): One entry with state key `"*"`. Collect all `HttpCapabilities` (where `StateKey = None`).
  6. JSON structure per `contracts/affordance-map-schema.json`:
     ```json
     {
       "version": "1.0",
       "baseUri": "https://example.com/alps",
       "generatedAt": "2026-03-19T02:15:00Z",
       "entries": {
         "/games/{gameId}|XTurn": {
           "allowedMethods": ["GET", "POST"],
           "linkRelations": [
             { "rel": "self", "href": "/games/{gameId}", "method": "GET" },
             { "rel": "https://example.com/alps/games#makeMove", "href": "/games/{gameId}", "method": "POST", "title": "Make a move" }
           ],
           "profileUrl": "https://example.com/alps/games"
         },
         "/games/{gameId}|Won": {
           "allowedMethods": ["GET"],
           "linkRelations": [
             { "rel": "self", "href": "/games/{gameId}", "method": "GET" }
           ],
           "profileUrl": "https://example.com/alps/games"
         },
         "/health|*": {
           "allowedMethods": ["GET"],
           "linkRelations": [
             { "rel": "self", "href": "/health", "method": "GET" }
           ],
           "profileUrl": "https://example.com/alps/health"
         }
       }
     }
     ```
  7. Use `Utf8JsonWriter` with `JsonWriterOptions(Indented = true)` for display output.

- **Files**: `src/Frank.Cli.Core/Unified/AffordanceMapGenerator.fs` (NEW, ~100-150 lines)
- **Notes**:
  - The `entries` object is a dictionary keyed by composite string, not an array. This matches the JSON Schema (`"additionalProperties": { "$ref": "#/$defs/AffordanceEntry" }`).
  - The `generatedAt` timestamp should use `DateTimeOffset.UtcNow` at generation time.
  - Keep the generator pure except for the timestamp -- accept an optional `generatedAt: DateTimeOffset option` parameter for testability.

### Subtask T033 -- Composite Key Generation

- **Purpose**: Implement the composite key format `"{routeTemplate}|{stateKey}"` per Research R3.
- **Steps**:
  1. Define a key generation function:
     ```fsharp
     let compositeKey (routeTemplate: string) (stateKey: string) : string =
         sprintf "%s|%s" routeTemplate stateKey
     ```
  2. For stateful resources, enumerate all state names from `resource.Statechart.Value.StateNames` and create a key for each.
  3. For stateless resources, use `"*"` as the state key:
     ```fsharp
     let stateKey = "*"
     compositeKey resource.RouteTemplate stateKey
     ```
  4. Validate that the `|` separator does not appear in route templates or state names. If it does, escape it or report an error. In practice, `|` is not valid in URI path segments (RFC 3986) or F# DU case names, so this is defensive.

- **Files**: `src/Frank.Cli.Core/Unified/AffordanceMapGenerator.fs` (part of T032)
- **Notes**:
  - The composite key is used both in the JSON output and at runtime for dictionary lookup. It must be deterministic -- same inputs always produce the same key.
  - Route templates should be stored exactly as defined in the CE (e.g., `/games/{gameId}`), not normalized. The middleware will look up using the same template from endpoint metadata.

### Subtask T034 -- Populate `AffordanceLinkRelation` Entries

- **Purpose**: Fill in the `linkRelations` array for each affordance map entry using the IANA-precedence relation types derived in WP05.
- **Steps**:
  1. For each `HttpCapability` in a given state:
     - Use `deriveRelationType` from WP05 (T028) to get the relation type URI
     - Construct an `AffordanceLinkRelation`:
       ```fsharp
       { Rel = capability.LinkRelation  // Already populated by WP05 derivation
         Href = resource.RouteTemplate
         Method = capability.Method
         Title = None }  // Optional: derive from method + resource slug
       ```
  2. For state-dependent transitions where the statechart provides transition target information:
     - If the transition goes from `XTurn` to `OTurn` via POST, include `"title": "Make move (transitions to OTurn)"` as context.
  3. Group link relations by state key -- each affordance entry contains only the relations available in that state.
  4. Ensure `GET self` appears in every entry (every state allows at least GET for resource retrieval).

- **Files**: `src/Frank.Cli.Core/Unified/AffordanceMapGenerator.fs`
- **Notes**:
  - The `title` field is optional per the contract schema. It provides human-readable context but is not required for machine consumption.
  - If a state has no HTTP capabilities defined (possible for intermediate states), the entry should have an empty `allowedMethods` list and no link relations. The middleware will return 405 for such states.
  - Consider sorting `allowedMethods` alphabetically for deterministic output.

### Subtask T035 -- Add `profileUrl` Per Entry

- **Purpose**: Derive and include the ALPS profile URL for each affordance map entry.
- **Steps**:
  1. The profile URL is constructed from `baseUri` and `resourceSlug`:
     ```fsharp
     let profileUrl (baseUri: string) (slug: string) : string =
         let trimmed = baseUri.TrimEnd('/')
         sprintf "%s/%s" trimmed slug
     ```
  2. For tic-tac-toe with `baseUri = "https://example.com/alps"` and `slug = "games"`:
     - `profileUrl = "https://example.com/alps/games"`
  3. Include this URL in every affordance entry for the resource. All entries for the same resource share the same profile URL (the ALPS profile describes the resource, not a specific state).
  4. This URL is what the runtime middleware will emit as `Link: <profileUrl>; rel="profile"`.

- **Files**: `src/Frank.Cli.Core/Unified/AffordanceMapGenerator.fs`
- **Notes**:
  - The `resourceSlug` must match between the affordance map and the actual served ALPS profile endpoint (WP08). Use the same slug derivation logic.
  - The slug derivation from route template should strip parameters and leading slashes: `/games/{gameId}` -> `games`, `/health` -> `health`, `/api/v1/games/{id}` -> `api-v1-games`.

### Subtask T036 -- Wire `--format affordance-map` in Generate Command

- **Purpose**: Add `affordance-map` as a supported format in the `frank-cli generate` command, outputting JSON for display or binary for embedding.
- **Steps**:
  1. In the generate command module (likely `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs` or a new unified generate command):
     - Add `"affordance-map"` to the supported format list
     - When this format is selected:
       - Read the unified extraction state from cache (or extract if needed)
       - Call `AffordanceMapGenerator.generate resources baseUri`
       - Output JSON to stdout (for `--output-format text` or `--output-format json`)
  2. For binary output (used by MSBuild embedding in WP09):
     - The `generate` command with `--output-format binary` should serialize the unified state to MessagePack binary and write to `obj/frank-cli/unified-state.bin`
     - This is the same binary format used for caching (WP03) and embedding (WP09)
  3. The affordance map JSON display format is separate from the binary embedded format. The JSON is for developer inspection; the binary is for runtime consumption.

- **Files**: `src/Frank.Cli.Core/Commands/StatechartGenerateCommand.fs` or new unified generate command (modified, ~20-40 lines added)
- **Notes**:
  - The generate command currently handles formats: `wsd`, `alps`, `alps-xml`, `scxml`, `smcat`, `xstate`. Adding `affordance-map` is an extension, not a replacement.
  - The `--base-uri` flag is required for affordance map generation (needed for profile URLs and ALPS fragment URIs). If not provided, default to `http://localhost:5000/alps` or require it as mandatory for this format.
  - Binary output should write to the standard cache path: `obj/frank-cli/unified-state.bin` under the project directory.

### Subtask T037 -- Tests: Tic-Tac-Toe Affordance Map

- **Purpose**: Verify affordance map generation produces correct entries for each state of the tic-tac-toe resource.
- **Steps**:
  1. Construct a `UnifiedResource list` fixture with the tic-tac-toe resource (reuse or extend the fixture from WP05 T031).
  2. Call `AffordanceMapGenerator.generate resources "https://example.com/alps"`.
  3. Parse the result with `JsonDocument` and assert:
     - `version` is `"1.0"`
     - `baseUri` is `"https://example.com/alps"`
     - Entry `/games/{gameId}|XTurn` exists with `allowedMethods: ["GET", "POST"]`
     - Entry `/games/{gameId}|OTurn` exists with `allowedMethods: ["GET", "POST"]`
     - Entry `/games/{gameId}|Won` exists with `allowedMethods: ["GET"]`
     - Entry `/games/{gameId}|Draw` exists with `allowedMethods: ["GET"]`
     - Each entry has `profileUrl: "https://example.com/alps/games"`
     - Link relations include `"rel": "self"` for GET entries
  4. Test stateless resource:
     - Add a `UnifiedResource` with `Statechart = None` and `HttpCapabilities = [GET]`
     - Assert entry `/health|*` exists with `allowedMethods: ["GET"]`
  5. Validate the generated JSON against the contract schema (optional but recommended -- use `System.Text.Json` to check required fields match).

- **Files**: Test file in the unified pipeline test project
- **Notes**:
  - Test determinism: the same input should produce the same output (modulo `generatedAt` timestamp). Use the optional timestamp parameter from T032.
  - Test edge case: a resource with zero HTTP capabilities should produce an entry with empty `allowedMethods` and empty `linkRelations`.
  - Test edge case: multiple resources in the list should produce entries for all of them, not just the first.

---

## Test Strategy

- **Unit tests**: Test `compositeKey`, `profileUrl`, and individual entry generation functions in isolation.
- **Integration tests**: Generate full affordance map from fixture resources, validate against contract schema.
- **Schema validation**: Optionally validate generated JSON against `contracts/affordance-map-schema.json` programmatically.

Run tests:
```bash
dotnet test test/Frank.Cli.Core.Tests/ --filter "AffordanceMap"
```

---

## Risks & Mitigations

| Risk | Mitigation |
|------|------------|
| Route template format differences between CLI and runtime | Use exact route templates from the CE -- do not normalize. The middleware matches using the same template from endpoint metadata. |
| Missing state names for stateful resources | The extractor (WP02) provides `StateNames` from the state DU. If the state DU has cases not covered by `inState`, those states still get entries (with empty methods from `DerivedFields.OrphanStates`). |
| Binary format not finalized in WP03 | T036's binary output depends on the serialization format from WP03. If WP03 is not complete, implement JSON-only output first and add binary output when the format is available. |
| Large projects with many resources producing large JSON | The affordance map is per-project, not per-resource. For 50 resources with 5 states each, that's ~250 entries -- a few KB of JSON. Not a concern. |

---

## Review Guidance

- Verify generated JSON matches `contracts/affordance-map-schema.json` structure exactly.
- Verify composite keys use `|` separator and `*` for stateless resources.
- Verify `profileUrl` is correctly derived from baseUri and resource slug.
- Verify link relations use IANA precedence (e.g., `self` for GET, ALPS fragment for domain-specific).
- Verify all states from the statechart appear as entries, including states with no methods (orphan states).
- Verify `dotnet build` and `dotnet test` pass cleanly.

---

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

### How to Add Activity Log Entries

**When adding an entry**:
1. Scroll to the bottom of this file (Activity Log section below "Valid lanes")
2. **APPEND the new entry at the END** (do NOT prepend or insert in middle)
3. Use exact format: `- YYYY-MM-DDTHH:MM:SSZ -- agent_id -- lane=<lane> -- <action>`
4. Timestamp MUST be current time in UTC (check with `date -u "+%Y-%m-%dT%H:%M:%SZ"`)
5. Lane MUST match the frontmatter `lane:` field exactly
6. Agent ID should identify who made the change (claude-sonnet-4-5, codex, etc.)

**Format**:
```
- YYYY-MM-DDTHH:MM:SSZ -- <agent_id> -- lane=<lane> -- <brief action description>
```

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

**Initial entry**:
- 2026-03-19T02:15:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-19T03:41:36Z – claude-opus-wp06 – shell_pid=21017 – lane=doing – Assigned agent via workflow command
- 2026-03-19T03:48:49Z -- claude-opus-wp06 -- lane=for_review -- Implementation complete: T032-T037 all done. AffordanceMapGenerator.fs created, CLI wired, 27/27 tests pass. Committed as 9f5d3ce.
- 2026-03-19T03:52:40Z – claude-opus-wp06-review – shell_pid=23922 – lane=doing – Started review via workflow command
- 2026-03-19T03:55:18Z – claude-opus-wp06-review – shell_pid=23922 – lane=done – Review passed: all 6 checklist items verified. Build 0 warnings/0 errors. 27/27 AffordanceMapGenerator tests + 5/5 AffordanceMap tests pass. Composite key uses | separator with * wildcard for stateless. Profile URLs correctly derived. Link relations populated with IANA precedence. CLI --format affordance-map wired with --base-uri support. JSON output conforms to contracts/affordance-map-schema.json. Good testability design with optional timestamp parameter.
