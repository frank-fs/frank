---
work_package_id: "WP03"
title: "GraphBuilder (dotNetRdf)"
lane: "doing"
dependencies: ["WP01"]
requirement_refs: ["FR-004", "FR-005", "FR-006", "FR-014"]
subtasks: ["T012", "T013", "T014", "T015", "T016"]
agent: "claude-opus-reviewer"
shell_pid: "41078"
history:
  - timestamp: "2026-03-07T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP03 -- GraphBuilder (dotNetRdf)

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
spec-kitty implement WP03 --base WP01
```

Depends on WP01 for core types and vocabulary constants.

---

## Objectives & Success Criteria

- Implement `GraphBuilder.toGraph : ProvenanceRecord list -> IGraph` that constructs a complete PROV-O RDF graph
- All PROV-O relationships are correctly asserted: `prov:wasGeneratedBy`, `prov:used`, `prov:wasAssociatedWith`, `prov:wasAttributedTo`, `prov:wasDerivedFrom`
- Activity nodes have `prov:startedAtTime` and `prov:endedAtTime` as XSD dateTime typed literals
- Agent nodes have correct `rdf:type` based on `AgentType` DU (Person, SoftwareAgent, LlmAgent)
- Frank extension properties (`frank:httpMethod`, `frank:eventName`, `frank:stateName`) are correctly asserted
- Namespace prefixes registered for readable Turtle output (`prov:`, `frank:`, `xsd:`)
- Empty input list produces an empty graph (not null)
- Multiple records produce a single unified graph

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/006-prov-o-state-change-tracking/research.md` -- Decision 2 (dotNetRdf graph construction API)
- `kitty-specs/006-prov-o-state-change-tracking/data-model.md` -- PROV-O Triple Pattern section (the exact Turtle output expected)

**Key constraints**:
- Use dotNetRdf `Graph` class with explicit triple assertion (`graph.Assert(Triple(...))`)
- Use `UriFactory.Root.Create(uri)` for URI node creation (not `new Uri(...)`)
- Use `graph.CreateLiteralNode(value, datatypeUri)` for typed literals
- Register namespace prefixes: `prov:`, `frank:`, `xsd:`, `rdf:`
- Reuse `ProvVocabulary` constants for all URIs (Constitution VIII: no duplicated logic)
- The function is pure: takes records, returns graph. No side effects, no store access.
- dotNetRdf.Core version 3.5.1 (matching Frank.LinkedData)

**dotNetRdf API reference** (from research.md):
```fsharp
let graph = new Graph()
let subject = graph.CreateUriNode(UriFactory.Root.Create(uri))
let predicate = graph.CreateUriNode(UriFactory.Root.Create(predicateUri))
let object_ = graph.CreateUriNode(UriFactory.Root.Create(objectUri))
graph.Assert(Triple(subject, predicate, object_)) |> ignore

// Typed literal
let xsdDateTime = UriFactory.Root.Create("http://www.w3.org/2001/XMLSchema#dateTime")
let literal = graph.CreateLiteralNode(timestamp.ToString("o"), xsdDateTime)
```

---

## Subtasks & Detailed Guidance

### Subtask T012 -- Create `GraphBuilder.fs` with `toGraph` function

**Purpose**: Create the module skeleton with the main public function and namespace prefix registration.

**Steps**:
1. Create `src/Frank.Provenance/GraphBuilder.fs`
2. Define the module structure:

