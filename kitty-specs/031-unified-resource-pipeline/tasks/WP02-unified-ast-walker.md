---
work_package_id: WP02
title: Unified AST Walker
lane: "doing"
dependencies: [WP01]
base_branch: 031-unified-resource-pipeline-WP01
base_commit: bd0722e27d0fa12e9ab2ad5cdbbcb7b8e6d5fed6
created_at: '2026-03-19T03:24:17.681895+00:00'
subtasks:
- T006
- T007
- T008
- T009
- T010
- T011
- T012
phase: Phase 0 - Foundation
assignee: ''
agent: "claude-opus"
shell_pid: "17534"
review_status: ''
reviewed_by: ''
history:
- timestamp: '2026-03-19T02:15:00Z'
  lane: planned
  agent: system
  shell_pid: ''
  action: Prompt generated via /spec-kitty.tasks
requirement_refs:
- FR-001
- FR-002
- FR-003
- FR-004
---

# Work Package Prompt: WP02 -- Unified AST Walker

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
Use language identifiers in code blocks: ````fsharp`, ````xml`, ````bash`

---

## Implementation Command

Depends on WP01:

```bash
spec-kitty implement WP02 --base WP01
```

---

## KEY CONSTRAINT

**This is Option B: single unified walk, not composition of existing extractors.** The unified walker walks the syntax AST once, dispatching to extraction helpers based on CE type (`resource` vs `statefulResource`). It does NOT call `AstAnalyzer.walkExpr` and `StatechartSourceExtractor.walkExprForStateful` separately and merge results.

**If the single-walk approach proves significantly harder than expected, STOP and ask.** Do not silently fall back to Option A (composition). The user explicitly chose Option B (research R2: "Rejected -- preserves the split, walks AST twice, user explicitly said 'B or stop.'").

---

## Objectives & Success Criteria

1. Create `UnifiedExtractor.fs` module with a single-pass syntax AST walker that identifies both `resource` and `statefulResource` CEs.
2. Merge the typed AST walking from `TypeAnalyzer.collectEntities` and `StatechartSourceExtractor.findMachineBindings` into a single typed AST traversal.
3. Cross-reference syntax CEs with typed bindings to produce `UnifiedResource` records.
4. Compute `DerivedResourceFields` (orphan states, state-to-type mappings, type coverage).
5. Handle plain `resource` CEs (type info only, no statechart) alongside `statefulResource` CEs.
6. Write comparison tests proving the unified extractor produces identical output to the old extractors.

**Success**: `UnifiedExtractor.extract` takes a project path and returns `Async<Result<UnifiedResource list, StatechartError>>`. For every project, the `ExtractedStatechart` data inside each `UnifiedResource.Statechart` matches what `StatechartSourceExtractor.extract` would produce, and the type info matches what `TypeAnalyzer.analyzeTypes` + `AstAnalyzer.analyzeFiles` would produce.

---

## Context & Constraints

- **Spec**: `kitty-specs/031-unified-resource-pipeline/spec.md` (FR-001, FR-002, FR-003, FR-004)
- **Plan**: `kitty-specs/031-unified-resource-pipeline/plan.md` (project structure: `src/Frank.Cli.Core/Unified/`)
- **Research**: `kitty-specs/031-unified-resource-pipeline/research.md` (R2: single-pass walk strategy)
- **Data Model**: `kitty-specs/031-unified-resource-pipeline/data-model.md` (UnifiedResource, DerivedResourceFields)
- **Existing code to understand**:
  - `src/Frank.Cli.Core/Analysis/AstAnalyzer.fs` -- `walkExpr` finds `resource` CEs, extracts route + HTTP methods
  - `src/Frank.Cli.Core/Statechart/StatechartSourceExtractor.fs` -- `walkExprForStateful` finds `statefulResource` CEs, extracts route + machine name + forState handlers
  - `src/Frank.Cli.Core/Analysis/TypeAnalyzer.fs` -- `collectEntities` walks `FSharpEntity` trees for types; `analyzeTypes` is the entry point
  - `src/Frank.Cli.Core/Statechart/StatechartSourceExtractor.fs` -- `findMachineBindings` walks `FSharpEntity` trees for `StateMachine<'S,'E,'C>` bindings
  - `src/Frank.Cli.Core/Analysis/ProjectLoader.fs` -- `loadProject` returns `LoadedProject` with `ParsedFiles` and `CheckResults`
  - `src/Frank.Cli.Core/Commands/ExtractCommand.fs` -- current semantic extraction pipeline
  - `src/Frank.Cli.Core/Commands/StatechartExtractCommand.fs` -- current statechart extraction pipeline
