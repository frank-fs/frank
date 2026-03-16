# Parser API Contract

**Module**: `Frank.Statecharts.Scxml.Parser` (internal)

## Public API Surface

```fsharp
module internal Frank.Statecharts.Scxml.Parser

open Frank.Statecharts.Scxml.Types

/// Parse SCXML from a string.
/// Uses XDocument.Parse with LoadOptions.SetLineInfo.
/// Catches XmlException for malformed XML, producing structured ParseError.
val parseString : xml:string -> ScxmlParseResult

/// Parse SCXML from a TextReader.
/// Uses XDocument.Load with LoadOptions.SetLineInfo.
/// Caller owns the TextReader lifetime.
val parseReader : reader:System.IO.TextReader -> ScxmlParseResult

/// Parse SCXML from a Stream.
/// Uses XDocument.Load with LoadOptions.SetLineInfo.
/// Caller owns the Stream lifetime.
val parseStream : stream:System.IO.Stream -> ScxmlParseResult
```

## Behavioral Contract

1. **Malformed XML** (not well-formed XML): Returns `ScxmlParseResult` with `Document = None` and a single `ParseError` containing the `XmlException` message, line number, and line position.

2. **Valid XML, valid SCXML**: Returns `ScxmlParseResult` with `Document = Some doc`, `Errors = []`, and any non-fatal warnings in `Warnings`.

3. **Valid XML, invalid SCXML structure**: Returns `ScxmlParseResult` with best-effort `Document = Some doc` and structural errors in `Errors` (e.g., missing required attributes). Parsing continues past structural errors to provide maximum information.

4. **Namespace resolution**: Accepts both default namespace (`xmlns="http://www.w3.org/2005/07/scxml"`) and prefixed namespace. Elements without the SCXML namespace are ignored with a warning.

5. **Initial state inference**: When `<scxml>` has no `initial` attribute, `InitialId` is set to the `id` of the first child state in document order (per W3C section 3.2).

6. **Space-separated targets**: `<transition target="s1 s2 s3">` produces `Targets = ["s1"; "s2"; "s3"]`.

7. **Data entry forms**: Both `<data id="x" expr="0"/>` and `<data id="x">content</data>` are supported. The `expr` attribute takes precedence over child text content.

8. **History defaults**: `<history id="h1"/>` with no `type` attribute defaults to `ScxmlHistoryKind.Shallow`.

9. **Out-of-scope elements**: `<onentry>`, `<onexit>`, `<script>`, and other executable content elements are silently skipped (not parse errors). A warning is emitted for unknown elements.
