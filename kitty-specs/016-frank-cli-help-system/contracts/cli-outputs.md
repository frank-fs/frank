# CLI Output Contracts: frank-cli Help System

**Date**: 2026-03-15
**Feature**: 016-frank-cli-help-system

## Contract 1: Enriched Per-Command Help (--help)

### Text Output (default)

When `frank-cli <command> --help` is invoked, the output includes standard System.CommandLine help followed by appended sections:

```
Description:
  Extract semantic definitions from F# source

Usage:
  frank-cli extract [options]

Options:
  --project <project> (REQUIRED)  Path to .fsproj file
  --base-uri <base-uri> (REQUIRED) Base URI for the ontology
  --vocabularies <vocabularies>    Vocabulary namespaces to align [default: schema.org]
  --format <format>                Output format (text|json) [default: text]
  -?, -h, --help                   Show help and usage information

WORKFLOW
  Step 1 of 5 (required)
  Prerequisites: (none - this is the first step)
  Next steps: clarify, validate

EXAMPLES
  frank-cli extract --project MyApp/MyApp.fsproj --base-uri http://example.org/
    Extract semantic definitions from the MyApp project.

  frank-cli extract --project MyApp/MyApp.fsproj --base-uri http://example.org/ --vocabularies schema.org,foaf
    Extract with multiple vocabulary alignments.
```

When `--context` is also active (`frank-cli --context extract --help`):

```
[...standard help + WORKFLOW + EXAMPLES as above...]

CONTEXT
  The extract command analyzes F# source code to derive an OWL ontology and SHACL
  shapes. It maps F# record types to OWL classes, record fields to OWL properties,
  and route definitions to resource identities. The extraction state is saved to
  obj/frank-cli/state.json for use by subsequent commands (clarify, validate, compile).
```

### Section Format Rules

- Section headers (WORKFLOW, EXAMPLES, CONTEXT) are uppercase, no colon
- One blank line before each section header
- WORKFLOW section always shows step number, total steps, required/optional, prerequisites, and next steps
- EXAMPLES section shows invocation indented by 2 spaces, description indented by 4 spaces on next line
- CONTEXT section is free-form paragraph text indented by 2 spaces

## Contract 2: Help Subcommand Output

### `frank-cli help` (no arguments) -- Text

```
frank-cli: Semantic resource extraction for Frank applications

COMMANDS
  extract     Extract semantic definitions from F# source
  clarify     Identify ambiguities requiring human input
  validate    Validate completeness and consistency of extracted definitions
  diff        Compare current extraction state with a previous snapshot
  compile     Generate OWL/XML and SHACL artifacts from extraction state
  status      Show project extraction and compilation status
  help        Show help topics and command documentation

TOPICS
  workflows   End-to-end guide to the extraction pipeline
  concepts    Frank's semantic model: F# types, OWL, and SHACL

Use 'frank-cli help <command>' for detailed help on a command.
Use 'frank-cli help <topic>' for topic documentation.
```

### `frank-cli help` (no arguments) -- JSON

```json
{
  "commands": [
    {
      "name": "extract",
      "summary": "Extract semantic definitions from F# source"
    },
    {
      "name": "clarify",
      "summary": "Identify ambiguities requiring human input"
    }
  ],
  "topics": [
    {
      "name": "workflows",
      "summary": "End-to-end guide to the extraction pipeline"
    },
    {
      "name": "concepts",
      "summary": "Frank's semantic model: F# types, OWL, and SHACL"
    }
  ]
}
```

### `frank-cli help <command>` -- Text

Same output as `frank-cli <command> --help --context` (full enriched help with context section always included).

### `frank-cli help <command>` -- JSON

```json
{
  "name": "extract",
  "summary": "Extract semantic definitions from F# source",
  "examples": [
    {
      "invocation": "frank-cli extract --project MyApp/MyApp.fsproj --base-uri http://example.org/",
      "description": "Extract semantic definitions from the MyApp project."
    }
  ],
  "workflow": {
    "stepNumber": 1,
    "totalSteps": 5,
    "isOptional": false,
    "prerequisites": [],
    "nextSteps": ["clarify", "validate"]
  },
  "context": "The extract command analyzes F# source code..."
}
```

### `frank-cli help <topic>` -- Text