- **Key insight**: Both `AstAnalyzer.walkExpr` and `StatechartSourceExtractor.walkExprForStateful` have identical traversal logic (Sequential, LetOrUse, App, Paren, Lambda, IfThenElse, Match, Tuple). The only difference is the pattern matching at the leaf: one looks for `SynExpr.Ident "resource"`, the other for `SynExpr.Ident "statefulResource"`. The unified walker dispatches to both extraction helpers at the same leaf.

---

## Subtasks & Detailed Guidance

### Subtask T006 -- Create `UnifiedExtractor.fs` module with single-pass syntax AST walker

- **Purpose**: Replace the two independent AST walkers with a single traversal that dispatches to extraction helpers based on CE type.
- **Steps**:
  1. Create `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs`.
  2. Module: `module Frank.Cli.Core.Unified.UnifiedExtractor`.
  3. Open necessary namespaces:

```fsharp
module Frank.Cli.Core.Unified.UnifiedExtractor

open System.IO
open FSharp.Compiler.Syntax
open FSharp.Compiler.Text
open FSharp.Compiler.Symbols
open FSharp.Compiler.CodeAnalysis
open Frank.Cli.Core.Analysis
open Frank.Cli.Core.Statechart
open Frank.Cli.Core.Statechart.StatechartError
```

  4. Define an intermediate result type for syntax-level findings:

```fsharp
/// What the unified syntax walker found -- either a plain resource or a stateful resource.
type private SyntaxResource =
    | PlainResource of AnalyzedResource
    | StatefulResource of StatechartSourceExtractor.SyntaxStatefulResource
```

Wait -- `SyntaxStatefulResource` is private in `StatechartSourceExtractor`. You need to either:
  - (a) Make the relevant extraction helpers public (just the `tryExtractStatefulResource` pattern), or
  - (b) Duplicate the statefulResource CE extraction logic in the unified walker.

**Recommended approach**: Duplicate the extraction logic inside `UnifiedExtractor.fs`. The unified walker is REPLACING the old walkers, not composing them. The duplication is temporary -- once the unified extractor is proven, the old extractors become dead code. Copy the helper functions (`tryExtractStateCaseName`, `extractHttpMethods`, `tryExtractForState`, `walkStatefulCeBody`, `tryExtractStatefulResource`) from `StatechartSourceExtractor.fs` into `UnifiedExtractor.fs` as private helpers. Similarly, copy `tryExtractResource` and `walkCeBody` from `AstAnalyzer.fs`.

  5. Implement the unified `walkExpr`:

```fsharp
type private SyntaxFinding =
    | FoundPlainResource of route: string * methods: HttpMethod list * name: string option * hasLinkedData: bool * file: string * line: int * col: int
    | FoundStatefulResource of route: string * machineName: string option * stateHandlers: ForStateInfo list

/// Single-pass expression walker that dispatches to both resource and statefulResource extraction.
let rec private walkExpr (file: string) (results: ResizeArray<SyntaxFinding>) (expr: SynExpr) =
    // Try statefulResource first (more specific pattern)
    match tryExtractStatefulResource expr with
    | Some sr ->
        results.Add(FoundStatefulResource(sr.RouteTemplate, sr.MachineName, sr.StateHandlers))
    | None ->
        // Try plain resource
        match tryExtractResource expr file with
        | Some ar ->
            results.Add(FoundPlainResource(ar.RouteTemplate, ar.HttpMethods, ar.Name, ar.HasLinkedData, file, ar.Location.Line, ar.Location.Column))
        | None ->
            // Continue walking -- identical traversal logic
            match expr with
            | SynExpr.App(funcExpr = f; argExpr = a) ->
                walkExpr file results f
                walkExpr file results a
            | SynExpr.Sequential(expr1 = e1; expr2 = e2) ->
                walkExpr file results e1
                walkExpr file results e2
            | SynExpr.LetOrUse(bindings = bindings; body = body) ->
                for binding in bindings do
                    match binding with
                    | SynBinding(expr = bindExpr) -> walkExpr file results bindExpr
                walkExpr file results body
            | SynExpr.Paren(expr = inner) -> walkExpr file results inner
            | SynExpr.Lambda(body = body) -> walkExpr file results body
            | SynExpr.ComputationExpr(expr = ceBody) -> walkExpr file results ceBody
            | SynExpr.IfThenElse(ifExpr = i; thenExpr = t; elseExpr = e) ->
                walkExpr file results i
                walkExpr file results t
                e |> Option.iter (walkExpr file results)
            | SynExpr.Match(expr = matchExpr; clauses = clauses) ->
                walkExpr file results matchExpr
                for clause in clauses do
                    match clause with
                    | SynMatchClause(resultExpr = re) -> walkExpr file results re
            | SynExpr.Tuple(exprs = exprs) ->
                for e in exprs do walkExpr file results e
            | SynExpr.ArrayOrList(exprs = exprs) ->
                for e in exprs do walkExpr file results e
            | _ -> ()
```

  6. Walk all declarations and parsed files (same pattern as both existing walkers):

