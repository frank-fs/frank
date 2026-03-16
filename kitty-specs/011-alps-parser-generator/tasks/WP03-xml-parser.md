---
work_package_id: "WP03"
subtasks:
  - "T015"
  - "T016"
  - "T017"
  - "T018"
  - "T019"
  - "T020"
title: "ALPS XML Parser"
phase: "Phase 1 - Core Parsers"
lane: "for_review"
assignee: ""
agent: ""
shell_pid: ""
review_status: ""
reviewed_by: ""
dependencies: ["WP01"]
requirement_refs: ["FR-002", "FR-003", "FR-004", "FR-005", "FR-006", "FR-007", "FR-008", "FR-016", "FR-017"]
history:
  - timestamp: "2026-03-16T00:00:00Z"
    lane: "planned"
    agent: "system"
    shell_pid: ""
    action: "Prompt generated via /spec-kitty.tasks"
---

# Work Package Prompt: WP03 -- ALPS XML Parser

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
Wrap HTML/XML tags in backticks: `` `<alps>` ``, `` `<descriptor>` ``, `` `<ext>` ``
Use language identifiers in code blocks: ````fsharp`, ````xml`

---

## Implementation Command

```bash
spec-kitty implement WP03 --base WP01
```

---

## Objectives & Success Criteria

1. `parseAlpsXml` function correctly parses ALPS XML documents into `AlpsDocument` AST.
2. Parsing the tic-tac-toe ALPS XML golden file produces an AST structurally equivalent to parsing the JSON golden file (FR-002).
3. All onboarding ALPS XML descriptors correctly parsed.
4. Malformed XML input produces structured `AlpsParseError` results with line/column position info.
5. Unknown XML elements are silently ignored (forward-compatibility, FR-017).
6. `dotnet build` and `dotnet test` pass.

## Context & Constraints

- **Spec references**: User Story 2 in spec.md. FR-001, FR-002 through FR-008, FR-016, FR-017.
- **Architecture decisions**: AD-002 (System.Xml.Linq), AD-004 (Result error handling), AD-005 (forward compatibility).
- **Research decisions**: R-004 (XDocument.Parse, NOT IDisposable), R-001 (default Semantic).
- **Data model**: See data-model.md for AlpsDocument type structure.
- **Types dependency**: `Alps/Types.fs` from WP01.
- **Golden files**: `GoldenFiles.fs` from WP01 provides XML test data.
- **Critical requirement (FR-002)**: JSON and XML parsers MUST produce identical `AlpsDocument` for equivalent input.
- **ALPS XML rules**: `alps`, `doc`, `descriptor`, `ext` are elements. All other ALPS properties are attributes.
- **Constitution**: Principle VII (no silent exception swallowing). Note: `XDocument` is NOT `IDisposable`.

## Subtasks & Detailed Guidance

### Subtask T015 -- Implement Top-Level parseAlpsXml Function

- **Purpose**: Entry point for ALPS XML parsing. Handles the root `<alps>` element, version attribute, and top-level children.
- **File**: `src/Frank.Statecharts/Alps/XmlParser.fs` (new file)
- **Module**: `module internal Frank.Statecharts.Alps.XmlParser`

**Function signature:**
```fsharp
val parseAlpsXml : xml: string -> Result<AlpsDocument, AlpsParseError list>
```

**Implementation steps:**

1. Open required namespaces:
   ```fsharp
   open System.Xml
   open System.Xml.Linq
   open Frank.Statecharts.Alps.Types
   ```

2. Parse the XML string (NOT IDisposable -- no `use` binding):
   ```fsharp
   let parseAlpsXml (xml: string) : Result<AlpsDocument, AlpsParseError list> =
       try
           let doc = XDocument.Parse(xml)
           let root = doc.Root
           // Verify root element is "alps"
           if root.Name.LocalName <> "alps" then
               Error [ { Description = "Root element must be 'alps'"; Position = None } ]
           else
               // Parse alps content
               Ok { Version = root |> attrValue "version"
                    Documentation = root |> childDoc
                    Descriptors = root |> childDescriptors
                    Links = root |> childLinks
                    Extensions = root |> childExtensions }
       with
       | :? XmlException as ex ->
           Error [ { Description = ex.Message
                     Position = Some { Line = ex.LineNumber; Column = ex.LinePosition } } ]
   ```

3. Extract from the `<alps>` root element:
   - `version`: Attribute on root element -> `string option`
   - `<doc>`: Child element -> `AlpsDocumentation option`
   - `<descriptor>`: Child elements -> `Descriptor list`
   - `<link>`: Child elements -> `AlpsLink list`
   - `<ext>`: Child elements -> `AlpsExtension list`

