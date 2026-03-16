---
work_package_id: "WP02"
subtasks:
  - "T008"
  - "T009"
  - "T010"
  - "T011"
  - "T012"
  - "T013"
  - "T014"
title: "ALPS JSON Parser"
phase: "Phase 1 - Core Parsers"
lane: "planned"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs: ["FR-001", "FR-003", "FR-004", "FR-005", "FR-006", "FR-007", "FR-008", "FR-016", "FR-017"]
history:
  - timestamp: "2026-03-16T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP02 -- ALPS JSON Parser

## Important: Review Feedback Status

**Read this first if you are implementing this task!**

- **Has review feedback?**: Check the `review_status` field above. If it says `has_feedback`, scroll to the **Review Feedback** section immediately.
- **You must address all feedback** before your work is complete.
- **Mark as acknowledged**: When you understand the feedback and begin addressing it, update `review_status: acknowledged` in the frontmatter.

---

## Review Feedback

> **Populated by `/spec-kitty.review`** -- Reviewers add detailed feedback here when work needs changes.

*[This section is empty initially.]*

---

## Markdown Formatting
Wrap HTML/XML tags in backticks: `` `<div>` ``, `` `<descriptor>` ``
Use language identifiers in code blocks: ````fsharp`, ````json`

---

## Implementation Command

```bash
spec-kitty implement WP02 --base WP01
```

---

## Objectives & Success Criteria

1. `parseAlpsJson` function correctly parses ALPS JSON documents into `AlpsDocument` AST.
2. All tic-tac-toe golden file descriptors (states, transitions, guards, parameters) correctly parsed.
3. All onboarding golden file descriptors correctly parsed.
4. Malformed JSON input produces structured `AlpsParseError` results (no unhandled exceptions).
5. Unknown JSON properties are silently ignored (forward-compatibility, FR-017).
6. `dotnet build` succeeds on all targets; `dotnet test` passes all new and existing tests.

## Context & Constraints

- **Spec references**: User Story 1 in spec.md. FR-001, FR-003 through FR-008, FR-016, FR-017, FR-018.
- **Architecture decisions**: AD-002 (System.Text.Json), AD-004 (Result error handling), AD-005 (forward compatibility).
- **Research decisions**: R-001 (default Semantic), R-002 (single rt), R-003 (JsonDocument.Parse with use binding).
- **Data model**: See data-model.md for type signatures and field descriptions.
- **Types dependency**: `Alps/Types.fs` from WP01 must exist. Use `open Frank.Statecharts.Alps.Types`.
- **Golden files**: `GoldenFiles.fs` from WP01 provides test data.
- **Constitution**: Principle VI (resource disposal -- `use` binding for `JsonDocument`), Principle VII (no silent exception swallowing).

## Subtasks & Detailed Guidance

### Subtask T008 -- Implement Top-Level parseAlpsJson Function

- **Purpose**: Entry point for ALPS JSON parsing. Handles the outermost structure: root `alps` object, version, and top-level doc/link/ext elements.
- **File**: `src/Frank.Statecharts/Alps/JsonParser.fs` (new file)
- **Module**: `module internal Frank.Statecharts.Alps.JsonParser`

**Function signature:**
```fsharp
val parseAlpsJson : json: string -> Result<AlpsDocument, AlpsParseError list>
```

**Implementation steps:**

1. Open required namespaces:
   ```fsharp
   open System
   open System.Text.Json
   open Frank.Statecharts.Alps.Types
   ```

2. Parse the JSON string with `use` binding:
   ```fsharp
   let parseAlpsJson (json: string) : Result<AlpsDocument, AlpsParseError list> =
       try
           use doc = JsonDocument.Parse(json)
           let root = doc.RootElement
           // ... walk the structure
       with
       | :? JsonException as ex ->
           Error [ { Description = ex.Message; Position = None } ]
   ```

3. Extract the `alps` root object:
   ```fsharp
   match root.TryGetProperty("alps") with
   | true, alps -> // proceed to parse alps content
   | false, _ -> Error [ { Description = "Missing 'alps' root object"; Position = None } ]
   ```

4. Parse optional fields from the `alps` object:
   - `version`: `alps.TryGetProperty("version")` -> `string option`
   - `doc`: Parse documentation element (helper function)
   - `link`: Parse link array (helper function)
   - `ext`: Parse extension array (helper function)
   - `descriptor`: Parse descriptor array (T009)