```fsharp
namespace Frank.Provenance

open VDS.RDF

/// Constructs dotNetRdf IGraph instances from ProvenanceRecord collections.
/// Pure projection function: no side effects, no store access.
module GraphBuilder =

    /// Helper: assert a triple on the graph
    let private assertTriple (graph: IGraph) (s: INode) (p: INode) (o: INode) =
        graph.Assert(Triple(s, p, o)) |> ignore

    /// Helper: create a URI node from a vocabulary constant
    let private uriNode (graph: IGraph) (uri: string) =
        graph.CreateUriNode(UriFactory.Root.Create(uri))

    /// Helper: create a typed literal node
    let private literalNode (graph: IGraph) (value: string) (datatype: string) =
        graph.CreateLiteralNode(value, UriFactory.Root.Create(datatype))

    /// Helper: create a plain literal node
    let private plainLiteral (graph: IGraph) (value: string) =
        graph.CreateLiteralNode(value)

    /// Register namespace prefixes for readable serialization output
    let private registerPrefixes (graph: IGraph) =
        graph.NamespaceMap.AddNamespace("prov", UriFactory.Root.Create(ProvVocabulary.ProvNamespace))
        graph.NamespaceMap.AddNamespace("frank", UriFactory.Root.Create(ProvVocabulary.FrankNamespace))
        graph.NamespaceMap.AddNamespace("xsd", UriFactory.Root.Create(ProvVocabulary.XsdNamespace))
        graph.NamespaceMap.AddNamespace("rdf", UriFactory.Root.Create("http://www.w3.org/1999/02/22-rdf-syntax-ns#"))

    // ... (addActivity, addAgent, addEntity helpers defined in T013-T015)

    /// Project a list of ProvenanceRecords into a complete PROV-O RDF graph.
    let toGraph (records: ProvenanceRecord list) : IGraph =
        let graph = new Graph()
        registerPrefixes graph
        for record in records do
            addActivity graph record
            addAgent graph record.Agent
            addUsedEntity graph record
            addGeneratedEntity graph record
        graph
```

3. Add `GraphBuilder.fs` to `.fsproj` after `Store.fs` (or after `MailboxProcessorStore.fs` if it exists):
   ```xml
   <Compile Include="GraphBuilder.fs" />
   ```

**Files**: `src/Frank.Provenance/GraphBuilder.fs`
**Notes**: The helper functions (`uriNode`, `literalNode`, `assertTriple`) keep triple assertion concise and readable. The `registerPrefixes` function ensures Turtle output uses prefixed names instead of full URIs.

### Subtask T013 -- Implement Activity triple construction

**Purpose**: Add PROV-O Activity triples to the graph for each provenance record.

**Steps**:
1. Implement `addActivity` in GraphBuilder:

```fsharp
let private addActivity (graph: IGraph) (record: ProvenanceRecord) =
    let activity = record.Activity
    let activityNode = uriNode graph activity.Id
    let rdfType = uriNode graph ProvVocabulary.RdfType

    // rdf:type prov:Activity
    assertTriple graph activityNode rdfType (uriNode graph ProvVocabulary.Activity)

    // prov:startedAtTime
    let startedAt = literalNode graph (activity.StartedAt.ToString("o")) ProvVocabulary.XsdDateTime
    assertTriple graph activityNode (uriNode graph ProvVocabulary.startedAtTime) startedAt

    // prov:endedAtTime
    let endedAt = literalNode graph (activity.EndedAt.ToString("o")) ProvVocabulary.XsdDateTime
    assertTriple graph activityNode (uriNode graph ProvVocabulary.endedAtTime) endedAt

    // prov:wasAssociatedWith -> Agent
    assertTriple graph activityNode (uriNode graph ProvVocabulary.wasAssociatedWith) (uriNode graph record.Agent.Id)

    // prov:used -> UsedEntity (pre-transition)
    assertTriple graph activityNode (uriNode graph ProvVocabulary.used) (uriNode graph record.UsedEntity.Id)

    // frank:httpMethod
    assertTriple graph activityNode (uriNode graph ProvVocabulary.httpMethod) (plainLiteral graph activity.HttpMethod)

    // frank:eventName
    assertTriple graph activityNode (uriNode graph ProvVocabulary.eventName) (plainLiteral graph activity.EventName)
```

**Files**: `src/Frank.Provenance/GraphBuilder.fs`
**Notes**: Timestamps use ISO 8601 "o" format with XSD dateTime datatype. The `frank:httpMethod` and `frank:eventName` are plain literals (no datatype needed for simple strings). The activity references the agent via `prov:wasAssociatedWith` and the pre-transition entity via `prov:used`.

### Subtask T014 -- Implement Agent triple construction

**Purpose**: Add PROV-O Agent triples with correct type discrimination.

**Steps**:
1. Implement `addAgent` in GraphBuilder:

