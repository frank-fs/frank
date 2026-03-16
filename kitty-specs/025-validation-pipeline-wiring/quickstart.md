# Quickstart: Validation Pipeline

## Basic Usage -- Validate Multiple Format Sources

```fsharp
open Frank.Statecharts.Validation

let wsdSource = """
participant idle
participant playerX
participant playerO
participant gameOver
idle -> playerX: start
playerX -> playerO: move
playerO -> playerX: move
playerX -> gameOver: win
playerO -> gameOver: win
"""

let smcatSource = """
idle => playerX: start;
playerX => playerO: move;
playerO => playerX: move;
playerX => gameOver: win;
playerO => gameOver: win;
"""

// Call the pipeline with format-tagged source pairs
let result = Pipeline.validateSources [
    (Wsd, wsdSource)
    (Smcat, smcatSource)
]

// Check for pipeline-level errors (duplicate formats, unsupported formats)
if not (List.isEmpty result.Errors) then
    printfn "Pipeline errors: %A" result.Errors

// Check per-format parse results
for parseResult in result.ParseResults do
    if parseResult.Succeeded then
        printfn "%A: parsed successfully" parseResult.Format
    else
        printfn "%A: %d parse errors" parseResult.Format (List.length parseResult.Errors)

// Check validation report
if result.Report.TotalFailures = 0 then
    printfn "All %d checks passed (%d skipped)" result.Report.TotalChecks result.Report.TotalSkipped
else
    printfn "%d failures:" result.Report.TotalFailures
    for failure in result.Report.Failures do
        printfn "  [%s] %s" failure.EntityType failure.Description
```

## Single Format Validation

```fsharp
// Works with a single format -- self-consistency rules run, cross-format rules skipped
let result = Pipeline.validateSources [ (Wsd, wsdSource) ]

// result.Report.TotalSkipped will be > 0 (cross-format rules skipped)
// result.Report.TotalFailures reflects self-consistency only
```

## Custom Validation Rules

```fsharp
open Frank.Statecharts.Ast

// Define a custom rule (FR-014)
let noIsolatedStatesStrict : ValidationRule =
    { Name = "Strict: no isolated states"
      RequiredFormats = Set.empty
      Check = fun artifacts ->
          let checks =
              artifacts
              |> List.collect (fun a ->
                  let stateIds = AstHelpers.stateIdentifiers a.Document
                  let transitions = AstHelpers.allTransitions a.Document
                  let sources = transitions |> List.map _.Source |> Set.ofList
                  let targets = transitions |> List.choose _.Target |> Set.ofList
                  let connected = Set.union sources targets
                  let isolated = stateIds - connected
                  isolated
                  |> Set.toList
                  |> List.map (fun s ->
                      { Name = sprintf "Isolated state '%s' (%A)" s a.Format
                        Status = Fail  // FAIL, not just warning
                        Reason = Some (sprintf "State '%s' has no transitions" s) }))
          (checks, []) }

// Pass custom rules alongside built-in rules
let result = Pipeline.validateSourcesWithRules [ noIsolatedStatesStrict ] [
    (Wsd, wsdSource)
    (Smcat, smcatSource)
]
```

## Edge Cases

```fsharp
// Empty input: returns valid result with empty report
let empty = Pipeline.validateSources []
// empty.Report.TotalChecks = 0, empty.Errors = []

// Duplicate format: returns pipeline error
let dup = Pipeline.validateSources [ (Wsd, src1); (Wsd, src2) ]
// dup.Errors = [ DuplicateFormat Wsd ]

// Unsupported format: returns pipeline error
let unsupported = Pipeline.validateSources [ (XState, src) ]
// unsupported.Errors = [ UnsupportedFormat XState ]
```

## Integration with frank-cli

The CLI reads files from disk and delegates to the pipeline:

```fsharp
// CLI reads files, determines format from extension, calls pipeline
let sources =
    files
    |> List.map (fun file ->
        let tag = formatTagFromExtension (Path.GetExtension file)
        let content = File.ReadAllText file
        (tag, content))

let result = Pipeline.validateSources sources

// CLI is responsible for formatting the result as text or JSON output
```