**Key detail**: `XDocument` is NOT `IDisposable` (unlike `XmlReader`). No `use` binding needed (R-004).

### Subtask T016 -- Implement Descriptor Parsing from XML

- **Purpose**: Parse `<descriptor>` elements with their attributes and child elements. Descriptors nest recursively.
- **File**: Same file: `src/Frank.Statecharts/Alps/XmlParser.fs`

**Helper functions to implement:**

1. **`attrValue`**: Get optional attribute value:
   ```fsharp
   let private attrValue (name: string) (elem: XElement) : string option =
       match elem.Attribute(XName.Get name) with
       | null -> None
       | attr -> Some attr.Value
   ```

2. **`childDoc`**: Parse optional `<doc>` child element:
   ```fsharp
   let private childDoc (parent: XElement) : AlpsDocumentation option =
       match parent.Element(XName.Get "doc") with
       | null -> None
       | docElem ->
           Some { Format = docElem |> attrValue "format"
                  Value = docElem.Value }  // inner text content
   ```

3. **`parseExtensionElement`**: Parse `<ext>` element:
   ```fsharp
   let private parseExtensionElement (elem: XElement) : AlpsExtension =
       { Id = elem.Attribute(XName.Get "id").Value
         Href = elem |> attrValue "href"
         Value = elem |> attrValue "value" }
   ```

4. **`parseLinkElement`**: Parse `<link>` element:
   ```fsharp
   let private parseLinkElement (elem: XElement) : AlpsLink =
       { Rel = elem.Attribute(XName.Get "rel").Value
         Href = elem.Attribute(XName.Get "href").Value }
   ```

5. **`parseDescriptorElement`** (recursive): Parse a single `<descriptor>` element:
   ```fsharp
   let rec private parseDescriptorElement (elem: XElement) : Descriptor =
       { Id = elem |> attrValue "id"
         Type =
             match elem |> attrValue "type" with
             | Some t -> parseDescriptorType t
             | None -> Semantic  // FR-006
         Href = elem |> attrValue "href"
         ReturnType = elem |> attrValue "rt"
         Documentation = elem |> childDoc
         Descriptors =
             elem.Elements(XName.Get "descriptor")
             |> Seq.map parseDescriptorElement
             |> Seq.toList
         Extensions =
             elem.Elements(XName.Get "ext")
             |> Seq.map parseExtensionElement
             |> Seq.toList
         Links =
             elem.Elements(XName.Get "link")
             |> Seq.map parseLinkElement
             |> Seq.toList }
   ```

6. **`parseDescriptorType`**: Same mapping as JSON parser:
   ```fsharp
   let private parseDescriptorType (typeStr: string) : DescriptorType =
       match typeStr.ToLowerInvariant() with
       | "semantic" -> Semantic
       | "safe" -> Safe
       | "unsafe" -> Unsafe
       | "idempotent" -> Idempotent
       | _ -> Semantic
   ```

**XML-specific details:**
- `<doc>` text content is the element's `Value` property (inner text), NOT an attribute.
- `<doc format="text">` has `format` as an attribute.
- `<ext id="..." value="..." href="..."/>` -- all values are attributes.
- `<link rel="..." href="..."/>` -- all values are attributes.
- Nested `<descriptor>` elements are child elements of the parent `<descriptor>`.

**Edge cases:**
- Descriptor with no `type` attribute: default to `Semantic` (FR-006).
- Empty `<descriptor/>` self-closing element: valid, produces Descriptor with all optional fields as None/empty.
- `<doc>` with no `format` attribute: Format is None (parser doesn't default; spec says ALPS defaults to "text" but parser preserves what's there).

### Subtask T017 -- Implement Error Handling

- **Purpose**: Produce structured errors for malformed XML with line/column info from `XmlException`.
- **File**: Same file: `src/Frank.Statecharts/Alps/XmlParser.fs`

**Error categories:**

1. **Structural errors** (invalid XML): Caught by `XDocument.Parse` throwing `XmlException`. The exception provides `LineNumber` and `LinePosition` -- capture these in `AlpsSourcePosition`.

   ```fsharp
   | :? XmlException as ex ->
       Error [ { Description = ex.Message
                 Position = Some { Line = ex.LineNumber; Column = ex.LinePosition } } ]
   ```

2. **Schema errors**: Root element is not `<alps>` -> return error with description.

3. **Missing required attributes**: If `<ext>` is missing `id`, `<link>` is missing `rel` or `href` -> produce error.

**Important**: Only catch `XmlException` specifically. Do not catch general exceptions (constitution principle VII).