```fsharp
let private addAgent (graph: IGraph) (agent: ProvenanceAgent) =
    let agentNode = uriNode graph agent.Id
    let rdfType = uriNode graph ProvVocabulary.RdfType

    // rdf:type based on AgentType
    match agent.AgentType with
    | AgentType.Person(name, _identifier) ->
        assertTriple graph agentNode rdfType (uriNode graph ProvVocabulary.Person)
        assertTriple graph agentNode (uriNode graph ProvVocabulary.label) (plainLiteral graph name)

    | AgentType.SoftwareAgent identifier ->
        assertTriple graph agentNode rdfType (uriNode graph ProvVocabulary.SoftwareAgent)
        assertTriple graph agentNode (uriNode graph ProvVocabulary.label) (plainLiteral graph identifier)

    | AgentType.LlmAgent(identifier, model) ->
        // Dual-typing: prov:SoftwareAgent + frank:LlmAgent
        assertTriple graph agentNode rdfType (uriNode graph ProvVocabulary.SoftwareAgent)
        assertTriple graph agentNode rdfType (uriNode graph ProvVocabulary.LlmAgent)
        assertTriple graph agentNode (uriNode graph ProvVocabulary.label) (plainLiteral graph identifier)
        match model with
        | Some m ->
            assertTriple graph agentNode (uriNode graph ProvVocabulary.agentModel) (plainLiteral graph m)
        | None -> ()
```

**Files**: `src/Frank.Provenance/GraphBuilder.fs`
**Notes**:
- `AgentType.Person` maps to `prov:Person` (subclass of `prov:Agent`) with `prov:label` for the name
- `AgentType.SoftwareAgent` maps to `prov:SoftwareAgent` with the identifier as label
- `AgentType.LlmAgent` has DUAL `rdf:type` assertions: both `prov:SoftwareAgent` and `frank:LlmAgent`. The optional model is asserted via `frank:agentModel`.
- Agent deduplication: if the same agent appears in multiple records, the same triples will be asserted multiple times. dotNetRdf's `Graph.Assert` is idempotent (duplicate triples are ignored), so this is safe.

### Subtask T015 -- Implement Entity triple construction

**Purpose**: Add PROV-O Entity triples for both pre-transition (used) and post-transition (generated) entities.

**Steps**:
1. Implement `addUsedEntity` and `addGeneratedEntity` in GraphBuilder:

```fsharp
let private addUsedEntity (graph: IGraph) (record: ProvenanceRecord) =
    let entity = record.UsedEntity
    let entityNode = uriNode graph entity.Id
    let rdfType = uriNode graph ProvVocabulary.RdfType

    // rdf:type prov:Entity
    assertTriple graph entityNode rdfType (uriNode graph ProvVocabulary.Entity)

    // prov:wasAttributedTo -> Agent
    assertTriple graph entityNode (uriNode graph ProvVocabulary.wasAttributedTo) (uriNode graph record.Agent.Id)

    // frank:stateName
    assertTriple graph entityNode (uriNode graph ProvVocabulary.stateName) (plainLiteral graph entity.StateName)

let private addGeneratedEntity (graph: IGraph) (record: ProvenanceRecord) =
    let entity = record.GeneratedEntity
    let entityNode = uriNode graph entity.Id
    let rdfType = uriNode graph ProvVocabulary.RdfType

    // rdf:type prov:Entity
    assertTriple graph entityNode rdfType (uriNode graph ProvVocabulary.Entity)

    // prov:wasGeneratedBy -> Activity
    assertTriple graph entityNode (uriNode graph ProvVocabulary.wasGeneratedBy) (uriNode graph record.Activity.Id)

    // prov:wasAttributedTo -> Agent
    assertTriple graph entityNode (uriNode graph ProvVocabulary.wasAttributedTo) (uriNode graph record.Agent.Id)

    // prov:wasDerivedFrom -> UsedEntity (pre-transition)
    assertTriple graph entityNode (uriNode graph ProvVocabulary.wasDerivedFrom) (uriNode graph record.UsedEntity.Id)

    // frank:stateName
    assertTriple graph entityNode (uriNode graph ProvVocabulary.stateName) (plainLiteral graph entity.StateName)
```

**Files**: `src/Frank.Provenance/GraphBuilder.fs`
**Notes**:
- The pre-transition entity (UsedEntity) has `prov:wasAttributedTo` -> Agent and `frank:stateName`
- The post-transition entity (GeneratedEntity) has ALL of: `prov:wasGeneratedBy` -> Activity, `prov:wasAttributedTo` -> Agent, `prov:wasDerivedFrom` -> UsedEntity, and `frank:stateName`
- This matches the PROV-O Triple Pattern in data-model.md exactly
- The `wasDerivedFrom` relationship links post-transition to pre-transition entity, enabling provenance chain traversal

### Subtask T016 -- Create `GraphBuilderTests.fs`

