namespace Frank.Benchmarks

open BenchmarkDotNet.Attributes
open Frank.Rdf

/// #503: DescribeBuilder/RdfBuilder call-site audit found a mix of shapes in real code:
/// - sample/Frank.Rdf.Sample/Program.fs's `gameDoc` (rebuilt on every "application/ld+json"
///   GET /games/{id} request) and `Frank.Provenance`'s `MailboxProcessorProvenanceStore`
///   (rebuilt on every `Append`) are PER-REQUEST call sites.
/// - sample/Frank.Rdf.Sample/Program.fs's `publisherDoc` is a module-level, parameterless
///   `let` -- "Built once and merged into each per-game document" per its own comment --
///   i.e. STARTUP-ONLY.
/// `BuildGameDocument` reproduces `gameDoc`'s shape (one `rdf { }` block, two nested
/// `describe { }` blocks, 4 statements, one `Doc.merge`) for the per-request evidence bar;
/// `BuildPublisherDocument` reproduces `publisherDoc`'s shape (one `rdf { }` block, one
/// nested `describe { }`, 2 statements) for the startup-only shape. Both call directly into
/// Frank.Rdf's public API (the same surface NegotiationBenchmarks.fs/ValidationBenchmarks.fs
/// alongside this file use), not through a TestServer round-trip -- there is no HTTP framing
/// to compare, only the CE construction itself.
[<MemoryDiagnoser>]
type RdfBuilderBenchmarks() =

    let publisher = Node.Iri "https://frank-fs.github.io/#organization"

    // Mirrors sample/Frank.Rdf.Sample/Program.fs's `publisherDoc` exactly.
    [<Benchmark>]
    member _.BuildPublisherDocument() =
        rdf {
            prefix "schema" "https://schema.org/"
            about (describe publisher { typ "schema:Organization"; propertyString "schema:name" "Frank" })
        }

    // Mirrors sample/Frank.Rdf.Sample/Program.fs's `gameDoc "https://example.org" "1" "Tic-tac-toe"`,
    // including its `Doc.merge` with a (locally rebuilt, not shared) publisher document.
    [<Benchmark>]
    member this.BuildGameDocument() =
        let gameUri = Node.Iri "https://example.org/games/1"
        let players = Node.Iri "https://example.org/games/1#players"

        let doc =
            rdf {
                prefix "schema" "https://schema.org/"

                about (
                    describe gameUri {
                        typ "schema:Game"
                        propertyString "schema:name" "Tic-tac-toe"
                        propertyNode "schema:numberOfPlayers" players
                        propertyNode "schema:publisher" publisher
                    }
                )

                about (describe players { typ "schema:QuantitativeValue"; propertyInt "schema:value" 2 })
            }

        Doc.merge doc (this.BuildPublisherDocument())
