namespace Frank.Benchmarks

open System
open BenchmarkDotNet.Attributes
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

/// #497: ValidationOutcome is on a genuine per-request hot path (the SHACL interceptor middleware
/// -- src/Frank.Validation/WebHostBuilderExtensions.fs's `runValidation` -- calls `Shacl.validate`
/// once per validated POST/PUT/PATCH request and pattern-matches its result). `Shacl.validate` is
/// the exact call site that allocates a `ValidationOutcome` value; this benchmark calls it directly
/// rather than round-tripping through a TestServer, because `useValidationMiddleware` is `internal`
/// to Frank.Validation (InternalsVisibleTo only reaches Frank.Validation.Tests) and this project,
/// like NegotiationBenchmarks.fs alongside it, deliberately stays on Frank's PUBLIC API surface --
/// no InternalsVisibleTo grant was added to reach it. `Shacl.validate`'s own two outcomes (Conforms
/// / Violates) are exercised with a real SHACL shapes graph and real dotNetRDF data graphs (built
/// via Frank.Rdf's `rdf { }` DSL, the same public surface a consuming application would use), so
/// MemoryDiagnoser's allocation numbers reflect the real ValidationOutcome allocation, not a
/// synthetic stand-in.
[<MemoryDiagnoser>]
type ShaclValidateBenchmarks() =

    let shapesGraph =
        Shacl.toShapesGraph
            [ recordShape
                  (targetClass (Uri "https://schema.org/MoveAction"))
                  [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                    |> addConstraint (PropertyConstraint.MinCount 1) ] ]

    let conformingGraph =
        Doc.toGraph (
            rdf {
                about (
                    describe (Node.Iri "https://example.org/move1") {
                        typ "https://schema.org/MoveAction"
                        propertyInt "https://schema.org/position" 3
                    }
                )
            }
        )
        :> VDS.RDF.IGraph

    let violatingGraph =
        Doc.toGraph (
            rdf {
                about (
                    describe (Node.Iri "https://example.org/move2") {
                        typ "https://schema.org/MoveAction"
                    }
                )
            }
        )
        :> VDS.RDF.IGraph

    [<Benchmark(Baseline = true)>]
    member _.Conforms() = Shacl.validate shapesGraph conformingGraph

    [<Benchmark>]
    member _.Violates() = Shacl.validate shapesGraph violatingGraph
