# Quickstart: smcat Parser and Generator

**Feature**: 013-smcat-parser-generator
**Date**: 2026-03-15

## Build & Test

```bash
# Build (multi-target)
dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj

# Run smcat tests only
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj --filter "Smcat"

# Run all statechart tests
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
```

## File Ordering in .fsproj

smcat source files must be added to `Frank.Statecharts.fsproj` after the WSD files and before `Types.fs` (the runtime types). The F# compiler requires files in dependency order:

```xml
<!-- Existing WSD files -->
<Compile Include="Wsd/Types.fs" />
<Compile Include="Wsd/Lexer.fs" />
<Compile Include="Wsd/GuardParser.fs" />
<Compile Include="Wsd/Parser.fs" />
<!-- New smcat files -->
<Compile Include="Smcat/Types.fs" />
<Compile Include="Smcat/Lexer.fs" />
<Compile Include="Smcat/LabelParser.fs" />
<Compile Include="Smcat/Parser.fs" />
<Compile Include="Smcat/Generator.fs" />
<!-- Smcat/Mapper.fs added when spec 020 lands -->
<!-- Existing runtime files -->
<Compile Include="Types.fs" />
```

Test files in `Frank.Statecharts.Tests.fsproj`:

```xml
<!-- Existing WSD test files -->
<Compile Include="Wsd/GuardParserTests.fs" />
<Compile Include="Wsd/ParserTests.fs" />
<Compile Include="Wsd/GroupingTests.fs" />
<Compile Include="Wsd/ErrorTests.fs" />
<Compile Include="Wsd/RoundTripTests.fs" />
<!-- New smcat test files -->
<Compile Include="Smcat/LexerTests.fs" />
<Compile Include="Smcat/LabelParserTests.fs" />
<Compile Include="Smcat/ParserTests.fs" />
<Compile Include="Smcat/ErrorTests.fs" />
<Compile Include="Smcat/GeneratorTests.fs" />
<Compile Include="Smcat/RoundTripTests.fs" />
```

## Key Patterns from WSD (Follow These)

### Module declaration

```fsharp
module internal Frank.Statecharts.Smcat.Lexer
// or
module internal Frank.Statecharts.Smcat.Parser
```

### Lexer pattern (mutable scanning)

```fsharp
let tokenize (source: string) : Token list =
    let len = source.Length
    let mutable pos = 0
    let mutable line = 1
    let mutable col = 1
    let tokens = ResizeArray<Token>()
    // ... scanning loop ...
    tokens |> Seq.toList
```

### Parser state pattern

```fsharp
type ParserState =
    { Tokens: Token array
      mutable Position: int
      mutable Elements: SmcatElement list
      mutable Errors: ParseFailure list
      mutable Warnings: ParseWarning list
      mutable ErrorLimitReached: bool
      MaxErrors: int }
```

### Test pattern (Expecto)

```fsharp
module Smcat.LexerTests

open Expecto
open Frank.Statecharts.Smcat.Types
open Frank.Statecharts.Smcat.Lexer

[<Tests>]
let basicTests =
    testList "Smcat.Lexer.Basic"
        [ testCase "transition arrow" <| fun _ ->
              let kinds = tokenKindsNoEof "a => b"
              Expect.equal kinds [ Identifier "a"; TransitionArrow; Identifier "b" ] "arrow" ]
```

## smcat Example for Testing

```
# Simple onboarding state machine
initial => home: start;
home => WIP: begin;
WIP => customerData: collectCustomerData [isValid] / logAction;
customerData => final: complete;
```

Expected AST elements:
1. `TransitionElement { Source = "initial"; Target = "home"; Label = Some { Event = Some "start"; Guard = None; Action = None } }`
2. `TransitionElement { Source = "home"; Target = "WIP"; Label = Some { Event = Some "begin"; Guard = None; Action = None } }`
3. `TransitionElement { Source = "WIP"; Target = "customerData"; Label = Some { Event = Some "collectCustomerData"; Guard = Some "isValid"; Action = Some "logAction" } }`
4. `TransitionElement { Source = "customerData"; Target = "final"; Label = Some { Event = Some "complete"; Guard = None; Action = None } }`

State types inferred:
- `"initial"` -> `StateType.Initial`
- `"home"`, `"WIP"`, `"customerData"` -> `StateType.Regular`
- `"final"` -> `StateType.Final`
