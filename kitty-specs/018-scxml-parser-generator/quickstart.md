# Quickstart: SCXML Parser and Generator

**Feature**: 018-scxml-parser-generator

## Prerequisites

- .NET 8.0+ SDK installed
- Frank.Statecharts project builds (`dotnet build src/Frank.Statecharts`)

## Parse an SCXML Document

```fsharp
open Frank.Statecharts.Scxml.Types
open Frank.Statecharts.Scxml.Parser

let scxml = """
<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
  <state id="idle">
    <transition event="start" target="active"/>
  </state>
  <state id="active">
    <transition event="submit" cond="isValid" target="done"/>
  </state>
  <final id="done"/>
</scxml>
"""

let result = parseString scxml

match result.Document with
| Some doc ->
    printfn "Initial state: %A" doc.InitialId    // Some "idle"
    printfn "States: %d" doc.States.Length         // 3
    for state in doc.States do
        printfn "  %A (%A)" state.Id state.Kind
        for t in state.Transitions do
            printfn "    -> %A on %A [%A]" t.Targets t.Event t.Guard
| None ->
    for err in result.Errors do
        printfn "Error at %A: %s" err.Position err.Description
```

## Generate SCXML from Types

```fsharp
open Frank.Statecharts.Scxml.Types
open Frank.Statecharts.Scxml.Generator

let doc =
    { Name = None
      InitialId = Some "idle"
      DatamodelType = None
      Binding = None
      States =
        [ { Id = Some "idle"; Kind = Simple; InitialId = None
            Transitions = [ { Event = Some "start"; Guard = None; Targets = ["active"]
                              TransitionType = External; Position = None } ]
            Children = []; DataEntries = []; HistoryNodes = []; InvokeNodes = []
            Position = None }
          { Id = Some "active"; Kind = Simple; InitialId = None
            Transitions = [ { Event = Some "submit"; Guard = Some "isValid"; Targets = ["done"]
                              TransitionType = External; Position = None } ]
            Children = []; DataEntries = []; HistoryNodes = []; InvokeNodes = []
            Position = None }
          { Id = Some "done"; Kind = Final; InitialId = None
            Transitions = []; Children = []; DataEntries = []
            HistoryNodes = []; InvokeNodes = []; Position = None } ]
      DataEntries = []
      Position = None }

let xml = generate doc
printfn "%s" xml
// Output:
// <scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0" initial="idle">
//   <state id="idle">
//     <transition event="start" target="active" />
//   </state>
//   <state id="active">
//     <transition event="submit" cond="isValid" target="done" />
//   </state>
//   <final id="done" />
// </scxml>
```

## Roundtrip Verification

```fsharp
let result1 = parseString scxml
let generated = generate result1.Document.Value
let result2 = parseString generated

// Structural equality check
assert (result1.Document.Value = result2.Document.Value)
```

## Build and Test

```bash
# Build (multi-target)
dotnet build src/Frank.Statecharts

# Run SCXML-specific tests
dotnet test test/Frank.Statecharts.Tests --filter "Scxml"
```
