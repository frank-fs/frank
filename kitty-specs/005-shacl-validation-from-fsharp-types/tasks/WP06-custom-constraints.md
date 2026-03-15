---
work_package_id: WP06
title: Custom Constraints & Conflict Detection
lane: "done"
dependencies:
- WP01
- WP10
- WP12
base_branch: 005-shacl-validation-from-fsharp-types-WP06-merge-base
base_commit: 39b0e2a28d58f68b80d0bddd9ae9ff078308c98d
created_at: '2026-03-15T18:49:25.775068+00:00'
subtasks: [T032, T033, T034, T035, T036]
shell_pid: "26354"
agent: "claude-opus"
reviewed_by: "Ryan Riley"
review_status: "approved"
history:
- timestamp: '2026-03-07T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
- timestamp: '2026-03-14T00:00:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Amended per build-time SHACL unification design
amendment_ref: docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md
requirement_refs: [FR-015, FR-016]
---

# Work Package Prompt: WP06 -- Custom Constraints & Conflict Detection

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

## Amendment (2026-03-14): Build-Time SHACL Unification

> This WP is amended per the [build-time SHACL unification design](../../../docs/superpowers/specs/2026-03-14-build-time-shacl-unification-design.md). Key changes:
>
> - **Custom constraints are also expressible as F# attributes** on types/fields (e.g., `[<Pattern("...")>]`, `[<MinInclusive(0)>]`). These are extracted by FCS at build time (WP09) and emitted as SHACL triples (WP10).
> - **Attribute types** are defined in a `Frank.Validation.Annotations` module (or lightweight assembly).
> - **ShapeMerger receives pre-loaded shapes** from the embedded Turtle resource (via ShapeLoader, WP12) rather than reflection-derived shapes. Conflict detection and additive merging logic is unchanged.
> - **Runtime API registration remains** as a secondary mechanism for SPARQL cross-field constraints and other constraints that cannot be expressed as attributes. ShapeMerger applies these on top of the pre-loaded shapes.
> - **Dependencies updated**: now depends on WP10 (shapes include attribute-derived constraints) and WP12 (ShapeLoader provides pre-loaded shapes). WP01 dependency remains.

---

## Implementation Command

```bash
spec-kitty implement WP06 --base WP12
```

Depends on WP01 (types), WP10 (attribute-derived constraints in shapes), WP12 (ShapeLoader provides pre-loaded shapes).

---

## Objectives & Success Criteria

- Implement `ShapeMerger.fs`: merge custom constraints with auto-derived shapes (FR-015, FR-016)
- Custom constraints are additive only -- cannot weaken auto-derived constraints (FR-016)
- Detect contradictions at startup and raise `InvalidOperationException` (FR-016, SC-007)
- Support sh:pattern, sh:minInclusive, sh:maxInclusive, sh:minLength, sh:maxLength, sh:in, SPARQL constraints (FR-015)
- SPARQL cross-field constraints use dotNetRdf's SPARQL engine (FR-015)
- Conflicting constraints detected at startup, not at request time (SC-007)

---

## Context & Constraints

**Reference documents**:
- `kitty-specs/005-shacl-validation-from-fsharp-types/data-model.md` -- CustomConstraint, ConstraintKind, merging rules
- `kitty-specs/005-shacl-validation-from-fsharp-types/spec.md` -- FR-015, FR-016, User Story 5, edge cases
- `kitty-specs/005-shacl-validation-from-fsharp-types/plan.md` -- Constitution check VIII (no duplicated logic -- merging in one place)

**Key constraints**:
- Merge runs once at startup, not per request
- Custom `Pattern` + auto-derived `Pattern`: both apply (AND semantics)
- Custom `InValues` + auto-derived `InValues`: intersection (tighter constraint)
- Custom `MinCount 0` on required field (auto `MinCount 1`): `InvalidOperationException`
- Custom `MinInclusive > MaxInclusive`: `InvalidOperationException`
- SPARQL constraints stored as `sh:sparql` on NodeShape, validated by dotNetRdf SPARQL engine
- Conservative conflict detection: only flag direct contradictions, not semantic tensions

---

## Subtasks & Detailed Guidance

### Subtask T032 -- Create `ShapeMerger.fs`

**Purpose**: Implement the merge function that takes an auto-derived `ShaclShape` and a list of `CustomConstraint` entries, producing a new `ShaclShape` with all constraints applied.

