---
work_package_id: "WP03"
title: "Migrate JsonGenerator to Shared AST"
lane: "done"
dependencies: ["WP01", "WP02"]
requirement_refs:
  - "FR-012"
  - "FR-013"
  - "FR-014"
  - "FR-015"
  - "FR-016"
  - "FR-017"
subtasks:
  - "T009"
  - "T010"
  - "T011"
  - "T012"
assignee: ""
agent: ""
shell_pid: ""
review_status: "approved"
reviewed_by: "claude-opus"
history:
  - timestamp: "2026-03-16T19:13:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP03 -- Migrate JsonGenerator to Shared AST

## Important: Review Feedback Status

- **Has review feedback?**: Check the `review_status` field above.
- **You must address all feedback** before your work is complete.

---

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review.]*

---

## Objectives & Success Criteria

- `generateAlpsJson` accepts `StatechartDocument` instead of `AlpsDocument` and produces valid ALPS JSON.
- The generator reconstructs the ALPS descriptor hierarchy from `StateNode` elements, `TransitionEdge` elements, and `AlpsMeta` annotations.
- Shared transitions (referenced via `AlpsDescriptorHref`) are deduplicated: one top-level descriptor, href-only references inside referencing states.
- Default values are applied when ALPS-specific annotations are missing: `unsafe` for transition type, `"1.0"` for version.
- Generated JSON can be re-parsed by the migrated parser (from WP02) to produce a structurally equal `StatechartDocument`.
- `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds.

## Context & Constraints

- **Spec**: `kitty-specs/023-alps-shared-ast-migration/spec.md` (FR-012 through FR-017, User Story 3)
- **Plan**: `kitty-specs/023-alps-shared-ast-migration/plan.md` (Generator Design: Hierarchy Reconstruction)
- **Data Model**: `kitty-specs/023-alps-shared-ast-migration/data-model.md` (annotation ordering rules)
- **Research**: `kitty-specs/023-alps-shared-ast-migration/research.md` (Section 5: Risks, D-004: shared transition deduplication)
- **Key Decision D-004**: Parser emits one `TransitionEdge` per referencing state; generator detects duplicates by matching event name + `AlpsDescriptorHref` annotation and emits single top-level descriptor + href references.
- **Reference**: The current `Mapper.fromStatechartDocument` (Mapper.fs lines 218-286) shows the basic pattern of converting `StatechartDocument` to ALPS descriptors, but does NOT handle deduplication, annotations, or data descriptors. The new generator must handle all of these.

## Implementation Command

```bash
spec-kitty implement WP03 --base WP02
```

## Subtasks & Detailed Guidance

### Subtask T009 -- Rewrite `generateAlpsJson` to accept `StatechartDocument`

- **Purpose**: The generator must consume the shared AST type that the parser produces, completing the migration.

- **File**: `src/Frank.Statecharts/Alps/JsonGenerator.fs`

- **Steps**:
  1. Change the module opens:
     ```fsharp
     module internal Frank.Statecharts.Alps.JsonGenerator

     open System.IO
     open System.Text
     open System.Text.Json
     open Frank.Statecharts.Ast
     ```
     Remove `open Frank.Statecharts.Alps.Types`.

  2. Change the function signature:
     ```fsharp
     let generateAlpsJson (doc: StatechartDocument) : string =
     ```

  3. Implement helper functions to extract data from `StatechartDocument`:
     ```fsharp
     /// Extract all StateNodes from elements.
     let private extractStateNodes (doc: StatechartDocument) : StateNode list =
         doc.Elements |> List.choose (fun el ->
             match el with StateDecl s -> Some s | _ -> None)

     /// Extract all TransitionEdges from elements.
     let private extractTransitionEdges (doc: StatechartDocument) : TransitionEdge list =
         doc.Elements |> List.choose (fun el ->
             match el with TransitionElement t -> Some t | _ -> None)
     ```

  4. Implement the main generation logic:
     - Extract version from `AlpsVersion` annotation on `doc.Annotations` (or default to `"1.0"`).
     - Extract document-level documentation from `AlpsDocumentation` annotation.
     - Extract document-level links from `AlpsLink` annotations.
     - Extract document-level extensions from `AlpsExtension` annotations.
     - Extract data descriptors from `AlpsDataDescriptor` annotations.
     - Get states and transitions, group transitions by source.
     - Identify shared transitions (via deduplication logic in T010).
     - Build JSON using `Utf8JsonWriter`.

  5. **JSON structure** (output ordering):
     ```json
     {
       "alps": {
         "version": "...",
         "doc": { ... },
         "descriptor": [
           // data descriptors first (from AlpsDataDescriptor annotations)
           // then state descriptors (from StateNode elements)
           // then shared transition descriptors (from deduplication)
         ],
         "link": [ ... ],
         "ext": [ ... ]
       }
     }
     ```

- **Notes**: The `MemoryStream` + `Utf8JsonWriter` pattern is preserved from the current generator. The `JsonWriterOptions(Indented = true)` setting is preserved.

### Subtask T010 -- Implement shared transition deduplication (D-004)

- **Purpose**: In the tic-tac-toe example, `viewGame` is a top-level transition descriptor referenced via `{ "href": "#viewGame" }` from XTurn, OTurn, Won, and Draw. The parser creates a `TransitionEdge` for each referencing state (all with `AlpsDescriptorHref "#viewGame"` annotation). The generator must detect these shared transitions and emit:
  - One top-level `viewGame` descriptor (using the full transition data).
  - `{ "href": "#viewGame" }` inside each referencing state.

- **File**: `src/Frank.Statecharts/Alps/JsonGenerator.fs`

- **Steps**:
  1. Identify shared transitions: group all transitions by event name. For each group, check if any transition has an `AlpsDescriptorHref` annotation. If so, this is a shared transition.
     ```fsharp
     let private tryGetDescriptorHref (t: TransitionEdge) : string option =
         t.Annotations |> List.tryPick (fun ann ->
             match ann with
             | AlpsAnnotation(AlpsDescriptorHref href) -> Some href
             | _ -> None)

     let private isSharedTransition (t: TransitionEdge) : bool =
         tryGetDescriptorHref t |> Option.isSome
     ```

  2. Collect shared transition event names:
     ```fsharp
     let sharedTransitions =
         transitions
         |> List.filter isSharedTransition
         |> List.groupBy (fun t -> t.Event)
         |> List.map (fun (eventName, group) -> eventName, group |> List.head)
         // Use head to get the canonical transition data
     ```

  3. When writing state descriptors, for each transition from that state:
     - If it is a shared transition (has `AlpsDescriptorHref`): emit `{ "href": "#eventName" }` instead of the full transition descriptor.
     - If it is a regular transition: emit the full transition descriptor with type, rt, doc, ext, etc.

  4. After all state descriptors, emit shared transition descriptors as top-level descriptors:
     ```fsharp
     // For each shared transition, write a top-level descriptor
     for (eventName, canonical) in sharedTransitions do
         writeTransitionDescriptor writer canonical
     ```

- **Notes**:
  - The `AlpsDescriptorHref` annotation contains the original href value (e.g., `"#viewGame"`).
  - When emitting the href-only reference inside a state, use the href value from the annotation.
  - When emitting the top-level shared transition descriptor, strip the `AlpsDescriptorHref` annotation (it is reconstruction metadata, not part of the output).

### Subtask T011 -- Implement default values

- **Purpose**: When generating ALPS JSON from a `StatechartDocument` that has no ALPS-specific annotations (e.g., produced by a WSD parser), the generator must use sensible defaults.

- **File**: `src/Frank.Statecharts/Alps/JsonGenerator.fs`

- **Steps**:
  1. **Version default** (FR-017): If no `AlpsAnnotation(AlpsVersion _)` annotation is on the document, use `"1.0"`:
     ```fsharp
     let version =
         doc.Annotations |> List.tryPick (fun a ->
             match a with AlpsAnnotation(AlpsVersion v) -> Some v | _ -> None)
         |> Option.defaultValue "1.0"
     ```

  2. **Transition type default** (FR-016): If no `AlpsAnnotation(AlpsTransitionType _)` annotation is on a transition, use `unsafe`:
     ```fsharp
     let transitionTypeStr (t: TransitionEdge) =
         t.Annotations |> List.tryPick (fun a ->
             match a with
             | AlpsAnnotation(AlpsTransitionType kind) ->
                 match kind with
                 | AlpsTransitionKind.Safe -> Some "safe"
                 | AlpsTransitionKind.Unsafe -> Some "unsafe"
                 | AlpsTransitionKind.Idempotent -> Some "idempotent"
             | _ -> None)
         |> Option.defaultValue "unsafe"
     ```

  3. **Empty optional fields**: Omit `doc`, `ext`, `link`, `descriptor` properties when they are empty/absent (same as current generator behavior).

### Subtask T012 -- Implement annotation-to-JSON reconstruction

- **Purpose**: Read `AlpsMeta` annotations from AST nodes and write the corresponding JSON elements.

- **File**: `src/Frank.Statecharts/Alps/JsonGenerator.fs`

- **Steps**:
  1. **Write documentation** from `AlpsDocumentation` annotation:
     ```fsharp
     let private writeDocAnnotation (writer: Utf8JsonWriter) (annotations: Annotation list) =
         annotations |> List.tryPick (fun a ->
             match a with AlpsAnnotation(AlpsDocumentation(fmt, value)) -> Some(fmt, value) | _ -> None)
         |> Option.iter (fun (fmt, value) ->
             writer.WritePropertyName("doc")
             writer.WriteStartObject()
             fmt |> Option.iter (fun f -> writer.WriteString("format", f))
             writer.WriteString("value", value)
             writer.WriteEndObject())
     ```

  2. **Write extensions** from `AlpsExtension` annotations (excluding guards which are in `TransitionEdge.Guard`):
     ```fsharp
     let private getExtAnnotations (annotations: Annotation list) =
         annotations |> List.choose (fun a ->
             match a with AlpsAnnotation(AlpsExtension(id, href, value)) -> Some(id, href, value) | _ -> None)
     ```
     For transitions, also add guard as an extension:
     ```fsharp
     // When writing a transition descriptor, prepend guard as ext if present
     let extElements =
         match t.Guard with
         | Some guard -> ("guard", None, Some guard) :: nonGuardExts
         | None -> nonGuardExts
     ```

  3. **Write links** from `AlpsLink` annotations:
     ```fsharp
     let private getLinkAnnotations (annotations: Annotation list) =
         annotations |> List.choose (fun a ->
             match a with AlpsAnnotation(AlpsLink(rel, href)) -> Some(rel, href) | _ -> None)
     ```

  4. **Write data descriptors** from `AlpsDataDescriptor` annotations:
     ```fsharp
     let private getDataDescriptors (annotations: Annotation list) =
         annotations |> List.choose (fun a ->
             match a with AlpsAnnotation(AlpsDataDescriptor(id, doc)) -> Some(id, doc) | _ -> None)
     ```
     Each data descriptor becomes a top-level semantic descriptor:
     ```json
     { "id": "position", "type": "semantic", "doc": { "format": "text", "value": "Board position (0-8)" } }
     ```

  5. **Write transition descriptors**: For each non-shared transition from a state:
     - Write `id` (from `Event`)
     - Write `type` (from `AlpsTransitionType` annotation or default `unsafe`)
     - Write `rt` (from `Target` with `#` prefix re-added for local references)
     - Write `doc` (from `AlpsDocumentation` annotation on the transition)
     - Write nested parameter descriptors (from `Parameters` list, each as `{ "href": "#paramName" }`)
     - Write `ext` (guard first, then other extensions from annotations)

  6. **Write parameter descriptors** from `TransitionEdge.Parameters`:
     ```fsharp
     if not t.Parameters.IsEmpty then
         writer.WritePropertyName("descriptor")
         writer.WriteStartArray()
         for param in t.Parameters do
             writer.WriteStartObject()
             writer.WriteString("href", "#" + param)
             writer.WriteEndObject()
         writer.WriteEndArray()
     ```

  7. **Re-add `#` prefix** to `rt` values for local references:
     ```fsharp
     let rtValue (target: string option) =
         target |> Option.map (fun t ->
             if t.StartsWith("http://") || t.StartsWith("https://") then t
             else "#" + t)
     ```

