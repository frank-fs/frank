# Quickstart: SCXML Shared AST Migration

**Feature**: 024-scxml-shared-ast-migration
**Date**: 2026-03-16

## Prerequisites

- .NET SDK 10.0+ installed (for multi-target build)
- Frank repository cloned with spec 020 (shared AST) already merged

## Build & Test

```bash
# Build the library across all targets
dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj

# Run the SCXML-specific tests
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Scxml"

# Run all statecharts tests (including cross-format validator)
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj

# Verify no references to deleted types
grep -r "ScxmlDocument\|ScxmlParseResult\|ScxmlStateKind" src/ test/ --include="*.fs" | grep -v "obj/"
# Should return no results

# Verify Mapper.fs is deleted
test ! -f src/Frank.Statecharts/Scxml/Mapper.fs && echo "Mapper.fs deleted" || echo "ERROR: Mapper.fs still exists"

# Verify no Mapper references in project file
grep -c "Scxml/Mapper.fs" src/Frank.Statecharts/Frank.Statecharts.fsproj
# Should return 0
```

## Migration Verification Checklist

- [ ] `parseString` returns `Ast.ParseResult` (not `ScxmlParseResult`)
- [ ] `parseReader` returns `Ast.ParseResult`
- [ ] `parseStream` returns `Ast.ParseResult`
- [ ] `generate` accepts `StatechartDocument` (not `ScxmlDocument`)
- [ ] `generateTo` accepts `StatechartDocument`
- [ ] `Scxml/Mapper.fs` file deleted
- [ ] `Scxml/Mapper.fs` removed from `.fsproj`
- [ ] `ScxmlDocument` type deleted from `Types.fs`
- [ ] `ScxmlState` type deleted from `Types.fs`
- [ ] `ScxmlTransition` type deleted from `Types.fs`
- [ ] `ScxmlParseResult` type deleted from `Types.fs`
- [ ] `ScxmlStateKind` type deleted from `Types.fs`
- [ ] `ScxmlTransitionType` retained in `Types.fs`
- [ ] `ScxmlHistoryKind` retained in `Types.fs`
- [ ] `SourcePosition` retained in `Types.fs`
- [ ] 6 new/modified `ScxmlMeta` cases in `Ast/Types.fs`
- [ ] All round-trip tests pass
- [ ] Cross-format validator tests pass
- [ ] Build succeeds on net8.0, net9.0, net10.0

## Key Files

| File | Action |
|---|---|
| `src/Frank.Statecharts/Ast/Types.fs` | Modify: extend `ScxmlMeta` DU |
| `src/Frank.Statecharts/Scxml/Types.fs` | Modify: delete document/state/transition types |
| `src/Frank.Statecharts/Scxml/Parser.fs` | Modify: return `Ast.ParseResult` directly |
| `src/Frank.Statecharts/Scxml/Generator.fs` | Modify: accept `StatechartDocument` |
| `src/Frank.Statecharts/Scxml/Mapper.fs` | Delete |
| `src/Frank.Statecharts/Frank.Statecharts.fsproj` | Modify: remove Mapper.fs compile entry |
| `test/Frank.Statecharts.Tests/Scxml/TypeTests.fs` | Modify: test retained types only |
| `test/Frank.Statecharts.Tests/Scxml/ParserTests.fs` | Modify: use Ast types |
| `test/Frank.Statecharts.Tests/Scxml/GeneratorTests.fs` | Modify: use Ast types |
| `test/Frank.Statecharts.Tests/Scxml/RoundTripTests.fs` | Modify: use Ast types |
| `test/Frank.Statecharts.Tests/Scxml/ErrorTests.fs` | Modify: use Ast types |