```fsharp
let rec private walkDecl (file: string) (results: ResizeArray<SyntaxFinding>) (decl: SynModuleDecl) =
    match decl with
    | SynModuleDecl.Let(bindings = bindings) ->
        for binding in bindings do
            match binding with
            | SynBinding(expr = expr) -> walkExpr file results expr
    | SynModuleDecl.NestedModule(decls = decls) ->
        for d in decls do walkDecl file results d
    | SynModuleDecl.Expr(expr = expr) -> walkExpr file results expr
    | _ -> ()

let private findAllResources (parsedFiles: (string * ParsedInput) list) : SyntaxFinding list =
    let results = ResizeArray<SyntaxFinding>()
    for fileName, parsedInput in parsedFiles do
        match parsedInput with
        | ParsedInput.ImplFile(ParsedImplFileInput(contents = modules)) ->
            for m in modules do
                match m with
                | SynModuleOrNamespace(decls = decls) ->
                    for d in decls do walkDecl fileName results d
        | _ -> ()
    results |> Seq.toList
```

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs` (NEW)
- **Notes**:
  - The key insight is that `tryExtractStatefulResource` check goes first. If the expression is a `statefulResource` CE, we extract it and stop. Otherwise, try `tryExtractResource` for a plain `resource` CE. If neither matches, continue the generic traversal.
  - This eliminates the duplicate traversal logic that exists between `AstAnalyzer.walkExpr` and `StatechartSourceExtractor.walkExprForStateful`.
  - The `file` parameter is needed for `FoundPlainResource` source location tracking.

### Subtask T007 -- Merge syntax CE extraction helpers

- **Purpose**: Bring the CE extraction helper functions from both `AstAnalyzer` and `StatechartSourceExtractor` into the unified module.
- **Steps**:
  1. Copy these private helpers from `StatechartSourceExtractor.fs` into `UnifiedExtractor.fs`:
     - `httpMethodOf`
     - `tryExtractStateCaseName`
     - `extractHttpMethods` and `extractSingleMethod`
     - `ForStateInfo` type
     - `tryExtractForState`
     - `CeAccum` (the stateful version) and `walkStatefulCeBody`
     - `SyntaxStatefulResource` type
     - `tryExtractStatefulResource`

  2. Copy these private helpers from `AstAnalyzer.fs` into `UnifiedExtractor.fs`:
     - The CE body accumulator type (renaming to avoid collision with the stateful version)
     - `walkCeBody` (the resource version)
     - `tryExtractResource`

  3. Rename to avoid collisions:
     - `AstAnalyzer.CeAccum` -> `ResourceCeAccum`
     - `AstAnalyzer.walkCeBody` -> `walkResourceCeBody`
     - `StatechartSourceExtractor.CeAccum` -> `StatefulCeAccum`
     - `StatechartSourceExtractor.walkStatefulCeBody` -> `walkStatefulCeBody` (keep name, it's already distinct)

  4. The HTTP method parsing in `AstAnalyzer` uses `HttpMethod` DU (Get, Post, etc.) while `StatechartSourceExtractor` uses string ("GET", "POST"). Unify by using string methods in the unified walker (consistent with `HttpCapability.Method` in the unified model). Convert `AstAnalyzer.HttpMethod` DU to strings when building `FoundPlainResource`.

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs` (continued)
- **Parallel?**: No, this is part of the same file as T006.
- **Notes**:
  - Do NOT modify `AstAnalyzer.fs` or `StatechartSourceExtractor.fs` -- they remain as-is for the comparison tests in T012 and for backward compatibility of existing commands.
  - The duplication is intentional and temporary. Once the unified extractor is proven equivalent (T012), the old extractors become dead code candidates.

