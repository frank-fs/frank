# Data Model: SCXML Native Annotations and Generator Fidelity

**Feature**: 028-scxml-native-annotations
**Date**: 2026-03-18

## New ScxmlMeta Cases (added to Ast/Types.fs)

```
ScxmlMeta (before)                              ScxmlMeta (after)
├── ScxmlInvoke                                 ├── ScxmlInvoke
├── ScxmlHistory                                ├── ScxmlHistory
├── ScxmlNamespace                              ├── ScxmlNamespace
├── ScxmlTransitionType                         ├── ScxmlTransitionType
├── ScxmlMultiTarget                            ├── ScxmlMultiTarget
├── ScxmlDatamodelType                          ├── ScxmlDatamodelType
├── ScxmlBinding                                ├── ScxmlBinding
└── ScxmlInitial                                ├── ScxmlInitial
                                                ├── ScxmlOnEntry of xml: string      ← new
                                                ├── ScxmlOnExit of xml: string       ← new
                                                ├── ScxmlInitialElement of targetId: string  ← new
                                                └── ScxmlDataSrc of name: string * src: string  ← new
```

## Dual-Layer Activities Model

### Portable Layer: StateActivities

```
StateNode.Activities : StateActivities option

StateActivities =
  { Entry: string list    ← action descriptions from <onentry> blocks
    Exit: string list     ← action descriptions from <onexit> blocks
    Do: string list }     ← unused for SCXML (no <do> equivalent)
```

Action description format: `"{elementName} {key-attribute}"` e.g., `"send done"`, `"log hello"`

### Format-Specific Layer: Annotations

Each `<onentry>` block → one `ScxmlAnnotation(ScxmlOnEntry(xml))` annotation
Each `<onexit>` block → one `ScxmlAnnotation(ScxmlOnExit(xml))` annotation

Raw XML captures the full content including nested elements, attributes, whitespace.

## Parser Annotation Placement Rules

### On StateNode

| SCXML source | Activities | Annotations |
|-------------|------------|-------------|
| `<onentry><send event="x"/></onentry>` | `Entry = ["send x"]` | `ScxmlOnEntry("<send event=\"x\" ... />")` |
| Multiple `<onentry>` blocks | Aggregated Entry list | One `ScxmlOnEntry` per block |
| `<onexit><log expr="bye"/></onexit>` | `Exit = ["log bye"]` | `ScxmlOnExit("<log expr=\"bye\" ... />")` |
| `<initial><transition target="s1"/></initial>` | N/A | `ScxmlInitialElement("s1")` |
| No executable content | `Activities = None` | No OnEntry/OnExit annotations |

### On StatechartDocument

| SCXML source | Annotation |
|-------------|------------|
| `<data id="x" src="file.json"/>` | `ScxmlDataSrc("x", "file.json")` |
| `<scxml xmlns="http://www.w3.org/2005/07/scxml">` | `ScxmlNamespace("http://www.w3.org/2005/07/scxml")` |
| `<scxml>` (no namespace) | `ScxmlNamespace("")` |

## Generator Reconstruction Rules

### Executable Content

```
Has ScxmlOnEntry annotations?
├── Yes → For each annotation: XElement.Parse(xml), add to state element
└── No, but has StateActivities.Entry?
    ├── Yes → Best-effort: emit <onentry><log expr="action"/></onentry>
    └── No → Don't emit <onentry>
```

### Initial Element vs Attribute

```
Has ScxmlInitialElement annotation?
├── Yes → Emit <initial><transition target="targetId"/></initial> (no initial attribute)
└── No, has ScxmlInitial annotation?
    ├── Yes → Emit initial="targetId" attribute on <state>
    └── No → No initial specification
```

### Namespace

```
Has ScxmlNamespace annotation?
├── ScxmlNamespace("") → Use XNamespace.None (no namespace)
├── ScxmlNamespace(ns) → Use XNamespace.Get(ns)
└── No annotation → Default to W3C namespace (backward compatible)
```
