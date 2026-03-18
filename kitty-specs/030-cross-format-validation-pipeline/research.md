# Research: Cross-Format Validation Pipeline and AST Merge

**Feature**: 030-cross-format-validation-pipeline
**Date**: 2026-03-18

## R-001: Merge Algorithm

**Decision**: Left fold over format-priority-sorted documents.

**Algorithm**:
1. Parse all sources into `(FormatTag * StatechartDocument)` pairs
2. Sort by format priority: SCXML (0) > smcat (1) > WSD (2) > ALPS/AlpsXml (3)
3. Base document = highest priority format's document
4. For each subsequent document, fold:
   - Match states by `Identifier` (exact string match)
   - Matched states: accumulate annotations, take non-None fields from enriching doc only if base has None
   - Unmatched states from enriching doc: add to merged document
   - Match transitions by `(Source, Target, Event)` triple
   - Matched transitions: accumulate annotations
   - Unmatched transitions: add to merged document
   - Document-level annotations: accumulate from all sources

**Rationale**: Left fold preserves priority — the base document's structural fields are never overridden. Annotations always accumulate since the DU discriminates by format.

## R-002: Jaro-Winkler String Distance

**Decision**: Implement inline, no external dependency. ~30 lines of F#.

**Algorithm** (standard Jaro-Winkler):
1. Compute Jaro distance: matching characters / character count, considering transpositions within a window of `max(|s1|, |s2|) / 2 - 1`
2. Apply Winkler bonus: `jaro + (prefixLength * 0.1 * (1 - jaro))` where prefixLength is the common prefix up to 4 characters

**Threshold**: 0.8 default (configurable). Above threshold = near-match warning. Below = no warning.

**Rationale**: Jaro-Winkler is standard for short string similarity (names, identifiers). Better than Levenshtein for identifiers because it weights prefix matches — "startOnboarding" and "start" share a prefix, which Jaro-Winkler captures.

## R-003: FormatTag.AlpsXml

**Decision**: New DU case `AlpsXml` on `FormatTag`.

**Impact**:
- `Types.fs`: add case
- `Pipeline.fs`: add dispatch to `Alps.XmlParser.parseAlpsXml`
- `Merge.fs`: `AlpsXml` has same priority as `Alps` (3) — annotations only
- All existing pattern matches on `FormatTag` need updating (exhaustiveness)

**Alternatives considered**:
- Overload `Alps` to detect JSON vs XML at parse time — rejected: violates typed dispatch principle
- `AlpsJson` + `AlpsXml` (rename existing `Alps`) — rejected: breaks existing API unnecessarily

## R-004: Near-Match Validation Rule

**Decision**: Add a new cross-format rule to `CrossFormatRules.fs`.

**Rule design**: For each pair of format artifacts, compare their state identifier sets. For each state in format A not found in format B, check all states in format B for Jaro-Winkler similarity > threshold. Report as warning with: format pair, mismatched identifiers, similarity score, suggestion.

Same logic for event names across transitions.

**Integration**: The rule produces `ValidationCheck` entries with `Status = Fail` (for near-matches) and includes the details in `ValidationFailure.Description`. The caller reads these and decides whether to auto-correct.
