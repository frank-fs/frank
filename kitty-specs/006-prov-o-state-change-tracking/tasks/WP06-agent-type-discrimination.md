---
work_package_id: WP06
title: Agent Type Discrimination
lane: "doing"
dependencies:
- WP03
subtasks: [T027, T028, T029, T030]
agent: "claude-opus-reviewer"
shell_pid: "43144"
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-003, FR-012]
---

# Work Package Prompt: WP06 -- Agent Type Discrimination

## Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Implementation Command

```bash
spec-kitty implement WP06 --base WP05
```

Depends on WP03 (graph builder for RDF output) and WP05 (observer for agent extraction).

---

## Objectives & Success Criteria

- `X-Agent-Type` and `X-Agent-Model` header extraction flows correctly through the observer
- `AgentType.LlmAgent` produces dual `rdf:type` triples in the RDF graph (`prov:SoftwareAgent` + `frank:LlmAgent`)
- `ClaimsPrincipal` identity extraction correctly handles `ClaimTypes.Name` and `ClaimTypes.NameIdentifier`
- End-to-end test: authenticated user, unauthenticated request, and LLM-origin request each produce correctly typed agent in store AND in serialized RDF output
- All three agent type acceptance scenarios from User Story 3 are covered by tests

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/006-prov-o-state-change-tracking/spec.md` -- User Story 3 (Agent Type Discrimination), FR-012
- `kitty-specs/006-prov-o-state-change-tracking/data-model.md` -- AgentType DU, PROV-O Triple Pattern (Agent section)
- `kitty-specs/006-prov-o-state-change-tracking/quickstart.md` -- Agent Type Discrimination table

**Key constraints**:
- Agent type classification is automatic (no per-resource configuration)
- Three agent types: `prov:Person`, `prov:SoftwareAgent`, `prov:SoftwareAgent + frank:LlmAgent`
- LLM identification via `X-Agent-Type: llm` header (convention, not standard)
- Optional model identifier via `X-Agent-Model` header
- `frank:agentModel` property only asserted when model is `Some`
- This WP integrates and tests behavior that was partially implemented in WP03 (graph builder agent triples) and WP05 (observer agent extraction). The focus here is on the integration, edge cases, and end-to-end flow.

---

## Subtasks & Detailed Guidance

### Subtask T027 -- Implement X-Agent-Type and X-Agent-Model header extraction

**Purpose**: Ensure the TransitionObserver correctly reads agent type hints from HTTP headers.

**Steps**:
1. Verify that `TransitionEvent.Headers` map is populated from `HttpContext.Request.Headers` in the transition pipeline (this may be a Frank.Statecharts responsibility -- document the contract)

2. In `TransitionObserver.AgentExtraction.extractAgent`, verify the header extraction logic:
   - `X-Agent-Type` header value `"llm"` (case-sensitive) triggers LlmAgent classification
   - `X-Agent-Model` header value is passed as `model` parameter to `LlmAgent` DU case
   - Missing headers result in default Person/SoftwareAgent classification

3. Add handling for edge cases:
   - `X-Agent-Type: llm` with unauthenticated principal: still produce `LlmAgent` but with identifier from header or "anonymous-llm"
   - `X-Agent-Type: robot` (unknown value): ignore, treat as default classification
   - Multiple `X-Agent-Type` headers: use first value

```fsharp
// In extractAgent, before the authenticated person check:
let isLlmAgent =
    headers
    |> Map.tryFind "X-Agent-Type"
    |> Option.map (fun v -> v.ToLowerInvariant() = "llm")
    |> Option.defaultValue false

if isLlmAgent then
    let identifier = ... // from claims or "anonymous-llm"
    let model = headers |> Map.tryFind "X-Agent-Model"
    { Id = sprintf "urn:frank:agent:llm:%s" identifier
      AgentType = LlmAgent(identifier, model) }
```

**Files**: `src/Frank.Provenance/TransitionObserver.fs`
**Notes**:
- Case-insensitive comparison on header value for robustness (`ToLowerInvariant`)
- Unknown `X-Agent-Type` values are silently ignored (not logged as errors -- they are not errors)
- If LLM agent is unauthenticated, the identifier falls back to "anonymous-llm" (not "system", which is for non-LLM unauthenticated)

### Subtask T028 -- Implement LlmAgent graph builder support

**Purpose**: Verify and refine the dual-typing RDF output for LLM agents.

**Steps**:
1. Review the `addAgent` function in `GraphBuilder.fs` (implemented in WP03 T014)
2. Verify LlmAgent case produces:
   - `rdf:type prov:SoftwareAgent` (first type)
   - `rdf:type frank:LlmAgent` (second type -- same subject node)
   - `prov:label` with identifier
   - `frank:agentModel` with model string (only when `Some`)

3. If not already implemented in WP03, add the dual-typing:
```fsharp
| AgentType.LlmAgent(identifier, model) ->
    assertTriple graph agentNode rdfType (uriNode graph ProvVocabulary.SoftwareAgent)
    assertTriple graph agentNode rdfType (uriNode graph ProvVocabulary.LlmAgent)
    assertTriple graph agentNode (uriNode graph ProvVocabulary.label) (plainLiteral graph identifier)
    match model with
    | Some m -> assertTriple graph agentNode (uriNode graph ProvVocabulary.agentModel) (plainLiteral graph m)
    | None -> ()
```

4. Verify the `frank:LlmAgent` URI is `https://frank-web.dev/ns/provenance/LlmAgent` (from ProvVocabulary)

**Files**: `src/Frank.Provenance/GraphBuilder.fs`
**Notes**: This subtask may be a verification-only task if WP03 already implemented the LlmAgent case correctly. The key value of this WP is the integration testing across observer + graph builder.

