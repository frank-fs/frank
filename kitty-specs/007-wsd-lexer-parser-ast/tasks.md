# Tasks: WSD Lexer, Parser, and AST

**Feature**: 007-wsd-lexer-parser-ast
**Generated**: 2026-03-07
**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

## Subtask Index

| ID | Description | WP |
|----|-------------|-----|
| T001 | SourcePosition struct type | WP01 |
| T002 | TokenKind DU and Token struct | WP01 |
| T003 | ArrowStyle + Direction DUs | WP01 |
| T004 | Participant record type | WP01 |
| T005 | Message record type | WP01 |
| T006 | GuardAnnotation record type | WP01 |
| T007 | Note + NotePosition types | WP01 |
| T008 | GroupKind, GroupBranch, Group types | WP01 |
| T009 | DiagramElement DU | WP01 |
| T010 | Diagram record type | WP01 |
| T011 | ParseFailure, ParseWarning, ParseResult types | WP01 |
| T012 | Lexer - line ending normalization + comment/blank line stripping | WP02 |
| T013 | Lexer - keyword tokenization (participant, title, autonumber, note, over, left of, right of, alt, opt, loop, par, break, critical, ref, else, end, as) | WP02 |
| T014 | Lexer - arrow tokenization (all four forms with longest-match) | WP02 |
| T015 | Lexer - punctuation, identifiers, string literals, text content tokens | WP02 |
| T016 | Lexer - source position tracking (line/column per token) | WP02 |
| T017 | Lexer tests (all token types, edge cases, position accuracy) | WP02 |
| T018 | Core parser infrastructure (token stream cursor, peek/advance/expect helpers, error collection) | WP03 |
| T019 | Parser - participant declarations (explicit `participant` lines + alias with `as`) | WP03 |
| T020 | Parser - message parsing (all arrow styles, labels, parameter lists, implicit participant declaration on first message reference) | WP03 |
| T021 | Parser - directive parsing (title, autonumber) | WP03 |
| T022 | Parser - note parsing (over, left of, right of, content extraction) | WP03 |
| T023 | Core parser tests (participants, messages, directives, notes) | WP03 |
| T024 | Guard parser - bracket detection + key-value pair extraction | WP04 |
| T025 | Guard parser - mixed content handling (guard annotation + remaining text) | WP04 |
| T026 | Guard parser - error cases (unclosed bracket, missing =, empty key, empty guard) | WP04 |
| T027 | Guard parser tests | WP04 |
| T028 | Grouping block parser - all seven block kinds (alt, opt, loop, par, break, critical, ref) | WP05 |
| T029 | Grouping block parser - else branch handling (multiple branches per block) | WP05 |
| T030 | Grouping block parser - arbitrary nesting support (recursive descent into block bodies) | WP05 |
| T031 | Grouping block tests (nesting, branches, all block kinds) | WP05 |
| T032 | Error recovery - skip-to-newline for unrecognized line-level syntax | WP06 |
| T033 | Error recovery - skip-to-end for errors inside grouping blocks | WP06 |
| T034 | Error recovery - unclosed block recovery (implicit close at EOF) | WP06 |
| T035 | Error recovery - implicit participant warnings (first-appearance declaration) | WP06 |
| T036 | Error limit configuration (configurable max errors, default 50) | WP06 |
| T037 | Corrective example generation for each error type (Amundsen conventions) | WP06 |
| T038 | Error/warning tests (structured failure reports, multiple errors, corrective examples, error limit cutoff at configured max) | WP06 |
| T039 | Convenience parseWsd function (parseWsd internally calls parse with maxErrors=50, the FR-008 default) + .fsproj updates + multi-target build verification | WP07 |
| T040 | Round-trip tests - Amundsen onboarding WSD example (SC-001) | WP07 |
| T041 | Round-trip tests - tic-tac-toe WSD with guard extensions (SC-002) | WP07 |
| T042 | Edge case tests (Unicode, empty input, deep nesting 5+, duplicate participants, tabs vs spaces, Windows line endings, empty parentheses makeMove() vs no parentheses makeMove distinction) | WP07 |
| T043 | .fsproj Compile item ordering (Types.fs, Lexer.fs, GuardParser.fs, Parser.fs) | WP07 |
| T044 | Multi-target build verification (net8.0/net9.0/net10.0) | WP07 |

## Work Package Summary

| WP | Title | Subtasks | Dependencies | Requirement Refs |
|----|-------|----------|--------------|------------------|
| WP01 | AST types + token definitions | T001-T011 | None | FR-001, FR-002, FR-003, FR-004, FR-007, FR-008a |
| WP02 | Lexer (tokenizer) | T012-T017 | WP01 | FR-001, FR-003, FR-010, FR-011 |
| WP03 | Core parser | T018-T023 | WP02 | FR-002, FR-003, FR-005, FR-009, FR-012, FR-013 |
| WP04 | Guard extension parser | T024-T027 | WP01 | FR-004 |
| WP05 | Grouping block parser | T028-T031 | WP03 | FR-006 |
| WP06 | Error recovery + failure reports | T032-T038 | WP03, WP04 | FR-004, FR-007, FR-008, FR-008a, FR-009 |
| WP07 | Integration, round-trip tests + build verification | T039-T044 | WP05, WP06 | FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-009, FR-010, FR-011, FR-012, FR-013 |

## Dependency Graph

```
WP01 (AST types)
 ├── WP02 (Lexer) ──── WP03 (Core parser) ──── WP05 (Grouping blocks) ────┐
 │                                         │                                │
 └── WP04 (Guard parser) ─────────────────┴── WP06 (Error recovery) ──────┴── WP07 (Integration)
```

---

## Work Package WP01: AST Types + Token Definitions (Priority: P0)

