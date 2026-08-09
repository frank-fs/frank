namespace Frank.Benchmarks

open System
open BenchmarkDotNet.Attributes
open Frank.Rdf
open Frank.Validation
open Frank.Validation.ShapeSpecFunctions

/// #499: `Violation` is the [<Struct>] measurement priority in issue #499 -- it's on the same
/// genuine per-request hot path as #497's `ValidationOutcome` (the SHACL interceptor middleware,
/// src/Frank.Validation/WebHostBuilderExtensions.fs's `runValidation`, calls `Shacl.validate`
/// once per validated POST/PUT/PATCH request), but #497's `ShaclValidateBenchmarks` exercises
/// only a single `Violation` per call (one target node, one failing constraint), which its own
/// benchmark table showed swallowed by ~600KB/call of surrounding SHACL-engine noise. This
/// benchmark widens the signal: a shape with TWO failing property constraints validated against
/// TWENTY non-conforming target nodes in one graph produces up to 40 `Violation` records per
/// `Shacl.validate` call, so the `Violation` representation's own share of the call's allocation
/// is a much larger fraction of the total -- still through the real, public `Shacl.validate` call
/// site (same InternalsVisibleTo constraint noted in ValidationBenchmarks.fs: `Frank.Benchmarks`
/// has no grant to reach the `internal` `useValidationMiddleware`, so this stays on Frank's public
/// API surface, same as NegotiationBenchmarks.fs and ShaclValidateBenchmarks alongside it).
[<MemoryDiagnoser>]
type ViolationBenchmarks() =

    let shapesGraph =
        Shacl.toShapesGraph
            [ recordShape
                  (targetClass (Uri "https://schema.org/MoveAction"))
                  [ ofPath (PropertyPath.Predicate(Uri "https://schema.org/position"))
                    |> addConstraint (PropertyConstraint.MinCount 1)
                    ofPath (PropertyPath.Predicate(Uri "https://schema.org/name"))
                    |> addConstraint (PropertyConstraint.MinCount 1) ] ]

    // rdf { } has no For/Combine (see Rdf.fsi's RdfBuilder doc comment), so N nodes are built as N
    // single-node Docs and combined with Doc.merge rather than a `for` loop inside the CE.
    let conformingNodeDoc (i: int) : Doc =
        rdf {
            about (
                describe (Node.Iri $"https://example.org/move{i}") {
                    typ "https://schema.org/MoveAction"
                    propertyInt "https://schema.org/position" i
                    propertyString "https://schema.org/name" $"move{i}"
                }
            )
        }

    // Neither required property present -- 2 violations per node.
    let violatingNodeDoc (i: int) : Doc =
        rdf {
            about (describe (Node.Iri $"https://example.org/move{i}") { typ "https://schema.org/MoveAction" })
        }

    let conformingGraph =
        [ for i in 1..20 -> conformingNodeDoc i ] |> List.reduce Doc.merge |> Doc.toGraph :> VDS.RDF.IGraph

    // 20 target nodes x 2 missing properties each = up to 40 Violation records per validate() call.
    let manyViolationsGraph =
        [ for i in 1..20 -> violatingNodeDoc i ] |> List.reduce Doc.merge |> Doc.toGraph :> VDS.RDF.IGraph

    [<Benchmark(Baseline = true)>]
    member _.Conforms() = Shacl.validate shapesGraph conformingGraph

    [<Benchmark>]
    member _.ManyViolations() = Shacl.validate shapesGraph manyViolationsGraph
