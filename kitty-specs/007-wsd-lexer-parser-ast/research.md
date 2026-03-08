# Research: WSD Lexer, Parser, and AST

**Branch**: `007-wsd-lexer-parser-ast` | **Date**: 2026-03-07 | **Spec**: [spec.md](spec.md)

## Parser Implementation Strategy

### Options Considered

**Option A: FParsec (parser combinator library)**

FParsec is the standard F# parser combinator library. It provides excellent error messages out of the box, composable parsers, and a well-tested foundation.

- Pro: Rich error reporting, composable, well-documented
- Pro: Handles whitespace and backtracking naturally
- Con: Additional NuGet dependency (violates constitution principle III -- minimize dependencies)
- Con: Allocation-heavy (combinator chains allocate closures per parse step); harder to meet SC-007 (1000-line inputs without allocation pressure)
- Con: Partial AST recovery requires fighting the combinator model (FParsec is designed for all-or-nothing parsing)

**Option B: Hand-written recursive descent parser**

A hand-written lexer and recursive descent parser, following the classic two-phase architecture.

- Pro: Zero dependencies
- Pro: Full control over error recovery (can skip tokens, insert synthetic nodes, continue parsing)
- Pro: Allocation profile under direct control (can use spans, avoid intermediate strings)
- Pro: WSD grammar is simple enough that recursive descent is straightforward
- Con: More code to write and maintain
- Con: No formal grammar verification (correctness depends on tests)

**Option C: F# active patterns as lightweight combinators**

Use F# active patterns to build a parser without an external library. Each active pattern matches a grammar production.

- Pro: No external dependency
- Pro: Idiomatic F# (active patterns are a language feature)
- Con: Active pattern nesting limits in F# make deep grammars awkward
- Con: Error recovery is still manual
- Con: Performance characteristics similar to FParsec (pattern matching chains)

### Decision: Option B -- Hand-Written Recursive Descent

Rationale:
1. **No new dependency**: The constitution (III) says minimize external dependencies. FParsec would be the first parser-specific NuGet in the Frank ecosystem.
2. **Error recovery**: The spec requires partial AST with warnings (FR-008, FR-008a). Recursive descent gives direct control over recovery strategies (skip-to-newline, skip-to-`end`, insert synthetic nodes).
3. **Performance**: Direct string/span operations avoid the closure allocation overhead of combinator chains. This matters for SC-007.
4. **Simplicity**: WSD has roughly 15 keywords, 4 arrow types, and 7 grouping block types. This is not a complex grammar. A hand-written parser for this grammar is ~300-500 lines of F#.

## WSD Syntax Coverage

### Full Syntax Inventory

The following WSD constructs are defined by the websequencediagrams.com renderer behavior. No formal grammar exists; this inventory is derived from the renderer's documentation and Amundsen's published examples.

#### Directives
| Construct | Syntax | Example |
|-----------|--------|---------|
| Title | `title <text>` | `title Onboarding Flow` |
| Auto-number | `autonumber` | `autonumber` |

#### Participants
| Construct | Syntax | Example |
|-----------|--------|---------|
| Explicit declaration | `participant <name>` | `participant Client` |
| Aliased declaration | `participant <name> as <alias>` | `participant API as "REST API"` |
| Implicit declaration | First appearance in a message | `Client->Server: request` (declares both) |

#### Messages (Arrows)
| Arrow | Syntax | Semantics (Amundsen) |
|-------|--------|---------------------|
| Solid forward | `->` | Synchronous call, activates target |
| Dashed forward | `-->` | Asynchronous or optional call |
| Solid deactivating | `->-` | Return from activation, safe/idempotent |
| Dashed deactivating | `-->-` | Asynchronous return |

Message syntax: `<sender><arrow><receiver>: <label>(<params>)`

Parameters are optional: `makeMove(position)`, `getStatus()`, or just `getStatus` (no parens).

#### Notes
| Construct | Syntax | Example |
|-----------|--------|---------|
| Note over | `note over <participant>: <text>` | `note over Client: Initiates flow` |
| Note left of | `note left of <participant>: <text>` | `note left of Server: Internal` |
| Note right of | `note right of <participant>: <text>` | `note right of Server: External` |

#### Grouping Blocks
| Block | Syntax | Semantics |
|-------|--------|-----------|
| Alt | `alt <condition>` ... `else <condition>` ... `end` | Conditional (if/else) |
| Opt | `opt <condition>` ... `end` | Optional (if, no else) |
| Loop | `loop <condition>` ... `end` | Repetition |
| Par | `par` ... `else` ... `end` | Parallel execution |
| Break | `break <condition>` ... `end` | Exception/break |
| Critical | `critical <condition>` ... `end` | Critical section |
| Ref | `ref <text>` ... `end` | Reference to another diagram |

All grouping blocks support `else` for additional branches and `end` for termination. Nesting is supported to arbitrary depth.

#### Comments and Whitespace
- Lines starting with `#` are comments (ignored)
- Blank lines and whitespace-only lines are ignored
- Tabs and spaces are both accepted for indentation (indentation is not significant)
- Windows (`\r\n`) and Unix (`\n`) line endings both accepted