```
WORKFLOWS

The frank-cli extraction pipeline transforms F# source code into semantic
definitions (OWL ontology + SHACL shapes) through a series of commands:

  Step 1: extract (required)
    Analyzes F# source code and produces initial semantic definitions.
    Prerequisites: (none)
    Next: clarify, validate

  Step 2: clarify (optional)
    Identifies ambiguities in the extraction and presents questions.
    Prerequisites: extract
    Next: validate

  Step 3: validate (required)
    Checks completeness and consistency of the extracted definitions.
    Prerequisites: extract
    Next: compile

  Step 4: diff (optional)
    Compares current extraction state with a previous snapshot.
    Prerequisites: extract
    Next: (informational only)

  Step 5: compile (required)
    Generates final OWL/XML and SHACL artifact files.
    Prerequisites: extract (validate recommended)
    Next: (end of pipeline)

Typical usage:
  frank-cli extract --project MyApp.fsproj --base-uri http://example.org/
  frank-cli clarify --project MyApp.fsproj
  frank-cli validate --project MyApp.fsproj
  frank-cli compile --project MyApp.fsproj
```

### `frank-cli help <topic>` -- JSON

```json
{
  "name": "workflows",
  "summary": "End-to-end guide to the extraction pipeline",
  "content": "The frank-cli extraction pipeline transforms..."
}
```

### `frank-cli help <unknown>` -- Text (no match)

```
Unknown command or topic: 'comiple'

Did you mean?
  compile     Generate OWL/XML and SHACL artifacts from extraction state

Use 'frank-cli help' to see all commands and topics.
```

### `frank-cli help <unknown>` -- JSON (no match)

```json
{
  "status": "not_found",
  "query": "comiple",
  "suggestions": [
    {
      "name": "compile",
      "summary": "Generate OWL/XML and SHACL artifacts from extraction state",
      "type": "command"
    }
  ]
}
```

## Contract 3: Status Command Output

### `frank-cli status --project MyApp.fsproj` -- Text

#### State: Not Extracted
```
Project: MyApp/MyApp.fsproj
State directory: MyApp/obj/frank-cli/

Extraction: not performed
Artifacts: not present
Recommended action: run 'frank-cli extract --project MyApp/MyApp.fsproj --base-uri <URI>'
```

#### State: Current, No Artifacts
```
Project: MyApp/MyApp.fsproj
State directory: MyApp/obj/frank-cli/

Extraction: current (extracted 2026-03-15T10:30:00Z)
Artifacts: not present
Recommended action: run 'frank-cli compile --project MyApp/MyApp.fsproj'
```

#### State: Stale
```
Project: MyApp/MyApp.fsproj
State directory: MyApp/obj/frank-cli/

Extraction: stale (source files changed since extraction)
Artifacts: present (may be outdated)
Recommended action: run 'frank-cli extract --project MyApp/MyApp.fsproj --base-uri <URI>'
```

#### State: Up to Date
```
Project: MyApp/MyApp.fsproj
State directory: MyApp/obj/frank-cli/

Extraction: current (extracted 2026-03-15T10:30:00Z)
Artifacts: present
Recommended action: up to date (no action needed)
```

#### State: Unreadable
```
Project: MyApp/MyApp.fsproj
State directory: MyApp/obj/frank-cli/

Extraction: unreadable (Unexpected end of JSON)
Artifacts: not present
Recommended action: run 'frank-cli extract --project MyApp/MyApp.fsproj --base-uri <URI>' to recover
```

#### Error: No Project
```
Error: Project file not found: /path/to/Missing.fsproj
```

### `frank-cli status --project MyApp.fsproj --format json` -- JSON

```json
{
  "status": "ok",
  "projectPath": "MyApp/MyApp.fsproj",
  "stateDirectory": "MyApp/obj/frank-cli/",
  "extraction": {
    "state": "current",
    "timestamp": "2026-03-15T10:30:00Z"
  },
  "artifacts": {
    "state": "present",
    "files": [
      "MyApp/obj/frank-cli/ontology.owl.xml",
      "MyApp/obj/frank-cli/shapes.shacl.ttl",
      "MyApp/obj/frank-cli/manifest.json"
    ]
  },
  "recommendedAction": {
    "action": "up_to_date",
    "message": "No action needed"
  }
}
```

#### JSON extraction.state values:
- `"not_extracted"` -- no state file
- `"current"` -- state file exists, not stale
- `"stale"` -- state file exists, source files changed
- `"unreadable"` -- state file exists but cannot be parsed

#### JSON artifacts.state values:
- `"present"` -- all three artifacts exist
- `"missing"` -- some or all artifacts missing

#### JSON recommendedAction.action values:
- `"run_extract"` -- initial extraction needed
- `"re_extract"` -- stale, re-extraction needed
- `"run_compile"` -- extraction current, compile needed
- `"up_to_date"` -- nothing to do
- `"recover_extract"` -- state unreadable, re-extract to recover

### Error JSON

```json
{
  "status": "error",
  "message": "Project file not found: /path/to/Missing.fsproj"
}
```
