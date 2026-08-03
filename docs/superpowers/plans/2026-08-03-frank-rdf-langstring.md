# Frank.Rdf: rdf:langString support

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a language-tagged string literal case to `Frank.Rdf`'s `Literal` DU (`rdf:langString`, e.g. `"Tic-tac-toe"@en`), a matching `describe { }` custom operation, and `Doc.toGraph` support, so JSON-LD expanded output can carry `{"@value": "...", "@language": "en"}`.

**Tracks:** frank-fs/frank#480

**Design doc:** `docs/superpowers/specs/2026-07-30-frank-rdf-design.md`

## Global Constraints

- `src/Frank.Rdf/` already targets `net8.0;net9.0;net10.0` — no project file changes needed.
- Every `.fs` file has a matching `.fsi` (`CLAUDE.md`). Update `RdfTypes.fsi`/`RdfTypes.fs` and `Rdf.fsi`/`Rdf.fs` together.
- `Literal` stays `[<RequireQualifiedAccess>]`.
- Test framework is Expecto, matching every other Frank.Rdf test.
- Commit directly to this task's branch when done (this repo is trunk-based — no PR needed once merged back to master by the coordinator).

## File Structure

| File | Change | Responsibility |
|---|---|---|
| `src/Frank.Rdf/RdfTypes.fsi` | Modify | Add `Literal.LangString of string * string` case with doc comment |
| `src/Frank.Rdf/RdfTypes.fs` | Modify | Same DU case, implementation |
| `src/Frank.Rdf/Rdf.fsi` | Modify | Add `propertyLangString` custom operation signature on `DescribeBuilder` |
| `src/Frank.Rdf/Rdf.fs` | Modify | Add `propertyLangString` implementation; add `Literal.LangString` branch to `toLiteralNode` |
| `test/Frank.Rdf.Tests/RdfTypesTests.fs` | Modify | Cover the new DU case if this file tests `Literal` construction directly |
| `test/Frank.Rdf.Tests/DescribeBuilderTests.fs` | Modify | Cover `propertyLangString` producing `Value.Literal(Literal.LangString(value, lang))` |
| `test/Frank.Rdf.Tests/RoundTripTests.fs` | Modify | Add a round-trip (isomorphism) test: expanded JSON-LD output contains `@language`, and re-parsing via dotNetRDF's `JsonLdParser` preserves the tag — same pattern as this file's existing tests |

---

### Task 1: Add `Literal.LangString` end to end

**Files:** see File Structure above.

**Interfaces:**
- Consumes: existing `Literal` DU (`String | Int | Bool | DateTime`), existing `DescribeBuilder` custom operations (`propertyString`, `propertyInt`, etc. — mirror their exact shape), existing `Doc.toGraph`'s private `toLiteralNode`.
- Produces: `Literal.LangString of string * string` (value, BCP47 language tag, in that order), `propertyLangString` custom operation.

**Exact changes:**

1. `RdfTypes.fsi` — in the `Literal` doc comment block, add the new case with an explanatory comment (mirror the existing terse style):

```fsharp
/// An RDF literal value.
[<RequireQualifiedAccess>]
type Literal =
    | String of string
    | Int of int
    | Bool of bool
    | DateTime of DateTimeOffset
    /// A language-tagged string (rdf:langString), e.g. "Tic-tac-toe"@en -- (value, BCP47 language tag).
    | LangString of string * string
```

2. `RdfTypes.fs` — same case, no implementation body needed (DU case):

```fsharp
[<RequireQualifiedAccess>]
type Literal =
    | String of string
    | Int of int
    | Bool of bool
    | DateTime of DateTimeOffset
    | LangString of string * string
```

3. `Rdf.fsi` — add to `DescribeBuilder`, after `PropertyDateTime`:

```fsharp
[<CustomOperation("propertyLangString")>]
member PropertyLangString: d: Description * predicate: string * value: string * language: string -> Description
```

4. `Rdf.fs` — add to `DescribeBuilder`, after `PropertyDateTime`, mirroring the existing members exactly (append to `d.Statements`, wrap in `Value.Literal`):

```fsharp
[<CustomOperation("propertyLangString")>]
member _.PropertyLangString(d: Description, predicate: string, value: string, language: string) : Description =
    { d with
        Statements = d.Statements @ [ predicate, Value.Literal(Literal.LangString(value, language)) ] }
```

5. `Rdf.fs`'s private `toLiteralNode` (inside `module Doc`) — add a branch using dotNetRDF's `CreateLiteralNode(literal, langspec)` overload (distinct from the plain-string and datatype-typed overloads already used by the other cases):

```fsharp
let private toLiteralNode (graph: Graph) (literal: Literal) : INode =
    match literal with
    | Literal.String s -> graph.CreateLiteralNode(s) :> INode
    | Literal.Int i -> i.ToLiteral(graph)
    | Literal.Bool b -> b.ToLiteral(graph)
    | Literal.DateTime dt -> dt.ToLiteral(graph)
    | Literal.LangString(value, lang) -> graph.CreateLiteralNode(value, lang) :> INode
```

**Tests to add:**

- `DescribeBuilderTests.fs`: a test asserting `describe subject { propertyLangString "schema:name" "Tic-tac-toe" "en" }` produces `Statements = [ "schema:name", Value.Literal(Literal.LangString("Tic-tac-toe", "en")) ]` — mirror the existing tests for `propertyString`/`propertyInt` in the same file exactly (same assertion style).
- `RoundTripTests.fs`: a new test in the `testList` following the existing "round-trips to an isomorphic graph for a single-subject document" pattern:
  - Build a `Doc` with one `propertyLangString "schema:name" "Tic-tac-toe" "en"` statement.
  - Assert `Doc.toJsonLd doc` contains `"@language"` and `"en"` (`Expect.stringContains`).
  - Assert `Doc.toGraph doc :> IGraph` is isomorphic (`.Equals`) with the graph obtained by parsing `Doc.toJsonLd doc` back via `JsonLdParser` (this file's existing `parseBackToGraph` helper) — same isomorphism-check pattern as the file's other round-trip tests.

**Verification:** `dotnet test test/Frank.Rdf.Tests/Frank.Rdf.Tests.fsproj` must pass on all three TFMs (`net8.0`, `net9.0`, `net10.0`) — run `dotnet build src/Frank.Rdf/Frank.Rdf.fsproj -f net8.0` / `-f net9.0` / `-f net10.0` too, since signature mismatches only surface at compile time per-TFM.
