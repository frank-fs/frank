---
work_package_id: WP03
title: Serializer and Generator
lane: done
dependencies:
- WP01
subtasks:
- T011
- T012
- T013
phase: Phase 2 - Core Implementation
assignee: ''
agent: ''
shell_pid: ''
review_status: approved
reviewed_by: claude-opus
history:
- timestamp: '2026-03-16T19:13:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs: [FR-011, FR-012, FR-013, FR-014, FR-015, FR-016, FR-017]
---

# Work Package Prompt: WP03 -- Serializer and Generator

## Review Feedback

*[This section is empty initially. Reviewers will populate it if the work is returned from review. If you see feedback here, treat each item as a must-do before completion.]*

---

## Objectives & Success Criteria

- New `Serializer.fs` exists with `serialize: StatechartDocument -> string` that produces valid smcat text
- `Generator.fs` returns `Result<StatechartDocument, GeneratorError>` from `StateMachineMetadata`
- `Mapper.fs` is deleted from the filesystem
- The serializer handles all smcat constructs: states with types, activities, attributes, composite states, transitions with labels
- The serializer produces sensible defaults when `SmcatAnnotation` entries are absent
- The generator follows the `Wsd.Generator` pattern exactly

## Context & Constraints

- **Spec**: FR-011 through FR-017 (Serializer and Generator requirements)
- **Plan**: DD-002 (Serializer pattern), DD-003 (Generator refactoring)
- **WSD reference Serializer**: `src/Frank.Statecharts/Wsd/Serializer.fs` -- structural reference (but smcat output format is very different from WSD)
- **WSD reference Generator**: `src/Frank.Statecharts/Wsd/Generator.fs` -- follow this pattern exactly
- **Current Generator**: `src/Frank.Statecharts/Smcat/Generator.fs` -- contains `formatLabel`, `formatTransition`, `needsQuoting`, `quoteName` helpers that move to Serializer
- **Current Mapper**: `src/Frank.Statecharts/Smcat/Mapper.fs` -- contains `fromAnnotation`, `fromStateKind`, `fromStateActivities`, `fromAstPosition` helpers useful for Serializer
- **Shared AST**: `src/Frank.Statecharts/Ast/Types.fs`
- **Prerequisite**: WP01 and WP02 must be complete

## Implementation Command

```bash
spec-kitty implement WP03 --base WP02
```

## Subtasks & Detailed Guidance

### Subtask T011 -- Create Serializer.fs

**Purpose**: Provide `serialize: StatechartDocument -> string` that converts a `StatechartDocument` (possibly with `SmcatAnnotation` entries) into valid smcat text output.

**Steps**:

1. Create `src/Frank.Statecharts/Smcat/Serializer.fs`
2. Module declaration:
   ```fsharp
   module internal Frank.Statecharts.Smcat.Serializer

   open Frank.Statecharts.Ast
   ```

3. **Helper: `needsQuoting`** (moved from Generator.fs, smcat-specific):
   ```fsharp
   /// Determines whether an smcat name requires quoting.
   /// smcat allows alphanumeric, underscore, dot, and hyphen without quoting.
   let private needsQuoting (name: string) =
       name
       |> Seq.exists (fun c ->
           not (System.Char.IsLetterOrDigit c || c = '_' || c = '.' || c = '-'))
   ```

4. **Helper: `quoteName`**:
   ```fsharp
   let private quoteName (name: string) =
       if needsQuoting name then
           sprintf "\"%s\"" (name.Replace("\"", "\\\""))
       else
           name
   ```

5. **Helper: Extract smcat annotations from a node's annotation list**:
   ```fsharp
   /// Extract color attribute from SmcatAnnotation entries.
   let private extractColor (annotations: Annotation list) : string option =
       annotations
       |> List.tryPick (function
           | SmcatAnnotation(SmcatColor c) -> Some c
           | _ -> None)

   /// Extract label attribute from SmcatAnnotation entries.
   let private extractLabel (annotations: Annotation list) : string option =
       annotations
       |> List.tryPick (function
           | SmcatAnnotation(SmcatStateLabel l) -> Some l
           | _ -> None)

   /// Extract non-standard attributes from SmcatAnnotation entries.
   let private extractCustomAttributes (annotations: Annotation list) : (string * string) list =
       annotations
       |> List.choose (function
           | SmcatAnnotation(SmcatActivity(kind, body)) -> Some (kind, body)
           | _ -> None)
   ```