**Key detail**: `JsonDocument` is `IDisposable`. The `use` binding ensures disposal. All data must be copied into F# types BEFORE the function returns, because `JsonElement` references become invalid after `JsonDocument` disposal.

### Subtask T009 -- Implement Descriptor Parsing

- **Purpose**: Parse the descriptor array, which is the core of ALPS. Descriptors can be nested (self-referential), have multiple optional fields, and reference other descriptors via href.
- **File**: Same file: `src/Frank.Statecharts/Alps/JsonParser.fs`

**Helper functions to implement:**

1. **`parseDescriptorType`**: Map string to `DescriptorType` DU:
   ```fsharp
   let private parseDescriptorType (typeStr: string) : DescriptorType =
       match typeStr.ToLowerInvariant() with
       | "semantic" -> Semantic
       | "safe" -> Safe
       | "unsafe" -> Unsafe
       | "idempotent" -> Idempotent
       | _ -> Semantic  // default to Semantic for unknown types (forward-compat)
   ```

2. **`parseDocumentation`**: Parse a `doc` JSON object:
   ```fsharp
   let private parseDocumentation (elem: JsonElement) : AlpsDocumentation =
       { Format = tryGetString elem "format"
         Value = elem.GetProperty("value").GetString() }
   ```

3. **`parseExtension`**: Parse an `ext` JSON object:
   ```fsharp
   let private parseExtension (elem: JsonElement) : AlpsExtension =
       { Id = elem.GetProperty("id").GetString()
         Href = tryGetString elem "href"
         Value = tryGetString elem "value" }
   ```

4. **`parseLink`**: Parse a `link` JSON object:
   ```fsharp
   let private parseLink (elem: JsonElement) : AlpsLink =
       { Rel = elem.GetProperty("rel").GetString()
         Href = elem.GetProperty("href").GetString() }
   ```

5. **`parseDescriptor`** (recursive): Parse a single descriptor, recursively parsing nested children:
   ```fsharp
   let rec private parseDescriptor (elem: JsonElement) : Descriptor =
       { Id = tryGetString elem "id"
         Type =
             match tryGetString elem "type" with
             | Some t -> parseDescriptorType t
             | None -> Semantic  // FR-006: default to Semantic
         Href = tryGetString elem "href"
         ReturnType = tryGetString elem "rt"
         Documentation = tryGetDoc elem
         Descriptors = tryGetArray elem "descriptor" |> List.map parseDescriptor
         Extensions = tryGetArray elem "ext" |> List.map parseExtension
         Links = tryGetArray elem "link" |> List.map parseLink }
   ```

6. **Utility helpers**:
   - `tryGetString`: `JsonElement -> string -> string option` -- returns `None` if property missing
   - `tryGetDoc`: `JsonElement -> AlpsDocumentation option` -- returns `None` if no `doc` property
   - `tryGetArray`: `JsonElement -> string -> JsonElement list` -- returns empty list if property missing

**Edge cases to handle:**
- Descriptor with no `type` attribute: defaults to `Semantic` (FR-006, R-001)
- Descriptor with only `href` (no `id`): valid href-only reference
- Empty descriptor array: valid, produces empty list
- Unknown properties on descriptor: silently ignored (FR-017)

### Subtask T010 -- Implement Error Handling

- **Purpose**: Ensure malformed input produces structured errors, not unhandled exceptions. Follows AD-004 and constitution principle VII.
- **File**: Same file: `src/Frank.Statecharts/Alps/JsonParser.fs`

**Error categories:**

1. **Structural errors** (invalid JSON): Caught by `JsonDocument.Parse` throwing `JsonException`. Wrap in `try/with` and return `Error` with description from the exception message. Position is `None` (System.Text.Json provides byte offset, not line/column).

2. **Schema errors** (valid JSON, invalid ALPS structure):
   - Missing `alps` root object -> return error
   - Missing required `id` on `ext` element -> return error
   - Missing required `rel` or `href` on `link` element -> return error
   - Missing required `value` on `doc` element -> return error

3. **Forward compatibility**: Unknown properties are NOT errors. They are silently skipped.

**Pattern**: Collect errors during parsing. If any errors found, return `Error errors`. If all OK, return `Ok alpsDocument`.