**Namespace handling**: ALPS XML documents may include namespace declarations. Use `XName.Get "localName"` (without namespace) for lookups. If the document uses an ALPS namespace like `xmlns="..."`, use `elem.Name.LocalName` for comparisons instead of `elem.Name`.

### Subtask T018 -- Add XmlParser.fs to fsproj

- **Purpose**: Wire the XML parser into the compile order.
- **File**: `src/Frank.Statecharts/Frank.Statecharts.fsproj`
- **Steps**: Add `<Compile Include="Alps/XmlParser.fs" />` after `<Compile Include="Alps/JsonParser.fs" />`.

**Expected compile order:**
```xml
<Compile Include="Alps/Types.fs" />
<Compile Include="Alps/JsonParser.fs" />
<Compile Include="Alps/XmlParser.fs" />
<Compile Include="Types.fs" />
```

**Note**: If WP02 has not yet been merged (parallel development), add XmlParser.fs after Types.fs and adjust once WP02 lands. The fsproj must have Alps/Types.fs before XmlParser.fs.

- **Validation**: `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` must succeed.

### Subtask T019 -- Golden File and Cross-Format Equivalence Tests

- **Purpose**: Validate XML parsing against golden files and verify JSON/XML parsers produce identical ASTs (FR-002).
- **File**: `test/Frank.Statecharts.Tests/Alps/XmlParserTests.fs` (new file)
- **Module**: `module Frank.Statecharts.Tests.Alps.XmlParserTests`

**Test cases:**

```fsharp
[<Tests>]
let xmlParserTests = testList "Alps.XmlParser" [
    testCase "parse tic-tac-toe XML golden file succeeds" <| fun _ ->
        let result = parseAlpsXml GoldenFiles.ticTacToeAlpsXml
        let doc = Expect.wantOk result "should parse successfully"
        Expect.equal doc.Version (Some "1.0") "version"
        Expect.isSome doc.Documentation "should have documentation"

    testCase "tic-tac-toe XML has all state descriptors" <| fun _ ->
        let doc = parseAlpsXml GoldenFiles.ticTacToeAlpsXml |> Result.defaultWith (fun _ -> failwith "parse failed")
        let stateIds = doc.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList
        Expect.containsAll stateIds (Set.ofList ["XTurn"; "OTurn"; "Won"; "Draw"]) "all states present"

    testCase "tic-tac-toe XML and JSON produce equivalent ASTs" <| fun _ ->
        let jsonDoc = parseAlpsJson GoldenFiles.ticTacToeAlpsJson |> Result.defaultWith (fun _ -> failwith "JSON parse failed")
        let xmlDoc = parseAlpsXml GoldenFiles.ticTacToeAlpsXml |> Result.defaultWith (fun _ -> failwith "XML parse failed")
        Expect.equal xmlDoc jsonDoc "XML and JSON ASTs must be structurally equal"

    testCase "onboarding XML and JSON produce equivalent ASTs" <| fun _ ->
        let jsonDoc = parseAlpsJson GoldenFiles.onboardingAlpsJson |> Result.defaultWith (fun _ -> failwith "JSON parse failed")
        let xmlDoc = parseAlpsXml GoldenFiles.onboardingAlpsXml |> Result.defaultWith (fun _ -> failwith "XML parse failed")
        Expect.equal xmlDoc jsonDoc "onboarding XML and JSON ASTs must be structurally equal"

    testCase "parse onboarding XML golden file succeeds" <| fun _ ->
        let result = parseAlpsXml GoldenFiles.onboardingAlpsXml
        let doc = Expect.wantOk result "should parse successfully"
        let stateIds = doc.Descriptors |> List.choose (fun d -> d.Id) |> Set.ofList
        Expect.containsAll stateIds (Set.ofList ["Welcome"; "CollectEmail"; "CollectProfile"; "Review"; "Complete"]) "all onboarding states"
]
```

**Cross-format equivalence note**: The `Expect.equal xmlDoc jsonDoc` test relies on F# structural equality for records. Both parsers must produce `AlpsDocument` with identical field values (same descriptor order, same optional field values). If ordering differs between JSON arrays and XML element order, the golden files must be authored with matching order.

**Note on cross-format tests**: These tests require `parseAlpsJson` from WP02. If WP03 is developed in parallel with WP02:
- Write the cross-format tests referencing `parseAlpsJson`.
- If WP02 has not yet merged, add the `Alps/JsonParser.fs` compile include to the fsproj (it will compile once WP02 merges).
- Alternatively, mark these tests as pending until WP02 is available.

### Subtask T020 -- Edge Case and Error Tests