### Subtask T008 -- Merge typed AST walking

- **Purpose**: Combine `TypeAnalyzer.collectEntities` and `StatechartSourceExtractor.findMachineBindings` into a single typed AST traversal.
- **Steps**:
  1. Create a unified typed AST result type:

```fsharp
type private UnifiedTypedResult =
    { /// All analyzed types (records, DUs, enums)
      AnalyzedTypes: AnalyzedType list
      /// StateMachine bindings with state type info
      MachineBindings: TypedMachineBinding list }
```

  2. Copy `TypedMachineBinding` and `tryExtractStateMachineInfo` from `StatechartSourceExtractor.fs`.

  3. Implement a unified entity walker:

```fsharp
let rec private walkEntity (entity: FSharpEntity) : UnifiedTypedResult =
    // Nested entities
    let nested =
        try
            entity.NestedEntities
            |> Seq.map walkEntity
            |> Seq.fold (fun acc r ->
                { AnalyzedTypes = acc.AnalyzedTypes @ r.AnalyzedTypes
                  MachineBindings = acc.MachineBindings @ r.MachineBindings })
                { AnalyzedTypes = []; MachineBindings = [] }
        with :? System.InvalidOperationException ->
            { AnalyzedTypes = []; MachineBindings = [] }

    // Collect analyzed types (from TypeAnalyzer logic)
    let typeResult = collectEntityType entity

    // Collect machine bindings (from StatechartSourceExtractor logic)
    let machineBindings =
        try
            entity.MembersFunctionsAndValues
            |> Seq.choose (fun mfv ->
                if mfv.IsModuleValueOrMember && not mfv.IsMember then
                    tryExtractStateMachineInfo mfv.FullType
                    |> Option.map (fun cases ->
                        { BindingName = mfv.DisplayName
                          StateTypeCases = cases
                          InitialStateName = cases |> List.tryHead
                          GuardNames = [] })
                else None)
            |> Seq.toList
        with :? System.InvalidOperationException -> []

    { AnalyzedTypes = typeResult @ nested.AnalyzedTypes
      MachineBindings = machineBindings @ nested.MachineBindings }
```

  4. The `collectEntityType` helper extracts `AnalyzedType` from a single entity (mirrors `TypeAnalyzer.collectEntities` but for one entity without recursion):

```fsharp
let private collectEntityType (entity: FSharpEntity) : AnalyzedType list =
    // Same logic as TypeAnalyzer.collectEntities but non-recursive
    // (recursion is handled by walkEntity)
    let entityFullName =
        try Some entity.FullName with _ -> None
        |> Option.defaultValue entity.DisplayName

    if entity.DisplayName.StartsWith("<") then []
    elif entity.IsFSharpUnion then
        // ... (same as TypeAnalyzer)
        [ { FullName = entityFullName; ... } ]
    elif entity.IsFSharpRecord then
        // ... (same as TypeAnalyzer)
        [ { FullName = entityFullName; ... } ]
    elif entity.IsEnum then
        // ... (same as TypeAnalyzer)
        [ { FullName = entityFullName; ... } ]
    else []
```

  5. Entry point:

```fsharp
let private analyzeTypedAst (checkResults: FSharpCheckProjectResults) : UnifiedTypedResult =
    checkResults.AssemblySignature.Entities
    |> Seq.map walkEntity
    |> Seq.fold (fun acc r ->
        { AnalyzedTypes = acc.AnalyzedTypes @ r.AnalyzedTypes
          MachineBindings = acc.MachineBindings @ r.MachineBindings })
        { AnalyzedTypes = []; MachineBindings = [] }
```

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs` (continued)
- **Notes**:
  - `TypeAnalyzer.mapFieldType`, `TypeAnalyzer.makeField`, `TypeAnalyzer.makeFieldFromFSharpField`, `TypeAnalyzer.entityToSourceLocation`, and the constraint attribute helpers are pure functions. Rather than duplicating them, make them accessible by either:
    - (a) Calling `TypeAnalyzer.mapFieldType` etc. directly from `UnifiedExtractor.fs` (they're in a public module), or
    - (b) Copying them if they're private.
  - Check: `TypeAnalyzer.mapFieldType` is public (`let rec mapFieldType`). `makeField`, `makeFieldFromFSharpField`, `entityToSourceLocation` are private. You'll need to either make them public or duplicate them.
  - **Recommended**: Make `makeField`, `makeFieldFromFSharpField`, and `entityToSourceLocation` internal (not public, but accessible within the assembly). Or duplicate them -- they're small functions.

### Subtask T009 -- Cross-reference syntax CEs with typed bindings

- **Purpose**: Connect syntax-level CE findings (routes, machine names, forState handlers) with typed AST results (state type DU cases, analyzed types) to produce `UnifiedResource` records.
- **Steps**:
  1. Implement the cross-referencing function:

```fsharp
let private buildUnifiedResources
    (syntaxFindings: SyntaxFinding list)
    (typedResult: UnifiedTypedResult)
    : UnifiedResource list =

    let bindingsByName =
        typedResult.MachineBindings
        |> List.map (fun b -> b.BindingName, b)
        |> Map.ofList

    syntaxFindings
    |> List.map (fun finding ->
        match finding with
        | FoundPlainResource(route, methods, name, hasLinkedData, file, line, col) ->
            let slug = UnifiedModel.resourceSlug route
            let methodStrings =
                methods
                |> List.map (fun m ->
                    match m with
                    | Get -> "GET" | Post -> "POST" | Put -> "PUT"
                    | Delete -> "DELETE" | Patch -> "PATCH"
                    | Head -> "HEAD" | Options -> "OPTIONS")
            let capabilities =
                methodStrings
                |> List.map (fun m ->
                    { Method = m
                      StateKey = None
                      LinkRelation = if m = "GET" then "self" else m.ToLowerInvariant()
                      IsSafe = m = "GET" || m = "HEAD" || m = "OPTIONS" })
            { RouteTemplate = route
              ResourceSlug = slug
              TypeInfo = [] // populated in T011
              Statechart = None
              HttpCapabilities = capabilities
              DerivedFields = UnifiedModel.emptyDerivedFields }

        | FoundStatefulResource(route, machineName, stateHandlers) ->
            let slug = UnifiedModel.resourceSlug route
            let machineInfo =
                machineName |> Option.bind (fun n -> Map.tryFind n bindingsByName)
            let stateNames =
                match machineInfo with
                | Some info -> info.StateTypeCases
                | None -> stateHandlers |> List.map _.CaseName |> List.distinct
            let initialStateKey =
                match machineInfo with
                | Some info ->
                    info.InitialStateName
                    |> Option.defaultValue (stateNames |> List.tryHead |> Option.defaultValue "Unknown")
                | None ->
                    stateNames |> List.tryHead |> Option.defaultValue "Unknown"
            let guardNames =
                match machineInfo with
                | Some info -> info.GuardNames
                | None -> []
            let stateHttpMethods =
                stateHandlers
                |> List.map (fun fs -> fs.CaseName, fs.Methods)
                |> Map.ofList
            let stateMetadata =
                stateNames
                |> List.map (fun name ->
                    let methods =
                        Map.tryFind name stateHttpMethods |> Option.defaultValue []
                    name, { IsFinal = false; AllowedMethods = methods; Description = None })
                |> Map.ofList
            let statechart =
                StatechartExtractor.toExtractedStatechart route stateNames initialStateKey guardNames stateMetadata

            let capabilities =
                stateHandlers
                |> List.collect (fun fs ->
                    fs.Methods
                    |> List.map (fun m ->
                        { Method = m
                          StateKey = Some fs.CaseName
                          LinkRelation = if m = "GET" then "self" else m.ToLowerInvariant()
                          IsSafe = m = "GET" || m = "HEAD" || m = "OPTIONS" }))

            { RouteTemplate = route
              ResourceSlug = slug
              TypeInfo = [] // populated in T011
              Statechart = Some statechart
              HttpCapabilities = capabilities
              DerivedFields = UnifiedModel.emptyDerivedFields })
