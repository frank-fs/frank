# Data Model: ALPS Native Annotations and Full Fidelity

**Feature**: 029-alps-native-annotations
**Date**: 2026-03-18

## AlpsMeta DU (unchanged)

```
AlpsMeta (7 cases — no expansion needed)
├── AlpsTransitionType of AlpsTransitionKind     # safe/unsafe/idempotent
├── AlpsDescriptorHref of string                 # href-only reference for shared transitions
├── AlpsExtension of id * href option * value option  # ext elements
├── AlpsDocumentation of format option * value   # doc elements
├── AlpsLink of rel * href                       # link elements
├── AlpsDataDescriptor of id * doc option        # non-state semantic descriptors
└── AlpsVersion of string                        # alps version attribute
```

## Shared Intermediate Types (extracted to Classification.fs)

```
ParsedDescriptor (internal, shared between JSON and XML parsers)
├── Id: string option
├── Type: string option           # raw "semantic", "safe", "unsafe", "idempotent"
├── Href: string option           # href-only reference
├── ReturnType: string option     # rt target
├── DocFormat: string option      # doc format attribute
├── DocValue: string option       # doc value text
├── Children: ParsedDescriptor list
├── Extensions: ParsedExtension list
└── Links: ParsedLink list

ParsedExtension = { Id: string; Href: string option; Value: string option }
ParsedLink = { Rel: string; Href: string }
```

## Classification Flow

```
JSON text ──→ JsonParser Pass 1 ──→ ParsedDescriptor list ──┐
                                                             ├──→ Classification Pass 2 ──→ StatechartDocument
XML text  ──→ XmlParser Pass 1  ──→ ParsedDescriptor list ──┘
```

Both parsers produce the same intermediate type, then shared classification produces identical ASTs.

## XML Element → ParsedDescriptor Mapping

| XML Element | ParsedDescriptor Field |
|-------------|----------------------|
| `<descriptor id="x">` | `Id = Some "x"` |
| `type="safe"` | `Type = Some "safe"` |
| `href="#ref"` | `Href = Some "#ref"` |
| `rt="#target"` | `ReturnType = Some "#target"` |
| `<doc format="text">content</doc>` | `DocFormat = Some "text"`, `DocValue = Some "content"` |
| `<ext id="..." href="..." value="..."/>` | `Extensions` list entry |
| `<link rel="..." href="..."/>` | `Links` list entry |
| Nested `<descriptor>` | `Children` list |
