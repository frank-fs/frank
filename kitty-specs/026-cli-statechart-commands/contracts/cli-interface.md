# CLI Interface Contract: Statechart Commands

**Feature**: 026-cli-statechart-commands
**Date**: 2026-03-16

## Command Group

```
frank-cli statechart <subcommand> [options]
```

Parent command: `statechart`
Description: "Statechart pipeline commands: extract, generate, validate, and import state machine artifacts"

---

## Subcommand: extract

```
frank-cli statechart extract <assembly> [--output-format text|json]
```

### Arguments

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `assembly` | `string` (positional) | Yes | - | Path to compiled .NET assembly (.dll) |
| `--output-format` | `string` (option) | No | `text` | Output format: `text` or `json` |

### Exit Codes

| Code | Condition |
|------|-----------|
| 0 | Success (including when no state machines found) |
| 1 | Assembly load failure, startup error, or other error |

### JSON Output Schema

```json
{
  "status": "ok",
  "stateMachines": [
    {
      "routeTemplate": "/games/{id}",
      "resourceSlug": "games",
      "initialState": "WaitingForPlayers",
      "states": [
        {
          "name": "WaitingForPlayers",
          "isFinal": false,
          "description": null,
          "allowedMethods": ["GET", "POST"]
        }
      ],
      "guardNames": ["IsPlayersTurn", "IsValidMove"]
    }
  ]
}
```

When no state machines found:
```json
{
  "status": "ok",
  "stateMachines": []
}
```

### Text Output Format

```
Statechart Extract Summary

Found 2 state machine(s):

  /games/{id} (games)
    Initial state: WaitingForPlayers
    States: WaitingForPlayers, InProgress, Won, Draw
    Guards: IsPlayersTurn, IsValidMove

  /orders/{id} (orders)
    Initial state: Pending
    States: Pending, Processing, Shipped, Delivered
    Guards: (none)
```

When no state machines found:
```
No state machines found in the assembly.
```

---

## Subcommand: generate

```
frank-cli statechart generate --format <format> <assembly> [--output <dir>] [--resource <name>] [--output-format text|json]
```

### Arguments

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `assembly` | `string` (positional) | Yes | - | Path to compiled .NET assembly (.dll) |
| `--format` | `string` (option) | Yes | - | Target notation: `wsd`, `alps`, `scxml`, `smcat`, `xstate`, `all` |
| `--output` | `string` (option) | No | - | Output directory for generated files |
| `--resource` | `string` (option) | No | - | Filter to a single resource by slug |
| `--output-format` | `string` (option) | No | `text` | Status output format: `text` or `json` |

### Exit Codes

| Code | Condition |
|------|-----------|
| 0 | Success (including when no state machines found) |
| 1 | Assembly load failure, invalid format, or other error |

### Stdout Output (no --output)

When writing to stdout, format and resource headers separate each artifact:

```
=== games (WSD) ===
participant WaitingForPlayers
participant InProgress
...

=== games (ALPS) ===
{
  "alps": { ... }
}
```

### File Output (--output)

Files written to the output directory using naming convention:
- `{resourceSlug}.wsd`
- `{resourceSlug}.alps.json`
- `{resourceSlug}.scxml`
- `{resourceSlug}.smcat`
- `{resourceSlug}.xstate.json`

### JSON Status Output (--output-format json)

```json
{
  "status": "ok",
  "artifacts": [
    {
      "resourceSlug": "games",
      "format": "wsd",
      "outputPath": "./specs/games.wsd"
    }
  ]
}
```

---

## Subcommand: validate

```
frank-cli statechart validate <spec-file> [<spec-file>...] <assembly> [--output-format text|json]
```

### Arguments

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `spec-file(s)` | `string[]` (positional) | Yes | - | One or more spec files to validate |
| `assembly` | `string` (positional, last) | Yes | - | Path to compiled .NET assembly (.dll) |
| `--output-format` | `string` (option) | No | `text` | Output format: `text` or `json` |

Note: The last positional argument is treated as the assembly path (must end in `.dll`). All preceding positional arguments are spec files.

### Exit Codes

| Code | Condition |
|------|-----------|
| 0 | All validation checks pass |
| 1 | One or more validation failures |
| 2 | Invalid input (unsupported format, file not found, assembly load failure) |