6. **Helper: Map StateKind to smcat type keyword** (for explicit type attributes):
   ```fsharp
   let private stateKindToSmcatType (kind: StateKind) : string option =
       match kind with
       | StateKind.Regular -> None   // Default, don't emit
       | StateKind.Initial -> Some "initial"
       | StateKind.Final -> Some "final"
       | StateKind.ShallowHistory -> Some "history"
       | StateKind.DeepHistory -> Some "deep.history"
       | StateKind.Choice -> Some "choice"
       | StateKind.ForkJoin -> Some "forkjoin"
       | StateKind.Terminate -> Some "terminate"
       | StateKind.Parallel -> None  // No smcat equivalent
   ```

7. **Helper: Format transition label** (moved from Generator.fs):
   ```fsharp
   let private formatLabel (event: string option) (guard: string option) (action: string option) : string option =
       match event, guard, action with
       | None, None, None -> None
       | Some e, None, None -> Some e
       | Some e, Some g, None -> Some(sprintf "%s [%s]" e g)
       | Some e, None, Some a -> Some(sprintf "%s / %s" e a)
       | Some e, Some g, Some a -> Some(sprintf "%s [%s] / %s" e g a)
       | None, Some g, None -> Some(sprintf "[%s]" g)
       | None, Some g, Some a -> Some(sprintf "[%s] / %s" g a)
       | None, None, Some a -> Some(sprintf "/ %s" a)
   ```

8. **Helper: Serialize attributes block**:
   ```fsharp
   let private serializeAttributes (annotations: Annotation list) : string =
       let parts = ResizeArray<string>()

       // Type attribute is NOT stored in annotations (it's on StateNode.Kind)
       // Color
       match extractColor annotations with
       | Some c -> parts.Add(sprintf "color=\"%s\"" c)
       | None -> ()

       // Label
       match extractLabel annotations with
       | Some l -> parts.Add(sprintf "label=\"%s\"" l)
       | None -> ()

       // Custom attributes
       for (key, value) in extractCustomAttributes annotations do
           parts.Add(sprintf "%s=\"%s\"" key value)

       if parts.Count > 0 then
           sprintf " [%s]" (System.String.Join(" ", parts))
       else
           ""
   ```

9. **Helper: Serialize activities**:
   ```fsharp
   let private serializeActivities (activities: StateActivities option) : string =
       match activities with
       | None -> ""
       | Some a ->
           let parts = ResizeArray<string>()
           for entry in a.Entry do
               parts.Add(sprintf "entry/ %s" entry)
           for exit in a.Exit do
               parts.Add(sprintf "exit/ %s" exit)
           for doAct in a.Do do
               parts.Add(sprintf "...%s" doAct)
           if parts.Count > 0 then
               sprintf ": %s" (System.String.Join(" ", parts))
           else
               ""
   ```