### Subtask T029 -- Implement ClaimsPrincipal identity extraction

**Purpose**: Refine and test the extraction of name and identifier from ClaimsPrincipal for Person agents.

**Steps**:
1. Review the agent extraction in `TransitionObserver.AgentExtraction` (implemented in WP05 T023)
2. Verify claim extraction handles these scenarios:

| ClaimsPrincipal State | Expected Name | Expected Identifier |
|----------------------|---------------|---------------------|
| Has `ClaimTypes.Name` + `ClaimTypes.NameIdentifier` | Name claim value | NameIdentifier claim value |
| Has `ClaimTypes.Name` only | Name claim value | `Identity.Name` or generated GUID |
| Has `ClaimTypes.NameIdentifier` only | "Unknown" | NameIdentifier claim value |
| Has neither (but is authenticated) | "Unknown" | `Identity.Name` or generated GUID |
| Has custom claims only (e.g., email) | "Unknown" | `Identity.Name` or generated GUID |

3. If needed, add extraction for additional common claim types:
   - `ClaimTypes.Email` as fallback identifier
   - `ClaimTypes.GivenName` + `ClaimTypes.Surname` as fallback name

4. Ensure generated GUID identifiers are deterministic within a request (same principal produces same ID) but unique across different principals

**Files**: `src/Frank.Provenance/TransitionObserver.fs`
**Notes**:
- The primary claims are `ClaimTypes.Name` and `ClaimTypes.NameIdentifier` (standard ASP.NET Core)
- Additional claim fallbacks are optional but improve the agent identification quality
- The generated GUID fallback ensures every authenticated principal gets a unique agent, even if no standard claims are present

### Subtask T030 -- Create `AgentTypeTests.fs`

**Purpose**: Comprehensive end-to-end tests for agent type discrimination across the full pipeline.

**Steps**:
1. Create `test/Frank.Provenance.Tests/AgentTypeTests.fs`
2. Write tests covering ALL acceptance scenarios from User Story 3:

**a. Acceptance Scenario 1: Authenticated user -> prov:Person**
- Create `ClaimsPrincipal` with `ClaimTypes.Name = "Jane Doe"` and `ClaimTypes.NameIdentifier = "jane@example.com"`
- Create `TransitionEvent` with this principal, no special headers
- Run through `TransitionObserver.OnNext`
- Verify appended record has `AgentType.Person("Jane Doe", "jane@example.com")`
- Build graph from record, verify `rdf:type prov:Person` triple exists
- Verify `prov:label "Jane Doe"` triple exists

**b. Acceptance Scenario 2: Unauthenticated -> prov:SoftwareAgent**
- Create `TransitionEvent` with `User = None`
- Run through observer
- Verify `AgentType.SoftwareAgent("system")`
- Build graph, verify `rdf:type prov:SoftwareAgent` triple exists
- Verify `prov:label "system"` triple exists

**c. Acceptance Scenario 3: LLM origin -> prov:SoftwareAgent + frank:LlmAgent**
- Create authenticated `ClaimsPrincipal` with identifier
- Create `TransitionEvent` with `X-Agent-Type: llm` and `X-Agent-Model: claude-opus-4` headers
- Run through observer
- Verify `AgentType.LlmAgent(identifier, Some "claude-opus-4")`
- Build graph, verify BOTH `rdf:type prov:SoftwareAgent` AND `rdf:type frank:LlmAgent` triples
- Verify `frank:agentModel "claude-opus-4"` triple exists

**d. Edge case: LLM without model header**
- `X-Agent-Type: llm` but no `X-Agent-Model`
- Verify `LlmAgent(id, None)`
- Build graph, verify NO `frank:agentModel` triple

**e. Edge case: Anonymous principal (authenticated = false)**
- `Some principal` with `IsAuthenticated = false`
- Verify `SoftwareAgent("system")`

**f. Edge case: Authenticated with minimal claims**
- `ClaimsPrincipal` with only `IsAuthenticated = true`, no standard claims
- Verify `Person("Unknown", ...)` with generated identifier

**g. Edge case: Unknown X-Agent-Type value**
- `X-Agent-Type: robot` header
- Verify treated as normal Person (header ignored)

3. Add `AgentTypeTests.fs` to test `.fsproj`

**Files**: `test/Frank.Provenance.Tests/AgentTypeTests.fs`
**Validation**: `dotnet test test/Frank.Provenance.Tests/` passes with all agent type tests green.

---

## Test Strategy

- Run `dotnet build` to verify compilation on all targets
- Run `dotnet test test/Frank.Provenance.Tests/` -- all agent type tests pass
- Each test validates BOTH the F# domain type AND the RDF graph output for end-to-end correctness

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Auth provider claim format variation | Fall back to SoftwareAgent when identity claims absent; test with minimal ClaimsPrincipal |
| Case sensitivity of X-Agent-Type header | Use case-insensitive comparison (ToLowerInvariant) |
| LLM agent with unauthenticated principal | Use "anonymous-llm" identifier, not "system" |
| Duplicate agent nodes in graph | dotNetRdf Assert is idempotent; duplicate triples ignored |

---

## Review Guidance

- Verify all 3 acceptance scenarios from User Story 3 are covered by tests
- Verify LlmAgent dual-typing produces exactly 2 rdf:type triples
- Verify `frank:agentModel` is NOT asserted when model is None
- Verify case-insensitive header comparison for X-Agent-Type
- Verify fallback behavior for missing claims (no NullReferenceException)
- Verify agent URN format consistency across all three types
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-15T19:35:44Z – unknown – lane=for_review – Moved to for_review
- 2026-03-15T19:46:43Z – claude-opus-reviewer – shell_pid=43144 – lane=doing – Started review via workflow command
