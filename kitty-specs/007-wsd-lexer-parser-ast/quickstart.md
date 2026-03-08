# Quickstart: WSD Parser

**Branch**: `007-wsd-lexer-parser-ast` | **Date**: 2026-03-07

The WSD parser is internal to `Frank.Statecharts`. These examples show how other modules within the assembly consume it.

## Parse a WSD String

```fsharp
open Frank.Statecharts.Wsd.Parser

let wsd = """
title Onboarding Flow
participant Client
participant API
participant DB

Client->API: createUser(name, email)
API->DB: insertUser(name, email)
DB->-API: userId
API->-Client: 201 Created
"""

let result = parseWsd wsd
// result.Diagram contains the typed AST
// result.Errors is empty for valid input
// result.Warnings may contain soft diagnostics
```

## Walk the AST

```fsharp
open Frank.Statecharts.Wsd.Types

let printElement (elem: DiagramElement) =
    match elem with
    | ParticipantDecl p ->
        printfn "Participant: %s (explicit=%b)" p.Name p.Explicit
    | MessageElement m ->
        printfn "%s %s%s %s: %s"
            m.Sender
            (match m.ArrowStyle with Solid -> "-" | Dashed -> "--")
            (match m.Direction with Forward -> ">" | Deactivating -> ">-")
            m.Receiver
            m.Label
        if not m.Parameters.IsEmpty then
            printfn "  params: %s" (String.concat ", " m.Parameters)
    | NoteElement n ->
        printfn "Note %A %s: %s"
            n.NotePosition n.Target n.Content
        match n.Guard with
        | Some g ->
            for (k, v) in g.Pairs do
                printfn "  guard: %s=%s" k v
        | None -> ()
    | GroupElement g ->
        printfn "Group %A (%d branches)" g.Kind g.Branches.Length
    | TitleDirective (title, _) ->
        printfn "Title: %s" title
    | AutoNumberDirective _ ->
        printfn "AutoNumber enabled"

// Walk all elements
for elem in result.Diagram.Elements do
    printElement elem
```

## Handle Warnings and Errors

```fsharp
let result = parseWsd someInput

// Check for hard errors (unparseable syntax)
if not result.Errors.IsEmpty then
    printfn "Parse failed with %d error(s):" result.Errors.Length
    for err in result.Errors do
        printfn "  [%d:%d] %s" err.Position.Line err.Position.Column err.Description
        printfn "    Expected: %s" err.Expected
        printfn "    Found: %s" err.Found
        printfn "    Try: %s" err.CorrectiveExample

// Check for soft warnings (valid but may not map to statecharts)
if not result.Warnings.IsEmpty then
    printfn "Parse succeeded with %d warning(s):" result.Warnings.Length
    for warn in result.Warnings do
        printfn "  [%d:%d] %s" warn.Position.Line warn.Position.Column warn.Description
        match warn.Suggestion with
        | Some s -> printfn "    Suggestion: %s" s
        | None -> ()

// Use the AST even if warnings exist
// The Diagram is always present (partial on errors, complete on warnings-only)
let diagram = result.Diagram
printfn "Parsed %d elements, %d participants"
    diagram.Elements.Length
    diagram.Participants.Length
```

## Parse Guard Annotations

```fsharp
let wsd = """
participant Player
participant Board

note over Player: [guard: role=PlayerX, state=XTurn]
Player->Board: makeMove(position)
"""

let result = parseWsd wsd

// Find notes with guard annotations
for elem in result.Diagram.Elements do
    match elem with
    | NoteElement { Guard = Some guard; Target = target } ->
        printfn "Guard on %s:" target
        for (key, value) in guard.Pairs do
            printfn "  %s = %s" key value
        // Output:
        //   Guard on Player:
        //     role = PlayerX
        //     state = XTurn
    | _ -> ()
```

## Parse Grouping Blocks

```fsharp
let wsd = """
participant Client
participant API

alt success
    Client->API: getResource()
    API->-Client: 200 OK
else not found
    API->-Client: 404 Not Found
end
"""

let result = parseWsd wsd

for elem in result.Diagram.Elements do
    match elem with
    | GroupElement { Kind = Alt; Branches = branches } ->
        for branch in branches do
            printfn "Branch: %s" (branch.Condition |> Option.defaultValue "(default)")
            printfn "  %d child elements" branch.Elements.Length
        // Output:
        //   Branch: success
        //     2 child elements
        //   Branch: not found
        //     1 child elements
    | _ -> ()
```