**Steps**:
1. Create `src/Frank.Validation/ShapeMerger.fs`
2. Implement the merge function:

```fsharp
namespace Frank.Validation

module ShapeMerger =
    /// Merge custom constraints into an auto-derived shape.
    /// Custom constraints are additive -- they can tighten but not weaken the shape.
    /// Raises InvalidOperationException for conflicting constraints.
    let merge (baseShape: ShaclShape) (customs: CustomConstraint list) : ShaclShape =
        let mergedProperties =
            baseShape.Properties
            |> List.map (fun prop ->
                let propConstraints =
                    customs |> List.filter (fun c -> c.PropertyPath = prop.Path)
                propConstraints
                |> List.fold (fun p c -> applyConstraint p c.Constraint) prop)
        { baseShape with Properties = mergedProperties }

    /// Apply a single custom constraint to a property shape.
    /// Validates that the constraint does not weaken the auto-derived shape.
    let private applyConstraint (prop: PropertyShape) (kind: ConstraintKind) : PropertyShape =
        match kind with
        | PatternConstraint regex ->
            // Additive: add pattern (AND with existing)
            match prop.Pattern with
            | Some existing ->
                { prop with Pattern = Some (sprintf "(%s)(%s)" existing regex) }
            | None ->
                { prop with Pattern = Some regex }
        | MinInclusiveConstraint value ->
            { prop with MinInclusive = Some value }
        | MaxInclusiveConstraint value ->
            { prop with MaxInclusive = Some value }
        | InValuesConstraint values ->
            match prop.InValues with
            | Some existing ->
                // Intersection: only values in BOTH lists are allowed
                let intersection = existing |> List.filter (fun v -> values |> List.contains v)
                if intersection.IsEmpty then
                    raise (System.InvalidOperationException(
                        sprintf "Custom InValues constraint on '%s' has no intersection with auto-derived values" prop.Path))
                { prop with InValues = Some intersection }
            | None ->
                { prop with InValues = Some values }
        | MinLengthConstraint length ->
            // Additive: always tightens
            { prop with ... } // Add MinLength field or store in custom metadata
        | _ -> ...
```

3. Add `ShapeMerger.fs` to the `.fsproj` compile list after `ShapeLoader.fs`.

**Files**: `src/Frank.Validation/ShapeMerger.fs`
**Notes**: The merge produces a NEW immutable `ShaclShape` -- it does not mutate the base shape. The base shape is preserved for use as the default in capability-dependent resolution.

### Subtask T033 -- Implement additive constraint merging

**Purpose**: Handle each `ConstraintKind` variant, ensuring constraints only tighten the shape.

**Steps**:
1. Implement merging for each constraint kind:

- **PatternConstraint**: Add `sh:pattern`. If base already has a pattern, combine with AND semantics (both must match). Use regex alternation or separate pattern assertions.
- **MinInclusiveConstraint**: Set `sh:minInclusive`. If base has one, use the larger value (tighter).
- **MaxInclusiveConstraint**: Set `sh:maxInclusive`. If base has one, use the smaller value (tighter).
- **MinExclusiveConstraint**: Set `sh:minExclusive`. Same tightening logic.
- **MaxExclusiveConstraint**: Set `sh:maxExclusive`. Same tightening logic.
- **MinLengthConstraint**: Set `sh:minLength`. If base has one, use the larger value.
- **MaxLengthConstraint**: Set `sh:maxLength`. If base has one, use the smaller value.
- **InValuesConstraint**: Intersect with existing `sh:in` values. Empty intersection = conflict.
- **CustomShaclConstraint**: Store raw predicate/value pair for inclusion in the shapes graph.

2. Validate cross-constraint consistency after merging:
   - `MinInclusive > MaxInclusive` -> error
   - `MinLength > MaxLength` -> error
   - `MinExclusive >= MaxExclusive` -> error

**Files**: `src/Frank.Validation/ShapeMerger.fs`
**Notes**: Some constraint kinds (MinLength, MaxLength, MinExclusive, MaxExclusive) are not fields on `PropertyShape` in the current type definition. Either extend `PropertyShape` to include them, or store them in a separate `CustomConstraints` list on `PropertyShape`. The latter is preferred to keep the core type stable.

### Subtask T034 -- Implement conflict detection

**Purpose**: Detect contradictions between custom constraints and auto-derived constraints at startup.