## Risks & Mitigations

- **Risk**: Shared transition deduplication logic is complex and easy to get wrong.
  - **Mitigation**: Test with the tic-tac-toe golden file which has `viewGame` referenced from 4 states. The generated JSON should have exactly one top-level `viewGame` descriptor and 4 `{ "href": "#viewGame" }` references inside state descriptors.
- **Risk**: Annotation ordering in output JSON does not match the original.
  - **Mitigation**: Follow ALPS convention: `version`, `doc`, `descriptor`, `link`, `ext` at root level. Within descriptors: `id`, `type`, `href`, `rt`, `doc`, `descriptor` (children), `ext`, `link`.
- **Risk**: `rt` prefix reconstruction for external URLs.
  - **Mitigation**: Only add `#` prefix for local references. External URLs (starting with `http://` or `https://`) are preserved as-is.

## Review Guidance

- Verify the generator produces valid ALPS JSON from a `StatechartDocument` parsed from both golden files.
- Verify shared transition deduplication works correctly (one top-level descriptor, href references inside states).
- Verify default values are applied when ALPS annotations are missing.
- Verify `rt` values have the `#` prefix re-added for local references but not for external URLs.
- Verify the generator output can be re-parsed by the migrated parser to produce a structurally equal `StatechartDocument`.

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T23:30:00Z -- claude-opus -- lane=done -- Review approved. All 4 subtasks (T009-T012) implemented correctly. Generator accepts StatechartDocument, reconstructs ALPS descriptor hierarchy, deduplicates shared transitions (D-004), applies defaults (FR-016/FR-017), and reconstructs all annotation types. Build passes net8.0/net9.0/net10.0 with 0 errors.