10. **Main serialization -- recursive for composite states**:
    ```fsharp
    let rec private serializeElements (sb: System.Text.StringBuilder) (elements: StatechartElement list) (indent: string) : unit =
        for element in elements do
            match element with
            | StateDecl s ->
                sb.Append(indent) |> ignore
                sb.Append(quoteName s.Identifier) |> ignore

                // Activities
                let actStr = serializeActivities s.Activities
                sb.Append(actStr) |> ignore

                // Attributes
                let attrStr = serializeAttributes s.Annotations
                sb.Append(attrStr) |> ignore

                // Composite children
                match s.Children with
                | [] ->
                    sb.Append(";\n") |> ignore
                | children ->
                    sb.Append(" {\n") |> ignore
                    // Serialize child states as state declarations
                    for child in children do
                        serializeElements sb [StateDecl child] (indent + "  ")
                    // Find transitions that reference child state names
                    let childNames = children |> List.map (fun c -> c.Identifier) |> Set.ofList
                    for el in elements do
                        match el with
                        | TransitionElement t when
                            childNames.Contains(t.Source) ||
                            (t.Target |> Option.map childNames.Contains |> Option.defaultValue false) ->
                            serializeElements sb [TransitionElement t] (indent + "  ")
                        | _ -> ()
                    sb.Append(indent) |> ignore
                    sb.Append("};\n") |> ignore

            | TransitionElement t ->
                // Only serialize if not already handled by composite parent
                // (This check happens at the caller level for composites)
                sb.Append(indent) |> ignore
                sb.Append(quoteName t.Source) |> ignore
                sb.Append(" => ") |> ignore
                match t.Target with
                | Some target -> sb.Append(quoteName target) |> ignore
                | None -> ()
                let label = formatLabel t.Event t.Guard t.Action
                match label with
                | Some l ->
                    sb.Append(": ") |> ignore
                    sb.Append(l) |> ignore
                | None -> ()
                sb.Append(";\n") |> ignore

            | NoteElement _ -> ()  // smcat doesn't have note syntax
            | GroupElement _ -> () // smcat doesn't have group syntax
            | DirectiveElement _ -> () // smcat doesn't have directives
    ```

    **Note**: The composite state serialization is the trickiest part. The approach above tries to match transitions with child state names, but this may be fragile. An alternative simpler approach: since the parser (WP02) hoists composite-internal transitions to the parent elements list, the serializer can group them by checking if they reference children of a composite state. However, the recommended approach is **to serialize elements as a flat list**, letting the state declarations with `Children` indicate nesting, and NOT trying to filter transitions into/out of composites. The serializer should just:
    - For `StateDecl` with children: emit `name { ... };` with recursive serialization of children
    - For `TransitionElement`: emit as-is at the current indent level
    - At the top level, skip transitions whose source/target are child states (since they'll be emitted inside the composite block)

    **Simplest correct approach**: Serialize the `StatechartDocument.Elements` list. For each `StateDecl` that has `Children`, recurse. For transitions, emit at current level. Trust that the parser organized elements correctly.

    Actually, re-thinking: the **simplest working approach** that produces valid smcat from any `StatechartDocument`:
    - Emit all `StateDecl` nodes (with their children recursively)
    - Emit all `TransitionElement` nodes
    - Handle composite states by recursing into `Children`
    - For composite states, transitions between children should ideally appear inside the `{ }` block

    Given the complexity, here is the pragmatic implementation:

    ```fsharp
    /// Serialize a single state node (handles composites recursively).
    let rec private serializeState (sb: System.Text.StringBuilder) (indent: string) (node: StateNode) (siblingTransitions: TransitionEdge list) : unit =
        sb.Append(indent) |> ignore
        sb.Append(quoteName node.Identifier) |> ignore
        sb.Append(serializeActivities node.Activities) |> ignore
        sb.Append(serializeAttributes node.Annotations) |> ignore

        match node.Children with
        | [] -> sb.Append(";\n") |> ignore
        | children ->
            sb.Append(" {\n") |> ignore
            let childNames = children |> List.map (fun c -> c.Identifier) |> Set.ofList
            // Serialize children and their internal transitions
            let innerIndent = indent + "  "
            let childTransitions =
                siblingTransitions
                |> List.filter (fun t ->
                    childNames.Contains(t.Source) ||
                    (t.Target |> Option.map childNames.Contains |> Option.defaultValue false))
            for child in children do
                serializeState sb innerIndent child childTransitions
            for t in childTransitions do
                serializeTransition sb innerIndent t
            sb.Append(indent) |> ignore
            sb.Append("};\n") |> ignore

    and private serializeTransition (sb: System.Text.StringBuilder) (indent: string) (t: TransitionEdge) : unit =
        sb.Append(indent) |> ignore
        sb.Append(quoteName t.Source) |> ignore
        sb.Append(" => ") |> ignore
        match t.Target with
        | Some target -> sb.Append(quoteName target) |> ignore
        | None -> ()
        match formatLabel t.Event t.Guard t.Action with
        | Some l ->
            sb.Append(": ") |> ignore
            sb.Append(l) |> ignore
        | None -> ()
        sb.Append(";\n") |> ignore
    ```

11. **Public API**:
    ```fsharp
    /// Serialize a StatechartDocument to valid smcat text.
    let serialize (document: StatechartDocument) : string =
        let sb = System.Text.StringBuilder()
        let allStates =
            document.Elements
            |> List.choose (function StateDecl s -> Some s | _ -> None)
        let allTransitions =
            document.Elements
            |> List.choose (function TransitionElement t -> Some t | _ -> None)

        // Collect all child state names (to avoid double-emitting transitions)
        let rec collectChildNames (nodes: StateNode list) =
            nodes |> List.collect (fun n -> n.Identifier :: collectChildNames n.Children)
        let childNames =
            allStates
            |> List.collect (fun s -> collectChildNames s.Children)
            |> Set.ofList

        // Emit states (with composite blocks)
        for s in allStates do
            serializeState sb "" s allTransitions

        // Emit top-level transitions (those not inside composite blocks)
        for t in allTransitions do
            let isChild =
                childNames.Contains(t.Source) ||
                (t.Target |> Option.map childNames.Contains |> Option.defaultValue false)
            if not isChild then
                serializeTransition sb "" t

        // Trim trailing newline for clean output
        let result = sb.ToString().TrimEnd('\n')
        result
    ```

**Files**: `src/Frank.Statecharts/Smcat/Serializer.fs` (new file)

**Validation**:
- [ ] `serialize` produces valid smcat text from a `StatechartDocument`
- [ ] Composite states emit `{ }` blocks with children
- [ ] Attributes are emitted as `[key="value"]`
- [ ] Activities are emitted as `: entry/ ... exit/ ...`
- [ ] Transitions use `=>` arrow syntax with label format `event [guard] / action`
- [ ] Document with no `SmcatAnnotation` entries produces valid minimal output
- [ ] Names with special characters are quoted

### Subtask T012 -- Refactor Generator.fs to return `Result<StatechartDocument, GeneratorError>`

**Purpose**: The generator should produce a `StatechartDocument` AST from `StateMachineMetadata`, following the `Wsd.Generator` pattern exactly.

**Steps**:

1. Open `src/Frank.Statecharts/Smcat/Generator.fs`
2. Completely rewrite to follow `src/Frank.Statecharts/Wsd/Generator.fs` pattern:

   ```fsharp
   module internal Frank.Statecharts.Smcat.Generator

   open Frank.Statecharts
   open Frank.Statecharts.Ast

   /// Options controlling smcat generation behavior.
   type GenerateOptions =
       { ResourceName: string }

   /// Error cases from the smcat generator.
   type GeneratorError =
       | UnrecognizedMachineType of typeName: string

   /// Synthetic source position for all generated AST nodes.
   let private syntheticPos : SourcePosition = { Line = 0; Column = 0 }

   /// Check whether the boxed Machine is a StateMachine<_,_,_> record.
   let private isStateMachineType (machine: obj) : bool =
       let t = machine.GetType()
       t.IsGenericType && t.GetGenericTypeDefinition().Name.StartsWith("StateMachine`")

   /// Generate a StatechartDocument AST from StateMachineMetadata.
   let generate (options: GenerateOptions) (metadata: StateMachineMetadata) : Result<StatechartDocument, GeneratorError> =
       if not (isStateMachineType metadata.Machine) then
           Error(UnrecognizedMachineType(metadata.Machine.GetType().FullName))
       else

       let stateNames = metadata.StateHandlerMap |> Map.toList |> List.map fst
       let others =
           stateNames
           |> List.filter (fun s -> s <> metadata.InitialStateKey)
           |> List.sort
       let orderedStates = metadata.InitialStateKey :: others

       // State declarations
       let stateElements =
           orderedStates
           |> List.map (fun name ->
               StateDecl
                   { Identifier = name
                     Label = None
                     Kind = Regular
                     Children = []
                     Activities = None
                     Position = Some syntheticPos
                     Annotations = [] })

       // Self-transitions for each (state, httpMethod) handler pair
       let transitionElements =
           orderedStates
           |> List.collect (fun stateName ->
               match Map.tryFind stateName metadata.StateHandlerMap with
               | Some handlers ->
                   handlers
                   |> List.map (fun (httpMethod, _) ->
                       TransitionElement
                           { Source = stateName
                             Target = Some stateName
                             Event = Some httpMethod
                             Guard = None
                             Action = None
                             Parameters = []
                             Position = Some syntheticPos
                             Annotations = [] })
               | None -> [])

       // Final state transitions
       let finalTransitions =
           orderedStates
           |> List.collect (fun stateName ->
               match Map.tryFind stateName metadata.StateMetadataMap with
               | Some info when info.IsFinal ->
                   [ TransitionElement
                         { Source = stateName
                           Target = Some "final"
                           Event = None
                           Guard = None
                           Action = None
                           Parameters = []
                           Position = Some syntheticPos
                           Annotations = [] } ]
               | _ -> [])

       let allElements = stateElements @ transitionElements @ finalTransitions

       Ok
           { Title = Some options.ResourceName
             InitialStateId = Some metadata.InitialStateKey
             Elements = allElements
             DataEntries = []
             Annotations = [] }
   ```

3. **Delete** the old helper functions:
   - `needsQuoting` (moved to Serializer)
   - `quoteName` (moved to Serializer)
   - `formatLabel` (moved to Serializer)
   - `formatTransition` (moved to Serializer)
   - `generateTo` (no longer needed -- callers use `generate` + `Serializer.serialize`)
   - Old `generate` function (replaced by new one)

4. Note: The old `generate` returned `string`. The new one returns `Result<StatechartDocument, GeneratorError>`. Callers that need text output will chain: `generate |> Result.map Serializer.serialize`.

**Files**: `src/Frank.Statecharts/Smcat/Generator.fs`

**Validation**:
- [ ] `generate` returns `Result<StatechartDocument, GeneratorError>`
- [ ] Invalid Machine type returns `Error(UnrecognizedMachineType ...)`
- [ ] States are ordered: initial first, others alphabetically
- [ ] Self-transitions created for each (state, httpMethod) pair
- [ ] Final state transitions emit `Target = Some "final"`
- [ ] `formatLabel`, `formatTransition`, `needsQuoting`, `quoteName`, `generateTo` are deleted

### Subtask T013 -- Delete Mapper.fs

**Purpose**: `Mapper.fs` is no longer needed -- the parser produces shared AST directly and the serializer works from `StatechartDocument`.

**Steps**:

1. Delete the file: `src/Frank.Statecharts/Smcat/Mapper.fs`
2. Verify the `fsproj` already has it removed (done in WP01, T003)
3. Verify no other files reference `Smcat.Mapper` -- grep for `Smcat.Mapper` across the codebase

**Files**: `src/Frank.Statecharts/Smcat/Mapper.fs` (deleted)

**Validation**:
- [ ] File does not exist on disk
- [ ] No compile entry in fsproj
- [ ] No references to `Smcat.Mapper` in any `.fs` file

## Risks & Mitigations

- **Risk**: Serializer composite state handling may produce incorrect nesting. **Mitigation**: Test with the golden file composite state example (`goldenCompositeStates` in RoundTripTests.fs).
- **Risk**: Generator `formatLabel` tests in `GeneratorTests.fs` reference the old internal helper. **Mitigation**: Those tests will be updated in WP04, but the `formatLabel` function is now in Serializer. If tests need it, they can reference `Serializer.formatLabel` (but it should be private -- tests may need restructuring).
- **Risk**: `generateTo` callers exist elsewhere. **Mitigation**: Grep for `generateTo` to find any callers; update them to use `generate |> Result.map Serializer.serialize`.

## Review Guidance

- Verify Serializer produces valid smcat output by manually testing a few cases
- Verify Generator follows WSD.Generator pattern (compare side-by-side)
- Verify Mapper.fs is fully deleted with no dangling references
- Check that `needsQuoting` in Serializer allows dots and hyphens (smcat-specific, unlike WSD)

## Activity Log

- 2026-03-16T19:13:00Z -- system -- lane=planned -- Prompt created.
- 2026-03-16T21:30:00Z -- claude-opus -- lane=done -- Review approved. All three subtasks pass: Serializer.fs created with correct serialize function, Generator.fs refactored to return Result<StatechartDocument, GeneratorError> following WSD pattern, Mapper.fs deleted with no dangling references. Build succeeds across net8.0/net9.0/net10.0 with 0 warnings. Noted: stateKindToSmcatType helper omitted (non-blocking, round-trip tests will surface if needed).