**Purpose**: Validate that `toGraph` produces correct PROV-O triples for all entity types and relationships.

**Steps**:
1. Create `test/Frank.Provenance.Tests/GraphBuilderTests.fs`
2. Create helper to build a test `ProvenanceRecord` (reuse or import from StoreTests if WP02 is available)
3. Write Expecto tests covering:

**a. Activity triples**: Build graph from one record. Verify activity node has `rdf:type prov:Activity`, `prov:startedAtTime`, `prov:endedAtTime`, `prov:wasAssociatedWith`, `prov:used`, `frank:httpMethod`, `frank:eventName`.

**b. Person agent triples**: Build graph with `AgentType.Person`. Verify `rdf:type prov:Person` and `prov:label` with name.

**c. SoftwareAgent triples**: Build graph with `AgentType.SoftwareAgent`. Verify `rdf:type prov:SoftwareAgent` and `prov:label`.

**d. LlmAgent dual-typing**: Build graph with `AgentType.LlmAgent`. Verify BOTH `rdf:type prov:SoftwareAgent` AND `rdf:type frank:LlmAgent`. Verify `frank:agentModel` when model is `Some`. Verify no `frank:agentModel` when model is `None`.

**e. Used entity triples**: Verify `rdf:type prov:Entity`, `prov:wasAttributedTo`, `frank:stateName`.

**f. Generated entity triples**: Verify `rdf:type prov:Entity`, `prov:wasGeneratedBy`, `prov:wasAttributedTo`, `prov:wasDerivedFrom`, `frank:stateName`.

**g. Empty input**: `toGraph []` returns a graph with 0 triples (not null).

**h. Multiple records**: `toGraph [rec1; rec2]` produces a unified graph with triples from both records.

**Triple verification helper**:
```fsharp
let hasTriple (graph: IGraph) (s: string) (p: string) (o: string) =
    let sNode = graph.CreateUriNode(UriFactory.Root.Create(s))
    let pNode = graph.CreateUriNode(UriFactory.Root.Create(p))
    let oNode = graph.CreateUriNode(UriFactory.Root.Create(o))
    graph.ContainsTriple(Triple(sNode, pNode, oNode))

let hasLiteralTriple (graph: IGraph) (s: string) (p: string) (value: string) =
    let sNode = graph.CreateUriNode(UriFactory.Root.Create(s))
    let pNode = graph.CreateUriNode(UriFactory.Root.Create(p))
    graph.GetTriplesWithSubjectPredicate(sNode, pNode)
    |> Seq.exists (fun t ->
        match t.Object with
        | :? VDS.RDF.ILiteralNode as lit -> lit.Value = value
        | _ -> false)
```

4. Add `GraphBuilderTests.fs` to test `.fsproj`

**Files**: `test/Frank.Provenance.Tests/GraphBuilderTests.fs`
**Validation**: `dotnet test test/Frank.Provenance.Tests/` passes with all graph builder tests green. Verify triple counts match expected counts per record.

---

## Test Strategy

- Run `dotnet build` to verify compilation on all targets
- Run `dotnet test test/Frank.Provenance.Tests/` -- all graph builder tests pass
- Visually inspect one test that serializes the graph to Turtle (using dotNetRdf writer) and compare with the expected Turtle pattern in data-model.md

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| dotNetRdf API differences between versions | Pin to 3.5.1 matching Frank.LinkedData. Test `UriFactory.Root.Create` availability. |
| Agent deduplication across records | dotNetRdf `Graph.Assert` is idempotent for duplicate triples. No special handling needed. |
| LlmAgent dual-typing | Explicitly test that both `rdf:type` triples exist on the same node. |
| Namespace prefix registration | Test that `graph.NamespaceMap` contains expected prefixes. |

---

## Review Guidance

- Verify all PROV-O relationships match the triple pattern in `data-model.md`
- Verify `ProvVocabulary` constants are used everywhere (no hardcoded URI strings in GraphBuilder)
- Verify `AgentType.LlmAgent` produces two `rdf:type` triples
- Verify timestamp format is ISO 8601 with XSD dateTime datatype
- Verify empty input produces empty graph (not null or exception)
- Verify `graph.Assert` return values are ignored (piped to `ignore`)
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-15T19:31:58Z – unknown – lane=for_review – Moved to for_review
- 2026-03-15T19:42:45Z – claude-opus-reviewer – shell_pid=41078 – lane=doing – Started review via workflow command
