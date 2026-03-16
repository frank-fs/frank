# Quickstart: ALPS Shared AST Migration

**Feature**: 023-alps-shared-ast-migration
**Date**: 2026-03-16

## Build & Test

```bash
# Build (multi-target)
dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj

# Run ALPS tests
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Alps"

# Run all tests (includes cross-format validator)
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj

# Verify multi-target build
dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj -f net8.0
dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj -f net9.0
dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj -f net10.0
```

## Key Files

### Source (modify)
- `src/Frank.Statecharts/Ast/Types.fs` -- Extend `AlpsMeta` DU
- `src/Frank.Statecharts/Alps/JsonParser.fs` -- Migrate to `ParseResult`/`StatechartDocument`
- `src/Frank.Statecharts/Alps/JsonGenerator.fs` -- Migrate to `StatechartDocument` input

### Source (delete)
- `src/Frank.Statecharts/Alps/Types.fs` -- Format-specific types
- `src/Frank.Statecharts/Alps/Mapper.fs` -- Bridge module

### Project files (modify)
- `src/Frank.Statecharts/Frank.Statecharts.fsproj` -- Remove deleted files from compile list
- `test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj` -- Remove deleted test files

### Tests (modify)
- `test/Frank.Statecharts.Tests/Alps/JsonParserTests.fs` -- Migrate + absorb mapper tests
- `test/Frank.Statecharts.Tests/Alps/JsonGeneratorTests.fs` -- Migrate to StatechartDocument input
- `test/Frank.Statecharts.Tests/Alps/RoundTripTests.fs` -- Migrate to StatechartDocument roundtrip

### Tests (delete)
- `test/Frank.Statecharts.Tests/Alps/TypeTests.fs` -- Tests deleted types
- `test/Frank.Statecharts.Tests/Alps/MapperTests.fs` -- Tests deleted module

### Tests (unchanged)
- `test/Frank.Statecharts.Tests/Alps/GoldenFiles.fs` -- Golden file data

## Verification Checklist

After migration, verify:

1. `dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj` succeeds (all 3 targets)
2. `dotnet test test/Frank.Statecharts.Tests/` passes all tests
3. `grep -r "AlpsDocument\|Alps\.Types\|Alps\.Mapper" src/ test/` finds zero references to deleted types
4. Parser returns `ParseResult` (not `Result<AlpsDocument, ...>`)
5. Generator accepts `StatechartDocument` (not `AlpsDocument`)
6. Roundtrip: parse -> generate -> re-parse produces structurally equal `StatechartDocument`
7. Cross-format validator tests still pass without modification

## Migration Order

The recommended implementation order (respects F# compilation dependencies):

1. Extend `AlpsMeta` in `Ast/Types.fs` (no existing code breaks -- only additions, except AlpsExtension field change)
2. Update any existing pattern matches on `AlpsExtension` outside Alps/ (if any)
3. Migrate `JsonParser.fs` to return `ParseResult` with `StatechartDocument`
4. Migrate `JsonGenerator.fs` to accept `StatechartDocument`
5. Delete `Alps/Mapper.fs` and `Alps/Types.fs`
6. Update project files
7. Migrate tests
8. Run full build and test suite
