# Generator API Contract

**Module**: `Frank.Statecharts.Scxml.Generator` (internal)

## Public API Surface

```fsharp
module internal Frank.Statecharts.Scxml.Generator

open Frank.Statecharts.Scxml.Types

/// Generate SCXML XML string from an ScxmlDocument.
/// Produces well-formed XML with the W3C SCXML namespace and version="1.0".
/// Output is indented for readability.
val generate : doc:ScxmlDocument -> string

/// Generate SCXML XML and write to a TextWriter.
/// Caller owns the TextWriter lifetime.
val generateTo : writer:System.IO.TextWriter -> doc:ScxmlDocument -> unit
```

## Behavioral Contract

1. **Root element**: Always produces `<scxml xmlns="http://www.w3.org/2005/07/scxml" version="1.0">` with the W3C namespace declaration.

2. **Optional attributes on `<scxml>`**: `initial`, `name`, `datamodel`, `binding` attributes are emitted only when the corresponding `ScxmlDocument` field is `Some`.

3. **State elements**: `ScxmlState` nodes are emitted as `<state>`, `<parallel>`, or `<final>` based on `ScxmlStateKind`.

4. **Compound states**: `Compound` states with `InitialId = Some id` emit `<state id="..." initial="...">` with nested child elements.

5. **Transitions**: `ScxmlTransition` nodes are emitted as `<transition>` with `event`, `cond`, `target`, and `type` attributes when present. Multiple targets are joined with spaces.

6. **Data model**: `DataEntry` lists produce `<datamodel><data id="..." expr="..."/></datamodel>`. Entries with no expression produce `<data id="..."/>`.

7. **History nodes**: `ScxmlHistory` nodes produce `<history id="..." type="...">` with an optional child `<transition>` for the default target.

8. **Invoke nodes**: `ScxmlInvoke` nodes produce `<invoke>` with `type`, `src`, and `id` attributes when present.

9. **Canonical output**: The generator produces normalized/canonical SCXML. It does not attempt to reproduce exact formatting of a previously parsed document. Attribute ordering follows a consistent convention: `id`, then `initial`/`type`/other, then `event`, `cond`, `target`.

10. **Well-formed XML**: Output is always valid XML that can be re-parsed by the same parser without errors (SC-002).