**Goal**: Define all AST discriminated unions, record types, and token definitions for the WSD parser.
**Prompt**: `tasks/WP01-ast-types-and-token-definitions.md`
**Estimated Size**: ~400 lines
**Requirement Refs**: FR-001, FR-002, FR-003, FR-004, FR-007, FR-008a

### Included Subtasks
- [x] T001 SourcePosition struct type
- [x] T002 TokenKind DU and Token struct
- [x] T003 ArrowStyle + Direction DUs
- [x] T004 Participant record type
- [x] T005 Message record type
- [x] T006 GuardAnnotation record type
- [x] T007 Note + NotePosition types
- [x] T008 GroupKind, GroupBranch, Group types
- [x] T009 DiagramElement DU
- [x] T010 Diagram record type
- [x] T011 ParseFailure, ParseWarning, ParseResult types

### Dependencies
- None (foundation WP)

---

## Work Package WP02: Lexer (Tokenizer) (Priority: P0)

**Goal**: Implement the lexer that tokenizes WSD input into a flat token stream.
**Prompt**: `tasks/WP02-lexer.md`
**Estimated Size**: ~450 lines
**Requirement Refs**: FR-001, FR-003, FR-010, FR-011

### Included Subtasks
- [x] T012 Lexer - line ending normalization + comment/blank line stripping
- [x] T013 Lexer - keyword tokenization
- [x] T014 Lexer - arrow tokenization (all four forms with longest-match)
- [x] T015 Lexer - punctuation, identifiers, string literals, text content tokens
- [x] T016 Lexer - source position tracking (line/column per token)
- [x] T017 Lexer tests (all token types, edge cases, position accuracy)

### Dependencies
- Depends on WP01

---

## Work Package WP03: Core Parser (Priority: P0)

**Goal**: Implement the recursive descent parser for participants, messages, directives, and notes.
**Prompt**: `tasks/WP03-core-parser.md`
**Estimated Size**: ~500 lines
**Requirement Refs**: FR-002, FR-003, FR-005, FR-009, FR-012, FR-013

### Included Subtasks
- [x] T018 Core parser infrastructure (token stream cursor, peek/advance/expect helpers, error collection)
- [x] T019 Parser - participant declarations
- [x] T020 Parser - message parsing (all arrow styles, labels, parameter lists)
- [x] T021 Parser - directive parsing (title, autonumber)
- [x] T022 Parser - note parsing (over, left of, right of, content extraction)
- [x] T023 Core parser tests (participants, messages, directives, notes)

### Dependencies
- Depends on WP02

---

## Work Package WP04: Guard Extension Parser (Priority: P1)

**Goal**: Parse guard annotations from note content (`[guard: key=value, ...]`).
**Prompt**: `tasks/WP04-guard-extension-parser.md`
**Estimated Size**: ~300 lines
**Requirement Refs**: FR-004

### Included Subtasks
- [x] T024 Guard parser - bracket detection + key-value pair extraction
- [x] T025 Guard parser - mixed content handling
- [x] T026 Guard parser - error cases (unclosed bracket, missing =, empty key, empty guard)
- [x] T027 Guard parser tests

### Dependencies
- Depends on WP01

---

## Work Package WP05: Grouping Block Parser (Priority: P1)

**Goal**: Parse grouping blocks (alt, opt, loop, par, break, critical, ref) with nesting support.
**Prompt**: `tasks/WP05-grouping-block-parser.md`
**Estimated Size**: ~350 lines
**Requirement Refs**: FR-006

### Included Subtasks
- [x] T028 Grouping block parser - all seven block kinds
- [x] T029 Grouping block parser - else branch handling
- [x] T030 Grouping block parser - arbitrary nesting support
- [x] T031 Grouping block tests (nesting, branches, all block kinds)

### Dependencies
- Depends on WP03

---

## Work Package WP06: Error Recovery + Failure Reports (Priority: P1)

**Goal**: Implement error recovery, multi-error collection, corrective examples, and partial AST output.
**Prompt**: `tasks/WP06-error-recovery-and-failure-reports.md`
**Estimated Size**: ~500 lines
**Requirement Refs**: FR-004, FR-007, FR-008, FR-008a, FR-009

### Included Subtasks
- [x] T032 Error recovery - skip-to-newline for unrecognized line-level syntax
- [x] T033 Error recovery - skip-to-end for errors inside grouping blocks
- [x] T034 Error recovery - unclosed block recovery (implicit close at EOF)
- [x] T035 Error recovery - implicit participant warnings
- [x] T036 Error limit configuration (configurable max errors, default 50)
- [x] T037 Corrective example generation for each error type
- [x] T038 Error/warning tests

### Dependencies
- Depends on WP03, WP04

---

## Work Package WP07: Integration, Round-Trip Tests + Build Verification (Priority: P1)

**Goal**: End-to-end validation, round-trip tests, edge case coverage, and multi-target build.
**Prompt**: `tasks/WP07-integration-and-build-verification.md`
**Estimated Size**: ~400 lines
**Requirement Refs**: FR-002, FR-003, FR-004, FR-005, FR-006, FR-007, FR-009, FR-010, FR-011, FR-012, FR-013

### Included Subtasks
- [x] T039 Convenience parseWsd function + .fsproj updates + multi-target build verification
- [x] T040 Round-trip tests - Amundsen onboarding WSD example (SC-001)
- [x] T041 Round-trip tests - tic-tac-toe WSD with guard extensions (SC-002)
- [x] T042 Edge case tests (Unicode, empty input, deep nesting, etc.)
- [x] T043 .fsproj Compile item ordering
- [x] T044 Multi-target build verification (net8.0/net9.0/net10.0)

### Dependencies
- Depends on WP05, WP06
