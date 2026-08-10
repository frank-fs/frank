# Frank.Rdf

[![NuGet Version](https://img.shields.io/nuget/v/Frank.Rdf)](https://www.nuget.org/packages/Frank.Rdf/)

An `rdf { }` computation expression for hand-authoring RDF triples across one or more resources, serialized to JSON-LD in expanded form.

Zero ASP.NET Core dependency — no `ProjectReference` to [`Frank`](https://www.nuget.org/packages/Frank/), no `FrameworkReference` to `Microsoft.AspNetCore.App`; the only NuGet dependency is `dotNetRdf.Core`. It builds and serializes documents; it has no opinion on how a handler returns the result.

## Installation

```bash
dotnet add package Frank.Rdf
```

## Example

```fsharp
open System
open Frank.Rdf

let players = Node.Iri "https://example.org/games/1#players"
let anonymousReview = Node.blank ()   // no natural IRI -- minted fresh, GUID-backed

let gameDoc =
    rdf {
        prefix "schema" "https://schema.org/"

        about (
            describe (Node.Iri "https://example.org/games/1") {
                typ "schema:Game"
                propertyString "schema:name" "Tic-tac-toe"
                propertyInt "schema:numberOfPlayers" 2
                propertyBool "schema:isFree" true
                propertyDateTime "schema:datePublished" (DateTimeOffset(1952, 1, 1, 0, 0, 0, TimeSpan.Zero))
                propertyNode "schema:sameAs" (Node.Iri "http://www.wikidata.org/entity/Q210339")
                propertyNode "schema:review" anonymousReview
            }
        )

        about (describe anonymousReview { propertyString "schema:reviewBody" "A timeless classic." })
    }

Doc.toJsonLd gameDoc
```

`describe`/`about` mirrors `handler { }`/`get`: `describe subject { ... }` runs to completion on its own, producing a plain `Description`, and `about` absorbs it into the surrounding `rdf { }` document — the same two-CE composition pattern Frank core already uses for `handler { }` feeding `resource { }`'s `get`. A bare `triple subject predicate value` operation is also available for one-off statements.

`Node.blank ()` mints an anonymous node for values with no natural IRI (like the review above) — each call is GUID-backed, so blank nodes minted by two independently-built `Doc`s never collide when merged via `Doc.merge`/`includeDoc`.

## Available Operations

- `prefix "name" "uri"` - Declares a CURIE namespace mapping
- `about (describe subject { ... })` - Absorbs a `Description` built by a nested `describe { }` block
- `triple subject predicate value` - Asserts a single statement directly
- `includeDoc otherDoc` - Merges another independently-built `Doc` in (same as `Doc.merge`)
- `typ "prefix:Type"` - Asserts `rdf:type`
- `propertyString` / `propertyInt` / `propertyBool` / `propertyDateTime` / `propertyNode` - Asserts a property, picked by the value's type (five distinct operations rather than one overloaded `property`, since F#'s custom-operation overload resolution can't reliably disambiguate by argument type across calls in the same block)
- `Node.blank ()` - Mints a fresh, GUID-backed blank node for a subject/object with no natural IRI

## Serializing

- `Doc.toGraph doc` - Builds a `VDS.RDF.Graph`
- `Doc.writeJsonLd doc writer` - Streams expanded-form JSON-LD into a `System.IO.TextWriter` (synchronous, flexible for any stream)
- `Doc.writeJsonLdAsync doc bufferWriter` - **Streams expanded-form JSON-LD asynchronously into an `IBufferWriter<byte>` (e.g. `HttpResponse.BodyWriter`)** — best for response streaming, encodes UTF8 directly to the buffer with no intermediate string allocation
- `Doc.toJsonLd doc` - Convenience wrapper returning the JSON-LD as a `string`

Output is always **expanded-form** JSON-LD: no `@context`, every predicate and type expanded to its absolute IRI. There is no compact-form option.

### Choosing a serialization method

- Use `writeJsonLdAsync` when streaming to HTTP responses (most efficient, async-friendly)
- Use `writeJsonLd` when you have a `TextWriter` from a third-party API or need flexibility
- Use `toJsonLd` for testing, debugging, or when the full document fits in memory

## Related Packages

Has no dependency on [`Frank`](https://www.nuget.org/packages/Frank/), but is designed to serve JSON-LD documents from Frank resources — see [`sample/Frank.Rdf.Sample`](https://github.com/frank-fs/frank/tree/master/sample/Frank.Rdf.Sample) for a runnable demonstration, including `Doc.merge` folding shared facts (a publisher record) into each per-resource document.

See the [project repository](https://github.com/frank-fs/frank) for the complete guide and sample applications.

## License

[MIT](https://github.com/frank-fs/frank/blob/master/LICENSE)
