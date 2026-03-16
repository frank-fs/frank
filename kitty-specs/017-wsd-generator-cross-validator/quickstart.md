# Quickstart: WSD Generator

**Feature**: 017-wsd-generator-cross-validator
**Date**: 2026-03-15

## Generate WSD from StateMachineMetadata

```fsharp
open Frank.Statecharts
open Frank.Statecharts.Wsd.Types
open Frank.Statecharts.Wsd.Generator
open Frank.Statecharts.Wsd.Serializer

// Given a StateMachineMetadata from endpoint metadata:
let metadata : StateMachineMetadata = (* obtained from endpoint.Metadata *)

let options = { ResourceName = "turnstile" }

match generate options metadata with
| Ok diagram ->
    let wsdText = serialize diagram
    printfn "%s" wsdText
    // Output:
    // title turnstile
    //
    // participant Locked
    // participant Unlocked
    //
    // Locked->Unlocked: POST
    // Unlocked->Locked: POST
| Error (UnrecognizedMachineType typeName) ->
    eprintfn "Cannot generate WSD: unrecognized machine type '%s'" typeName
| Error (NoStatesFound resourceName) ->
    eprintfn "Cannot generate WSD: no states found for resource '%s'" resourceName
```

## Roundtrip Verification

```fsharp
open Frank.Statecharts.Wsd.Parser

// Generate WSD text
let wsdText = generate options metadata |> Result.map serialize

match wsdText with
| Ok text ->
    // Parse it back through the WSD parser
    let parseResult = parseWsd text

    // Verify no parse errors (roundtrip fidelity)
    assert (parseResult.Errors.IsEmpty)

    // Compare participants
    let participants = parseResult.Diagram.Participants |> List.map (fun p -> p.Name)
    printfn "States: %A" participants

    // Compare messages
    let messages =
        parseResult.Diagram.Elements
        |> List.choose (function MessageElement m -> Some (m.Sender, m.Receiver, m.Label) | _ -> None)
    printfn "Transitions: %A" messages
| Error _ -> ()
```

## Serialize an Existing Diagram AST

```fsharp
open Frank.Statecharts.Wsd.Types
open Frank.Statecharts.Wsd.Serializer

// Construct a Diagram programmatically
let syntheticPos = { Line = 0; Column = 0 }

let diagram =
    { Title = Some "My State Machine"
      AutoNumber = false
      Participants =
        [ { Name = "Idle"; Alias = None; Explicit = true; Position = syntheticPos }
          { Name = "Active"; Alias = None; Explicit = true; Position = syntheticPos } ]
      Elements =
        [ ParticipantDecl { Name = "Idle"; Alias = None; Explicit = true; Position = syntheticPos }
          ParticipantDecl { Name = "Active"; Alias = None; Explicit = true; Position = syntheticPos }
          MessageElement
            { Sender = "Idle"
              Receiver = "Active"
              ArrowStyle = Solid
              Direction = Forward
              Label = "start"
              Parameters = []
              Position = syntheticPos } ] }

let text = serialize diagram
// Output:
// title My State Machine
//
// participant Idle
// participant Active
//
// Idle->Active: start
```

## Build and Test

```bash
# Build all targets
dotnet build src/Frank.Statecharts/Frank.Statecharts.fsproj

# Run tests
dotnet test test/Frank.Statecharts.Tests/Frank.Statecharts.Tests.fsproj
```