```

  2. The `LinkRelation` assignment above is preliminary. Full ALPS-derived relation computation (research R4) will be handled by the generation command, not the extractor. The extractor provides defaults: `"self"` for GET, method name for others.

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs` (continued)
- **Notes**:
  - `StatechartExtractor.toExtractedStatechart` is a public helper that constructs the `ExtractedStatechart` record. Reuse it to ensure identical construction.
  - `TypeInfo` is left empty here and populated in T011 after associating types with resources.
  - The `IsSafe` computation matches HTTP semantics: GET, HEAD, OPTIONS are safe methods.

### Subtask T010 -- Compute `DerivedResourceFields`

- **Purpose**: Compute the structure-behavior invariant checks: orphan states, unhandled cases, state-to-type mappings, type coverage.
- **Steps**:
  1. Implement derived fields computation:

```fsharp
let private computeDerivedFields
    (resource: UnifiedResource)
    (allTypes: AnalyzedType list)
    : DerivedResourceFields =

    match resource.Statechart with
    | None -> UnifiedModel.emptyDerivedFields
    | Some sc ->
        // Find the state DU type among the analyzed types
        let stateDuCases =
            allTypes
            |> List.tryPick (fun t ->
                match t.Kind with
                | DiscriminatedUnion cases ->
                    let caseNames = cases |> List.map _.Name
                    // Match if all state names from the statechart are a subset of this DU's cases
                    if sc.StateNames |> List.forall (fun s -> List.contains s caseNames) then
                        Some cases
                    else None
                | _ -> None)
            |> Option.defaultValue []

        // Orphan states: DU cases not covered by any inState/forState call
        let handledStates =
            resource.HttpCapabilities
            |> List.choose _.StateKey
            |> List.distinct
        let orphanStates =
            sc.StateNames
            |> List.filter (fun s -> not (List.contains s handledStates))

        // Unhandled cases: DU cases in state type but not in statechart's state list
        let unhandledCases =
            stateDuCases
            |> List.map _.Name
            |> List.filter (fun c -> not (List.contains c sc.StateNames))

        // State structure: map each state to relevant fields
        // For now, all states share the same fields (future: per-case DU fields)
        let stateStructure =
            sc.StateNames
            |> List.map (fun stateName ->
                let fields =
                    stateDuCases
                    |> List.tryFind (fun c -> c.Name = stateName)
                    |> Option.map _.Fields
                    |> Option.defaultValue []
                stateName, fields)
            |> Map.ofList

        // Type coverage: ratio of state names that have type info
        let typeCoverage =
            if sc.StateNames.IsEmpty then 1.0
            else
                let covered =
                    sc.StateNames
                    |> List.filter (fun s -> Map.containsKey s stateStructure)
                    |> List.length
                float covered / float sc.StateNames.Length

        { OrphanStates = orphanStates
          UnhandledCases = unhandledCases
          StateStructure = stateStructure
          TypeCoverage = typeCoverage }
```

  2. Apply derived fields to each resource after type association:

```fsharp
let private enrichWithDerivedFields
    (allTypes: AnalyzedType list)
    (resources: UnifiedResource list)
    : UnifiedResource list =
    resources
    |> List.map (fun r ->
        { r with DerivedFields = computeDerivedFields r allTypes })
```

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs` (continued)
- **Notes**:
  - `OrphanStates` catches states declared in the DU but never handled in `inState`/`forState` calls. This is the "unhandled case" warning from spec FR-004.
  - `StateStructure` maps each state to its DU case fields. For `Won` in tic-tac-toe, this would include `Winner: string` etc. For cases with no fields (like `Draw`), the list is empty.
  - `TypeCoverage` = 1.0 when all states have type info. This helps the user understand extraction completeness.
  - The state DU matching heuristic (all statechart state names are a subset of DU case names) may produce false positives if multiple DUs share case names. This is acceptable for now -- the typed AST cross-reference via machine binding name is the primary match.

### Subtask T011 -- Handle plain `resource` CEs (type association)

- **Purpose**: Associate analyzed types with resources by matching route patterns and type usage in the source.
- **Steps**:
  1. For `statefulResource` CEs, the machine binding's state type is already known. But the resource's other types (request/response bodies) need association too.
  2. For plain `resource` CEs, type association is based on the types used in handler bodies (which the syntax AST doesn't fully capture). **Pragmatic approach**: associate ALL analyzed types with the project-level resource list. The per-resource type association is a best-effort heuristic at this stage.
  3. Implement type association:

```fsharp
let private associateTypes
    (resources: UnifiedResource list)
    (allTypes: AnalyzedType list)
    : UnifiedResource list =
    // For statefulResource: associate the state DU and any types referenced in its fields
    // For plain resource: associate all types (conservative -- better to over-associate than miss)
    resources
    |> List.map (fun r ->
        match r.Statechart with
        | Some sc ->
            // Find the state DU type
            let stateTypes =
                allTypes
                |> List.filter (fun t ->
                    match t.Kind with
                    | DiscriminatedUnion cases ->
                        let caseNames = cases |> List.map _.Name
                        sc.StateNames |> List.forall (fun s -> List.contains s caseNames)
                    | _ -> false)
            // Include the state DU + any types referenced in its fields
            let referencedTypeNames =
                stateTypes
                |> List.collect (fun t ->
                    match t.Kind with
                    | DiscriminatedUnion cases ->
                        cases |> List.collect (fun c ->
                            c.Fields |> List.choose (fun f ->
                                match f.Kind with
                                | Reference name -> Some name
                                | _ -> None))
                    | _ -> [])
                |> List.distinct
            let referencedTypes =
                allTypes
                |> List.filter (fun t -> List.contains t.ShortName referencedTypeNames)
            { r with TypeInfo = stateTypes @ referencedTypes |> List.distinctBy _.FullName }
        | None ->
            // Plain resource: associate all project types
            { r with TypeInfo = allTypes })
```

  4. Wire into the main extraction flow so types are associated before derived fields are computed.

- **Files**: `src/Frank.Cli.Core/Unified/UnifiedExtractor.fs` (continued)
- **Notes**:
  - For plain `resource` CEs, associating ALL types is intentionally broad. The ALPS generation (later WP) will narrow by examining which types appear in handler signatures.
  - For `statefulResource` CEs, the association is more precise: the state DU + its referenced types.
  - `List.distinctBy _.FullName` prevents duplicate type entries.

### Subtask T012 -- Write comparison tests

- **Purpose**: Prove that the unified extractor produces identical output to running the old extractors separately.
- **Steps**:
  1. Create `test/Frank.Cli.Core.Tests/UnifiedExtractorComparisonTests.fs` (or add to existing test file).
  2. For each test project (e.g., the tic-tac-toe sample):
     - Run `StatechartSourceExtractor.extract` to get `ExtractedStatechart list`.
     - Run `AstAnalyzer.analyzeFiles` + `TypeAnalyzer.analyzeTypes` to get types and resources.
     - Run `UnifiedExtractor.extract` to get `UnifiedResource list`.
     - Assert: for each `UnifiedResource` with `Statechart = Some sc`, verify `sc` matches the corresponding `ExtractedStatechart` (same route, same states, same initial state, same guards, same state metadata).
     - Assert: for each `UnifiedResource`, verify type info is non-empty for resources that have associated types.

  3. Test structure:

```fsharp
testCaseAsync "unified extractor matches old extractors for tic-tac-toe project" <| async {
    let projectPath = "path/to/sample.fsproj"

    // Old path: separate extractors
    let! oldStatecharts = StatechartSourceExtractor.extract projectPath
    let! loaded = ProjectLoader.loadProject projectPath
    let oldTypes = loaded |> Result.map (fun l -> TypeAnalyzer.analyzeTypes l.CheckResults)
    let oldResources = loaded |> Result.map (fun l ->
        AstAnalyzer.analyzeFiles (l.ParsedFiles |> List.map snd))

    // New path: unified extractor
    let! unified = UnifiedExtractor.extract projectPath

    // Compare
    match oldStatecharts, unified with
    | Ok oldSc, Ok unifiedRes ->
        for sc in oldSc do
            let matching =
                unifiedRes
                |> List.tryFind (fun r -> r.RouteTemplate = sc.RouteTemplate)
            Expect.isSome matching $"Unified should contain resource for {sc.RouteTemplate}"
            let ur = matching.Value
            Expect.isSome ur.Statechart "Should have statechart data"
            let usc = ur.Statechart.Value
            Expect.equal usc.StateNames sc.StateNames "State names should match"
            Expect.equal usc.InitialStateKey sc.InitialStateKey "Initial state should match"
            Expect.equal usc.GuardNames sc.GuardNames "Guard names should match"
    | Error e, _ -> failtest $"Old extractor failed: {e}"
    | _, Error e -> failtest $"Unified extractor failed: {e}"
}
```

  4. Also test that plain `resource` CEs are found by the unified extractor (the old `AstAnalyzer` found them).

- **Files**: Test file in `test/Frank.Cli.Core.Tests/` (NEW or MODIFIED)
- **Notes**:
  - The comparison test is the key acceptance gate. If the unified extractor produces different results, the implementation has a bug.
  - If there's no existing test project for `Frank.Cli.Core`, you may need to create one or add tests to an existing project.
  - The test needs a real F# project to analyze. Use the tic-tac-toe sample or a minimal fixture project.

---

## Public API

The main entry point:

```fsharp
/// Extract unified resource descriptions from an F# project using FCS.
/// Performs a single FCS typecheck and produces both type and behavioral data.
let extract (projectPath: string) : Async<Result<UnifiedResource list, StatechartError>> =
    async {
        if not (File.Exists projectPath) then
            return Error (FileNotFound projectPath)
        else
            match! ProjectLoader.loadProject projectPath with
            | Error msg ->
                return Error (AssemblyLoadError(projectPath, msg))
            | Ok loaded ->
                // Phase 1: Single-pass syntax walk
                let syntaxFindings = findAllResources loaded.ParsedFiles

                // Phase 2: Single-pass typed AST walk
                let typedResult = analyzeTypedAst loaded.CheckResults

                // Phase 3: Cross-reference and build UnifiedResource records
                let resources = buildUnifiedResources syntaxFindings typedResult

                // Phase 4: Associate types with resources
                let withTypes = associateTypes resources typedResult.AnalyzedTypes

                // Phase 5: Compute derived fields
                let enriched = enrichWithDerivedFields typedResult.AnalyzedTypes withTypes

                return Ok enriched
    }