**Steps**:
1. Implement conflict checks in `ShapeMerger.merge`:

```fsharp
    let private detectConflict (prop: PropertyShape) (kind: ConstraintKind) =
        match kind with
        | MinInclusiveConstraint _ | MaxInclusiveConstraint _ ->
            // Check if min > max after merge
            ...
        | InValuesConstraint values ->
            match prop.InValues with
            | Some existing ->
                let intersection = Set.intersect (Set.ofList existing) (Set.ofList values)
                if intersection.IsEmpty then
                    raise (InvalidOperationException(
                        sprintf "Custom InValues on '%s' contradicts auto-derived values. No common values remain." prop.Path))
            | None -> ()
        | _ -> ()

    let private detectMinCountConflict (prop: PropertyShape) (customs: CustomConstraint list) =
        // Check if any custom constraint attempts to set minCount 0 on a required field
        // This is the most common contradiction: trying to make a required field optional
        let hasMinCountZero =
            customs |> List.exists (fun c ->
                c.PropertyPath = prop.Path &&
                match c.Constraint with
                | _ -> false) // MinCount is not a ConstraintKind -- it's derived from the type
        // Note: minCount cannot be customized via ConstraintKind since it's type-derived.
        // Conflict detection here focuses on InValues, MinInclusive/MaxInclusive ranges.
        ()
```

2. Detect conflicts:
   - InValues intersection is empty
   - MinInclusive > MaxInclusive (after merge)
   - MinLength > MaxLength (after merge)
   - Custom constraint targets a property path that doesn't exist on the shape
3. All conflicts raise `InvalidOperationException` with a descriptive message including:
   - Which property path has the conflict
   - What the auto-derived constraint was
   - What the custom constraint attempted

**Files**: `src/Frank.Validation/ShapeMerger.fs`
**Notes**: Conflict detection is conservative -- only flag direct contradictions. A custom `MaxInclusive 100` on a field with no auto-derived range is fine (no conflict). A custom `InValues ["X"]` on a field with auto-derived `InValues ["A", "B"]` is a conflict (empty intersection).

### Subtask T035 -- Implement SPARQL cross-field constraint support

**Purpose**: Support developer-supplied SPARQL-based constraints for cross-field validation (e.g., endDate > startDate).