### Constructs NOT Supported (Out of Scope)

- `activate` / `deactivate` explicit commands (activation is implicit via arrow semantics)
- `destroy` participant command
- Styling directives (`theme`, `skin`, colors)
- Multi-line notes (`note over X\n...\nend note`)
- Box grouping (`box "label"` ... `end`)

These are renderer-specific features that do not map to statechart semantics. If encountered, the parser emits a warning and skips the line.

## Guard Extension Syntax Design

### Syntax

```
note over <participant>: [guard: <key>=<value>, <key>=<value>, ...]
```

### Design Decisions

1. **Bracket delimiters**: `[guard: ...]` uses square brackets to visually distinguish guard metadata from regular note text. Square brackets are not used in standard WSD syntax, so there is no ambiguity.

2. **Key-value pairs**: Guards are expressed as `key=value` pairs separated by commas. This is intentionally simple -- no nested structures, no quoted values, no operators beyond `=`.

3. **Known guard keys**: The parser does not validate key names. Downstream consumers (the statechart pipeline) define which keys are meaningful. The parser's job is structural extraction only.

4. **Placement**: Guards are only recognized in `note over` annotations, not in `note left of` or `note right of`. This is because guards apply to the participant they are "over" -- directional notes are for documentation.

5. **Mixed content**: A note may contain both guard syntax and descriptive text: `note over Player: [guard: role=PlayerX] Must be current player`. The guard is extracted; remaining text is preserved as the note content.

### Examples

```
note over Player: [guard: role=PlayerX]
note over Board: [guard: state=XTurn, role=PlayerX]
note over API: [guard: auth=bearer, scope=write]
```

### Error Cases

| Input | Result |
|-------|--------|
| `note over X: [guard: malformed` | ParseFailure: unclosed bracket at position |
| `note over X: [guard: =value]` | ParseFailure: empty key at position |
| `note over X: [guard: key=]` | ParseWarning: empty value (valid but suspicious) |
| `note over X: [guard: ]` | ParseWarning: empty guard annotation |
| `note over X: [guard: key]` | ParseFailure: missing `=` in guard pair |

## Error Recovery Strategy

### Philosophy

The parser follows a **best-effort partial AST** model. Rather than aborting on the first error, it collects errors and warnings, recovers, and continues parsing. The result always includes a `Diagram` (possibly empty or partial) alongside any diagnostics.

### Recovery Techniques

#### 1. Skip-to-Newline Recovery

For unrecognized tokens on a line (e.g., unknown arrow syntax), the parser:
1. Records a `ParseFailure` with the current position, what was expected, and what was found
2. Skips all tokens until the next newline
3. Resumes parsing from the next line

This is the default recovery for any line-level parse error.

#### 2. Skip-to-End Recovery (Grouping Blocks)

For errors inside a grouping block, the parser:
1. Records the error
2. Skips tokens until a matching `end` keyword is found (tracking nesting depth)
3. Adds a `Group` node with whatever children were successfully parsed before the error

#### 3. Unclosed Block Recovery

For `alt`/`opt`/`loop`/etc. blocks that reach EOF without an `end`:
1. Records a `ParseFailure` referencing the opening line number
2. Closes all open blocks implicitly
3. Includes the partial group structure in the AST

#### 4. Implicit Participant Recovery

For messages referencing undeclared participants:
1. Records a `ParseWarning` (not an error -- implicit declaration is valid WSD)
2. Adds the participant to the diagram's participant list
3. Continues normally

### Error Limits

The parser accepts a configurable maximum error count (default: 50). After reaching the limit, it stops parsing and returns the partial AST with collected diagnostics. This prevents runaway error cascading on severely malformed input.

## Amundsen Arrow Semantics Mapping

Mike Amundsen's API design approach assigns semantic meaning to WSD arrow types. This mapping is central to how the parser's output feeds the statechart pipeline.

| Arrow | Style | Direction | HTTP Semantics | Statechart Semantics |
|-------|-------|-----------|---------------|---------------------|
| `->` | Solid | Forward | Unsafe operation (POST, PUT, DELETE) | State transition trigger |
| `-->` | Dashed | Forward | Safe/optional operation (GET, conditional) | Query or optional transition |
| `->-` | Solid | Deactivating | Return from unsafe operation | Transition completion |
| `-->-` | Dashed | Deactivating | Return from safe operation | Query response |

### Mapping Rationale

- **Solid = unsafe**: In Amundsen's workflow, solid arrows represent operations that change server-side state. These map to HTTP methods that are not safe (POST, PUT, DELETE).
- **Dashed = safe/optional**: Dashed arrows represent operations that do not change state or are conditional. These map to safe HTTP methods (GET, HEAD, OPTIONS).
- **Forward = request**: Forward arrows represent outgoing requests that activate the target participant.
- **Deactivating = response**: Deactivating arrows represent returns that deactivate the target (end of interaction sequence).

### Parser Responsibility

The parser assigns `ArrowStyle` (Solid/Dashed) and `Direction` (Forward/Deactivating) to each `Message` node. It does NOT perform the HTTP method mapping -- that is the responsibility of the downstream WSD-to-statechart pipeline. The parser's job is faithful syntactic representation.