**Important**: Only catch `JsonException` specifically. Do NOT add a catch-all handler. If an unexpected exception occurs, let it propagate (constitution principle VII -- no silent exception swallowing).

### Subtask T011 -- Add JsonParser.fs to fsproj

- **Purpose**: Wire the new parser into the compile order.
- **File**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Steps**: Add `<Compile Include="Alps/JsonParser.fs" />` immediately after `<Compile Include="Alps/Types.fs" />`.

**Expected compile order:**
```xml
<Compile Include="Alps/Types.fs" />
<Compile Include="Alps/JsonParser.fs" />
<Compile Include="Types.fs" />
```

- **Validation**: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` must succeed.

### Subtask T012 -- Golden File Parser Tests

- **Purpose**: Validate that the JSON parser correctly handles the primary golden files, establishing confidence in the core parsing logic.
- **File**: `test/Frank.Statecharts.Tests/Alps/JsonParserTests.fs` (new file)
- **Module**: `module Frank.Statecharts.Tests.Alps.JsonParserTests`

**Test cases:**

```fsharp
[<Tests>]
let jsonParserTests = testList "Alps.JsonParser" [
    testCase "parse tic-tac-toe golden file succeeds" <| fun _ ->
        let result = parseAlpsJson GoldenFiles.ticTacToeAlpsJson
        let doc = Expect.wantOk result "should parse successfully"
        // Verify version
        Expect.equal doc.Version (Some "1.0") "version"
        // Verify top-level doc exists
        Expect.isSome doc.Documentation "should have documentation"
        // Verify descriptor count
        // Verify specific descriptors by walking the list

    testCase "tic-tac-toe has all state descriptors" <| fun _ ->
        let doc = parseAlpsJson GoldenFiles.ticTacToeAlpsJson |> Result.defaultWith (fun _ -> failwith "parse failed")
        let stateIds = doc.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList
        Expect.containsAll stateIds (Set.ofList ["XTurn"; "OTurn"; "Won"; "Draw"]) "all states present"

    testCase "tic-tac-toe makeMove has correct type and rt" <| fun _ ->
        // Find a makeMove descriptor and verify type=Unsafe, rt is set
        ...

    testCase "tic-tac-toe guards are captured in ext elements" <| fun _ ->
        // Find descriptors with ext elements, verify guard id and value
        ...

    testCase "parse onboarding golden file succeeds" <| fun _ ->
        let result = parseAlpsJson GoldenFiles.onboardingAlpsJson
        let doc = Expect.wantOk result "should parse successfully"
        // Verify states and transitions
        ...
]
```

**Key assertions**:
- Parser returns `Ok` for valid golden files.
- All descriptor ids present.
- Descriptor types correct (semantic for states, unsafe/safe for transitions).
- `rt` values correctly captured.
- `ext` elements (guards) correctly captured with id and value.
- Nested descriptor hierarchy preserved.
- Documentation captured.
- Links captured.

### Subtask T013 -- Edge Case Tests

- **Purpose**: Validate parser behavior for edge cases specified in the spec.
- **File**: Same file: `test/Frank.Statecharts.Tests/Alps/JsonParserTests.fs`
- **Parallel**: Yes, can be written alongside T012.

**Test cases:**

```fsharp
testList "Alps.JsonParser edge cases" [
    testCase "empty ALPS document (no descriptors)" <| fun _ ->
        let json = """{"alps":{"descriptor":[]}}"""
        let doc = parseAlpsJson json |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.isEmpty doc.Descriptors "empty descriptors"

    testCase "descriptor without type defaults to Semantic" <| fun _ ->
        let json = """{"alps":{"descriptor":[{"id":"test"}]}}"""
        let doc = parseAlpsJson json |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.equal doc.Descriptors.[0].Type Semantic "default to Semantic"

    testCase "unknown JSON properties are ignored" <| fun _ ->
        let json = """{"alps":{"unknownProp":"value","descriptor":[{"id":"test","futureField":42}]}}"""
        let doc = parseAlpsJson json |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.equal doc.Descriptors.Length 1 "descriptor parsed despite unknown props"

    testCase "ALPS document with only links (no descriptors)" <| fun _ ->
        let json = """{"alps":{"link":[{"rel":"self","href":"http://example.com"}]}}"""
        let doc = parseAlpsJson json |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.isEmpty doc.Descriptors "no descriptors"
        Expect.equal doc.Links.Length 1 "one link"

    testCase "descriptor with href to external URL" <| fun _ ->
        let json = """{"alps":{"descriptor":[{"href":"http://example.com/profile"}]}}"""
        let doc = parseAlpsJson json |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.equal doc.Descriptors.[0].Href (Some "http://example.com/profile") "external href"

    testCase "multiple ext elements on a single descriptor" <| fun _ ->
        let json = """{"alps":{"descriptor":[{"id":"test","ext":[{"id":"guard","value":"role=X"},{"id":"meta","value":"info"}]}]}}"""
        let doc = parseAlpsJson json |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.equal doc.Descriptors.[0].Extensions.Length 2 "two extensions"

    testCase "unicode characters in descriptor ids and doc values" <| fun _ ->
        let json = """{"alps":{"descriptor":[{"id":"beschreibung","doc":{"value":"Beschreibung auf Deutsch"}}]}}"""
        let doc = parseAlpsJson json |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.equal doc.Descriptors.[0].Id (Some "beschreibung") "unicode id"

    testCase "descriptor with no id (href-only reference)" <| fun _ ->
        let json = """{"alps":{"descriptor":[{"href":"#otherDescriptor"}]}}"""
        let doc = parseAlpsJson json |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.isNone doc.Descriptors.[0].Id "no id on href-only descriptor"
        Expect.equal doc.Descriptors.[0].Href (Some "#otherDescriptor") "href present"
]
```

### Subtask T014 -- Error Case Tests

- **Purpose**: Validate that malformed input produces structured errors, not exceptions.
- **File**: Same file: `test/Frank.Statecharts.Tests/Alps/JsonParserTests.fs`
- **Parallel**: Yes, can be written alongside T012.

**Test cases:**

```fsharp
testList "Alps.JsonParser errors" [
    testCase "malformed JSON returns error" <| fun _ ->
        let result = parseAlpsJson "not valid json"
        Expect.isError result "should be error"

    testCase "empty string returns error" <| fun _ ->
        let result = parseAlpsJson ""
        Expect.isError result "should be error"

    testCase "valid JSON but missing alps root returns error" <| fun _ ->
        let result = parseAlpsJson """{"descriptors":[]}"""
        Expect.isError result "should be error for missing alps root"
        match result with
        | Error errors -> Expect.isNonEmpty errors "should have error details"
        | Ok _ -> failwith "expected error"

    testCase "error description is actionable" <| fun _ ->
        let result = parseAlpsJson "not valid json"
        match result with
        | Error errors ->
            Expect.isNonEmpty errors "should have errors"
            Expect.isNotEmpty errors.[0].Description "error description not empty"
        | Ok _ -> failwith "expected error"

    testCase "JSON parse error has no position" <| fun _ ->
        let result = parseAlpsJson "not valid json"
        match result with
        | Error errors ->
            Expect.isNone errors.[0].Position "JSON errors have no position"
        | Ok _ -> failwith "expected error"
]
```

**Add test file to fsproj**: Add `<Compile Include="Alps/JsonParserTests.fs" />` after `<Compile Include="Alps/TypeTests.fs" />` in `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj`.

- **Validation**: `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` must pass all tests.

## Risks & Mitigations

- **JsonDocument disposal timing**: All `JsonElement` data must be extracted into F# records before the `use` block ends. If you return a `JsonElement` reference, it will be invalid. Extract strings and build records eagerly.
- **Recursive parsing depth**: Deeply nested descriptors could cause stack overflow. For typical ALPS documents (<=100 descriptors, <=5 levels deep), this is not a concern. No explicit depth limit needed per spec.
- **Golden file changes**: If WP01 golden files need adjustment, parser tests may need updating. Coordinate with WP01 if issues arise.

## Review Guidance

- Verify `JsonDocument` has `use` binding (constitution principle VI).
- Verify only `JsonException` is caught specifically (constitution principle VII -- no catch-all).
- Verify unknown properties are silently ignored (FR-017).
- Verify missing `type` defaults to `Semantic` (FR-006).
- Verify all golden file assertions check meaningful structure (not just "parse succeeded").
- Verify fsproj compile order is correct.
- Run `dotnet build` and `dotnet test` to confirm everything compiles and passes.

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

- 2026-03-16T00:00:00Z - system - lane=planned - Prompt created.
