# Research: ALPS Native Annotations and Full Fidelity

**Feature**: 029-alps-native-annotations
**Date**: 2026-03-18

## R-001: Shared Classification Module

**Decision**: Extract Pass 2 heuristics and intermediate types to `Alps/Classification.fs`.

**Types to extract**: `ParsedDescriptor`, `ParsedExtension`, `ParsedLink` (currently `private` in JsonParser.fs). Make them `internal` in the new module.

**Functions to extract**: `isTransitionTypeStr`, `collectRtTargets`, `isStateDescriptor`, `buildDescriptorIndex`, `resolveRt`, `extractGuard`, `extractParameters`, `toTransitionKind`, `buildStateAnnotations`, `buildTransitionAnnotations`, `resolveDescriptor`, `extractTransitions`, `toStateNode`.

**Rationale**: Constitution Principle VIII prohibits duplicated logic across modules. JSON and XML parsers both need these functions.

## R-002: ALPS XML Structure

**Decision**: ALPS XML mirrors JSON structure closely.

ALPS XML example:
```xml
<alps version="1.0">
  <doc>Generated from onboarding.wsd</doc>
  <descriptor id="identifier" type="semantic"/>
  <descriptor id="home" type="semantic">
    <descriptor href="#startOnboarding"/>
  </descriptor>
  <descriptor id="startOnboarding" type="unsafe" rt="#WIP">
    <descriptor href="#identifier"/>
  </descriptor>
  <link rel="self" href="http://example.com/alps/onboarding"/>
  <ext id="custom" href="http://example.com/ext" value="data"/>
</alps>
```

XML → `ParsedDescriptor` mapping:
- `<descriptor id="..." type="..." href="..." rt="...">` → `ParsedDescriptor` fields
- `<doc format="...">text</doc>` → `DocFormat`, `DocValue`
- `<ext id="..." href="..." value="..."/>` → `ParsedExtension`
- `<link rel="..." href="..."/>` → `ParsedLink`
- Nested `<descriptor>` → `Children`

## R-003: JSON Fidelity Gaps

**Current gaps identified**:
1. **Title duplication**: `rootDocValue` is used as both `Title` and `AlpsDocumentation`. On round-trip, `Title` is emitted as top-level `doc` which is correct. No gap here — verified.
2. **Descriptor ordering**: Generator emits data descriptors first, then states, then shared transitions. If original had different ordering, it changes. This is acceptable — ALPS doesn't define descriptor ordering semantics.
3. **Property ordering in JSON objects**: JSON spec says objects are unordered. Not a fidelity issue.

**Conclusion**: JSON round-trip is already close to lossless. The main test is to verify with Amundsen's onboarding example and other edge cases.

## R-004: F# Project File Updates

New files must be added to `Frank.Statecharts.fsproj` in the correct compilation order:
- `Alps/Classification.fs` — BEFORE `Alps/JsonParser.fs` and `Alps/XmlParser.fs`
- `Alps/XmlParser.fs` — AFTER `Alps/Classification.fs`
- `Alps/XmlGenerator.fs` — AFTER `Alps/Classification.fs`

Similarly for test project `.fsproj`.
