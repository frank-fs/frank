# Research: Frank.Datastar.Oxpecker Sample

**Date**: 2026-01-27
**Feature**: 004-datastar-oxpecker-sample

## Research Summary

This sample has minimal unknowns since it replicates existing implementations. Research focuses on Oxpecker.ViewEngine API patterns for Datastar integration.

---

## 1. Oxpecker.ViewEngine HTML Generation

**Decision**: Use `Oxpecker.ViewEngine.Render.toString` for synchronous HTML string generation.

**Rationale**:
- Oxpecker.ViewEngine's `Render.toString` function converts any `HtmlElement` to a string synchronously
- This matches Datastar SSE pattern where small HTML fragments are sent
- Simpler than Hox's async `Render.asString` - no `task {}` wrapper needed for rendering

**Alternatives Considered**:
- `Render.toBytes` - Not needed, Datastar.patchElements accepts string
- Custom StringBuilder approach - Unnecessary, `Render.toString` handles pooling internally

**Code Pattern**:
```fsharp
open Oxpecker.ViewEngine
open Oxpecker.ViewEngine.Render

let view = div(id="contact-view") { p() { "Hello" } }
let html = toString view  // Synchronous, returns string
do! Datastar.patchElements html ctx
```

---

## 2. Datastar Custom Attributes

**Decision**: Use `.attr()` extension method for `data-*` attributes.

**Rationale**:
- Oxpecker.ViewEngine provides `.attr(name, value)` for arbitrary attributes
- Also provides `.data(name, value)` which auto-prefixes with `data-`
- Datastar attributes like `data-on:click`, `data-bind:firstName` require colon in name
- `.attr("data-on:click", "@get('/path')")` works correctly

**Alternatives Considered**:
- `.data()` method - Works for simple cases but Datastar uses colons in attribute names
- Creating custom extension types - Over-engineering for a sample

**Code Pattern**:
```fsharp
// For data-on:click attribute
button().attr("data-on:click", "@get('/contacts/1/edit')") { "Edit" }

// For data-bind:firstName attribute
input(type'="text").attr("data-bind:firstName", null)

// For data-signals (JSON object)
div(id="form").attr("data-signals", "{'firstName': '', 'lastName': ''}") { ... }
```

---

## 3. Oxpecker.ViewEngine Syntax Patterns

**Decision**: Follow computation expression patterns from VIEW_ENGINE_COMPARISON.md.

**Rationale**: The comparison document provides clear examples that align with Frank's style.

**Key Patterns**:

| Pattern | Oxpecker.ViewEngine Syntax |
|---------|---------------------------|
| Element with ID | `div(id="foo") { ... }` |
| Element with class | `div(class'="card") { ... }` (note apostrophe - `class'`) |
| Element with both | `div(id="foo", class'="bar") { ... }` |
| Text content | `p() { "text" }` or `p() { $"Hello {name}" }` |
| Void element | `input(type'="text")` (no braces) |
| Loop | `for item in items do li() { item.Name }` |
| Conditional | `if condition then div() { ... }` |
| Fragment | `Fragment() { child1; child2 }` |

**Note on Reserved Words**:
- `class` is F# keyword, use `class'`
- `type` is F# keyword, use `type'`

---

## 4. Converting Hox Patterns to Oxpecker

**Decision**: Direct 1:1 translation using patterns below.

| Hox Pattern | Oxpecker.ViewEngine Equivalent |
|-------------|-------------------------------|
| `h("div#id.class", [...])` | `div(id="id", class'="class") { ... }` |
| `h("button [data-on:click=@get('/x')]", [...])` | `button().attr("data-on:click", "@get('/x')") { ... }` |
| `h("input [type=text] [data-bind:x]", [])` | `input(type'="text").attr("data-bind:x", null)` |
| `Text "hello"` | `"hello"` (implicit in computation expression) |
| `fragment [...]` | `Fragment() { ... }` or inline children |

---

## 5. Project Configuration

**Decision**: Reference Oxpecker.ViewEngine 2.x from NuGet.

**Rationale**:
- Version 2.0.0 is published on NuGet and targets .NET 10.0
- No need for local project reference (unlike Frank.Datastar)

**fsproj Pattern**:
```xml
<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
  </PropertyGroup>

  <ItemGroup>
    <Compile Include="Program.fs" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\Frank.Datastar\Frank.Datastar.fsproj" />
  </ItemGroup>

  <ItemGroup>
    <PackageReference Include="Frank" Version="6.*" />
    <PackageReference Include="Oxpecker.ViewEngine" Version="2.*" />
  </ItemGroup>
</Project>
```

---

## Research Conclusion

All technical decisions are resolved. Implementation can proceed with:
1. Standard Oxpecker.ViewEngine computation expressions
2. `.attr()` extension for Datastar-specific attributes
3. `Render.toString` for synchronous HTML generation
4. Direct translation from Hox sample patterns