### JSON Output Schema

```json
{
  "status": "ok",
  "isValid": true,
  "totalChecks": 15,
  "totalSkipped": 2,
  "totalFailures": 0,
  "checks": [
    {
      "name": "state-name-agreement-wsd-alps",
      "status": "pass",
      "reason": null
    },
    {
      "name": "no-orphan-targets-wsd",
      "status": "skip",
      "reason": "Required format XState not present"
    }
  ],
  "failures": []
}
```

When validation fails:
```json
{
  "status": "invalid",
  "isValid": false,
  "totalChecks": 15,
  "totalSkipped": 0,
  "totalFailures": 1,
  "checks": [...],
  "failures": [
    {
      "formats": ["wsd", "alps"],
      "entityType": "state",
      "expected": "Review",
      "actual": "(absent)",
      "description": "State 'Review' present in WSD but missing from ALPS"
    }
  ]
}
```

### Text Output Format

```
Validation: PASS
Total checks: 15 | Skipped: 2 | Failures: 0

Checks:
  [PASS] state-name-agreement-wsd-alps
  [PASS] event-name-agreement-wsd-alps
  [SKIP] no-orphan-targets-xstate (Required format XState not present)
```

When validation fails:
```
Validation: FAIL
Total checks: 15 | Skipped: 0 | Failures: 1

Failures:
  [FAIL] state-name-agreement-wsd-alps
    Formats: WSD, ALPS
    Entity: state
    Expected: Review
    Actual: (absent)
    Description: State 'Review' present in WSD but missing from ALPS
```

---

## Subcommand: import

```
frank-cli statechart import <spec-file> [--format wsd|alps|scxml|smcat|xstate] [--output-format text|json]
```

### Arguments

| Name | Type | Required | Default | Description |
|------|------|----------|---------|-------------|
| `spec-file` | `string` (positional) | Yes | - | Path to spec file to parse |
| `--format` | `string` (option) | No | - | Notation format override (for ambiguous `.json` files): `wsd`, `alps`, `scxml`, `smcat`, `xstate` |
| `--output-format` | `string` (option) | No | `json` | Output format: `text` or `json` (default json for LLM consumption) |

### Exit Codes

| Code | Condition |
|------|-----------|
| 0 | Parse succeeded (warnings OK) |
| 1 | Parse errors present (best-effort document still output) |
| 2 | Invalid input (unsupported format, file not found) |

### JSON Output Schema

```json
{
  "status": "ok",
  "sourceFormat": "xstate",
  "document": {
    "title": "TicTacToe",
    "initialStateId": "WaitingForPlayers",
    "states": [
      {
        "identifier": "WaitingForPlayers",
        "kind": "regular",
        "label": null,
        "children": []
      }
    ],
    "transitions": [
      {
        "source": "WaitingForPlayers",
        "target": "InProgress",
        "event": "START",
        "guard": null,
        "action": null
      }
    ]
  },
  "errors": [],
  "warnings": []
}
```

Note: The `document` field is a flattened view of `StatechartDocument` suitable for LLM consumption. States and transitions are extracted from `Elements` and presented as flat arrays.

### Text Output Format

```
Import: game.xstate.json (XState JSON)

States:
  WaitingForPlayers (initial)
  InProgress
  Won (final)
  Draw (final)

Transitions:
  WaitingForPlayers -> InProgress [START]
  InProgress -> Won [WIN]
  InProgress -> Draw [DRAW]

Errors: 0 | Warnings: 0
```

---

## Error Output (all commands)

### JSON Error
```json
{
  "status": "error",
  "message": "Failed to load assembly: Could not find file '/path/to/missing.dll'"
}
```

### Text Error
```
Error: Failed to load assembly: Could not find file '/path/to/missing.dll'
```

---

## Supported File Extensions

| Extension | Format | Detected By |
|-----------|--------|-------------|
| `.wsd` | WSD | Single extension |
| `.alps.json` | ALPS | Compound extension (checked before `.json`) |
| `.scxml` | SCXML | Single extension |
| `.smcat` | smcat | Single extension |
| `.xstate.json` | XState JSON | Compound extension (checked before `.json`) |
| `.json` | Ambiguous | Requires `--format` flag |
| `.dll` | Assembly | Used to distinguish assembly from spec files in validate |