```

Add compile entry to `src/Frank.Cli.Core/Frank.Cli.Core.fsproj`:

```xml
<!-- Unified pipeline -->
<Compile Include="Unified/UnifiedModel.fs" />
<Compile Include="Unified/UnifiedExtractor.fs" />
```

Place after the statechart pipeline section.

---

## Test Strategy

- **Comparison tests** (T012): Run old and new extractors against same project, assert identical results.
- **Unit tests**: Test `computeDerivedFields` with synthetic inputs (known orphan states, known unhandled cases).
- **Edge cases**: Project with only plain resources (no statefulResource), project with only statefulResource (no plain resource), project with compilation errors (should fail fast with clear FCS diagnostics).
- **Build validation**: `dotnet build` from repository root.

```bash
dotnet build
dotnet test
```

---

## Risks & Mitigations

| Risk | Severity | Mitigation |
|------|----------|------------|
| Single-walk approach is significantly harder than expected | High | KEY CONSTRAINT: stop and ask if this happens. Do not silently fall back to composition. |
| `AstAnalyzer` private helpers need duplication | Medium | Acceptable -- the unified extractor is replacing the old code. Duplication is temporary. |
| `TypeAnalyzer` private helpers (`makeField`, `makeFieldFromFSharpField`) need access | Medium | Either make them internal or duplicate. They're small pure functions. |
| State DU matching heuristic produces false positives | Low | The machine binding name cross-reference is the primary match. Heuristic is fallback only. |
| Type association for plain resources is too broad (all types) | Low | Acceptable for Phase 0. Narrowed in later WPs by examining handler signatures. |

---

## Review Guidance

- Verify the unified walker has a SINGLE `walkExpr` function, not two separate walkers composed.
- Verify `tryExtractStatefulResource` is checked BEFORE `tryExtractResource` (more specific pattern first).
- Verify comparison tests (T012) cover at least one project with both `resource` and `statefulResource` CEs.
- Verify `DerivedResourceFields` computation correctly identifies orphan states and unhandled cases.
- Verify the public `extract` function signature matches what WP03 and WP04 will consume.
- Verify `dotnet build` passes cleanly.

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
- 2026-03-19T03:24:17Z – claude-opus – shell_pid=17534 – lane=doing – Assigned agent via workflow command