**Steps**:
1. Handle `SparqlConstraint` in the shape graph builder (from WP03's `ShapeGraphBuilder`):

```fsharp
    // When building the shapes graph, add sh:sparql constraints
    | SparqlConstraint query ->
        // Add sh:sparql to the NodeShape
        let sparqlNode = g.CreateBlankNode()
        let shSparql = g.CreateUriNode(UriFactory.Create("http://www.w3.org/ns/shacl#sparql"))
        g.Assert(shapeNode, shSparql, sparqlNode)

        let shSelect = g.CreateUriNode(UriFactory.Create("http://www.w3.org/ns/shacl#select"))
        g.Assert(sparqlNode, shSelect, g.CreateLiteralNode(query))

        let shMessage = g.CreateUriNode(UriFactory.Create("http://www.w3.org/ns/shacl#message"))
        g.Assert(sparqlNode, shMessage,
            g.CreateLiteralNode("Cross-field constraint violation"))
```

2. Validate SPARQL syntax at startup:
   - Parse the SPARQL query using dotNetRdf's SPARQL parser
   - If invalid syntax, raise `InvalidOperationException` with the parse error
3. SPARQL constraints are stored on the `ShaclShape` (not `PropertyShape`) since they span multiple properties.

**Files**: `src/Frank.Validation/ShapeMerger.fs`, `src/Frank.Validation/Validator.fs` (or `ShapeGraphBuilder.fs`)
**Notes**: SPARQL-based SHACL constraints use ASK queries that return bindings for focus nodes violating the constraint. Example: `SELECT $this WHERE { $this <urn:frank:property:startDate> ?start . $this <urn:frank:property:endDate> ?end . FILTER (?end <= ?start) }`. dotNetRdf's SHACL engine evaluates these automatically during `ShapesGraph.Validate()`.

**Implementation note**: SPARQL constraints must be ASK queries that return true when the constraint is satisfied. Syntax is validated at startup; invalid SPARQL raises `InvalidOperationException`. Example:
```fsharp
customConstraint "DateRange" (SparqlConstraint """
  ASK WHERE {
    $this :startDate ?start ; :endDate ?end .
    FILTER (?end > ?start)
  }
""")
```

### Subtask T036 -- Create `ShapeMergerTests.fs`

**Purpose**: Verify constraint merging and conflict detection.

**Steps**:
1. Create `test/Frank.Validation.Tests/ShapeMergerTests.fs`
2. Write tests:

**a. Additive pattern constraint**:
- Base shape: `Email` field with `XsdString`, no pattern
- Custom: `PatternConstraint @"^[^@]+@[^@]+$"`
- Verify merged shape has pattern on Email property

**b. Additive MinInclusive**:
- Base shape: `Age` field with `XsdInteger`
- Custom: `MinInclusiveConstraint 0`
- Verify merged shape has minInclusive on Age

**c. InValues intersection**:
- Base shape: `Status` field with `InValues ["A"; "B"; "C"]`
- Custom: `InValuesConstraint ["B"; "C"; "D"]`
- Verify merged shape has `InValues ["B"; "C"]` (intersection)

**d. Conflict: empty InValues intersection**:
- Base shape: `Status` with `InValues ["A"; "B"]`
- Custom: `InValuesConstraint ["X"; "Y"]`
- Verify `InvalidOperationException` with descriptive message

**e. Conflict: MinInclusive > MaxInclusive**:
- Custom: `MinInclusiveConstraint 100` and `MaxInclusiveConstraint 50`
- Verify `InvalidOperationException`

**f. Non-existent property path**:
- Custom constraint targets property "nonexistent"
- Verify appropriate error (property not found on shape)

**g. Multiple custom constraints on same property**:
- `MinInclusiveConstraint 0` and `MaxInclusiveConstraint 150` on `Age`
- Verify both applied

**h. SPARQL constraint added to shape**:
- Custom: `SparqlConstraint "SELECT $this WHERE { ... }"`
- Verify constraint stored on the shape (validate graph construction in integration)

**i. Both auto-derived and custom constraints evaluated**:
- Base: required string field
- Custom: pattern constraint
- Data missing the field: minCount violation
- Data with wrong pattern: pattern violation
- Both violations appear in report

**Files**: `test/Frank.Validation.Tests/ShapeMergerTests.fs`
**Parallel?**: Yes -- depends on T032-T035 but can be scaffolded once T032 is done.
**Validation**: `dotnet test test/Frank.Validation.Tests/` passes with all tests green.

---

## Test Strategy

- Run `dotnet build` to verify compilation of ShapeMerger.fs
- Run `dotnet test` for all merger and conflict detection tests
- Verify User Story 5 acceptance scenarios from spec.md (all 4 scenarios)

---

## Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| Conflict detection false positives | Conservative: only flag direct contradictions (empty intersection, inverted ranges), not semantic tensions |
| SPARQL constraint complexity | Limit to well-formed ASK/SELECT queries; validate syntax at startup |
| PropertyShape type needs extension for MinLength/MaxLength | Store additional constraints in a separate list on PropertyShape or use a map; keep core type stable |
| Pattern combination (AND semantics) complexity | Use SHACL's native multiple sh:pattern support (each pattern is a separate assertion) rather than regex concatenation |

---

## Review Guidance

- Verify custom constraints are additive only (never weaken)
- Verify conflict detection raises `InvalidOperationException` with descriptive messages
- Verify InValues intersection logic is correct
- Verify SPARQL syntax validation at startup
- Verify merge produces new immutable ShaclShape (no mutation)
- Verify Constitution VII (no silent exception swallowing): conflicts propagate as exceptions
- Run `dotnet build Frank.sln` and `dotnet test` to confirm green

---

## Activity Log

- 2026-03-07T00:00:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-15T18:49:26Z – claude-opus – shell_pid=24783 – lane=doing – Assigned agent via workflow command
- 2026-03-15T19:06:52Z – claude-opus – shell_pid=24783 – lane=for_review – Ready for review: ShapeMerger with additive merging, conflict detection, SPARQL support
- 2026-03-15T19:07:45Z – claude-opus – shell_pid=26354 – lane=doing – Started review via workflow command
- 2026-03-15T19:08:28Z – claude-opus – shell_pid=26354 – lane=done – Review passed: ShapeMerger correctly implements additive-only merging, conflict detection, SPARQL support. All 25 tests pass. Types cleanly extended. ShapeGraphBuilder emits all new fields.