- **Purpose**: Validate XML-specific edge cases and error handling.
- **File**: Same file: `test/Frank.Statecharts.Tests/Alps/XmlParserTests.fs`
- **Parallel**: Yes, can be written alongside T019.

**Test cases:**

```fsharp
testList "Alps.XmlParser edge cases and errors" [
    testCase "empty ALPS XML (no descriptors)" <| fun _ ->
        let xml = """<alps version="1.0"></alps>"""
        let doc = parseAlpsXml xml |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.isEmpty doc.Descriptors "empty descriptors"

    testCase "descriptor without type defaults to Semantic" <| fun _ ->
        let xml = """<alps><descriptor id="test"/></alps>"""
        let doc = parseAlpsXml xml |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.equal doc.Descriptors.[0].Type Semantic "default to Semantic"

    testCase "unknown XML elements are ignored" <| fun _ ->
        let xml = """<alps><futureElement foo="bar"/><descriptor id="test"/></alps>"""
        let doc = parseAlpsXml xml |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.equal doc.Descriptors.Length 1 "descriptor parsed despite unknown elements"

    testCase "malformed XML returns error" <| fun _ ->
        let result = parseAlpsXml "<not valid xml"
        Expect.isError result "should be error"

    testCase "XML error includes line/column position" <| fun _ ->
        let result = parseAlpsXml "<alps>\n<broken"
        match result with
        | Error errors ->
            Expect.isSome errors.[0].Position "XML errors should have position"
        | Ok _ -> failwith "expected error"

    testCase "wrong root element returns error" <| fun _ ->
        let result = parseAlpsXml """<notAlps><descriptor id="test"/></notAlps>"""
        Expect.isError result "should be error for wrong root element"

    testCase "doc element with format attribute" <| fun _ ->
        let xml = """<alps><doc format="html">Some &lt;b&gt;bold&lt;/b&gt; text</doc></alps>"""
        let doc = parseAlpsXml xml |> Result.defaultWith (fun _ -> failwith "parse failed")
        Expect.isSome doc.Documentation "should have documentation"
        Expect.equal doc.Documentation.Value.Format (Some "html") "format should be html"

    testCase "empty string returns error" <| fun _ ->
        let result = parseAlpsXml ""
        Expect.isError result "should be error"

    testCase "XML with namespace declarations" <| fun _ ->
        let xml = """<alps xmlns="http://alps.io/spec" version="1.0"><descriptor id="test"/></alps>"""
        let result = parseAlpsXml xml
        // Should handle namespace gracefully -- either parse correctly or provide clear error
        ...
]
```

**Add test file to fsproj**: Add `<Compile Include="Alps/XmlParserTests.fs" />` after `<Compile Include="Alps/JsonParserTests.fs" />` in the test fsproj. If WP02 has not been merged, add it after `<Compile Include="Alps/TypeTests.fs" />`.

- **Validation**: `dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` must pass.

## Risks & Mitigations

- **Namespace handling**: ALPS XML documents may use namespaces (edge case from spec). Using `XName.Get "localName"` without namespace should work for unnamespaced documents. For namespaced documents, use `elem.Name.LocalName` for comparisons. Test both scenarios.
- **Parallel with WP02**: Cross-format equivalence tests (T019) depend on `parseAlpsJson` from WP02. If developing in parallel, either defer these tests or ensure WP02 is available for compilation.
- **XML entity encoding**: `<doc>` content may contain XML entities (`&lt;`, `&gt;`, `&amp;`). `XDocument` handles this automatically -- `elem.Value` returns decoded text.
- **Golden file ordering**: JSON arrays and XML element order must match in golden files for structural equality comparison to work.

## Review Guidance

- Verify `XDocument` does NOT have a `use` binding (it is not IDisposable).
- Verify only `XmlException` is caught (constitution principle VII).
- Verify XML parse errors include `AlpsSourcePosition` with line/column.
- Verify cross-format equivalence test compares full AST equality (not just partial checks).
- Verify unknown XML elements are silently ignored (FR-017).
- Verify missing `type` attribute defaults to `Semantic` (FR-006).
- Run `dotnet build` and `dotnet test`.

## Activity Log

> **CRITICAL**: Activity log entries MUST be in chronological order (oldest first, newest last).

**Valid lanes**: `planned`, `doing`, `for_review`, `done`

- 2026-03-16T00:00:00Z - system - lane=planned - Prompt created.
- 2026-03-16T04:28:10Z – unknown – lane=for_review – Ready for review: ALPS XML parser with 41 tests, all passing. Cross-format equivalence tests deferred until WP02 JSON parser merges.
