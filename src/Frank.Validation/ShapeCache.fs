namespace Frank.Validation

open System
open System.Collections.Concurrent
open VDS.RDF.Shacl

/// Thread-safe cache of compiled ShapesGraph instances, keyed by the shape's NodeShapeUri.
/// Shapes are pre-populated at startup via LoadAll (from ShapeLoader or ShapeBuilder),
/// then retrieved per-request via TryGet. No on-demand derivation occurs during request handling.
type ShapeCache() =

    let cache = ConcurrentDictionary<Uri, struct (ShapesGraph * ShaclShape)>()

    /// Pre-populate the cache from a list of ShaclShape values.
    /// Idempotent: re-loading the same URI overwrites the previous entry.
    member _.LoadAll(shapes: ShaclShape list) =
        for shape in shapes do
            let shapesGraph = ShapeGraphBuilder.buildShapesGraph shape
            cache.[shape.NodeShapeUri] <- struct (shapesGraph, shape)

    /// Try to retrieve a compiled ShapesGraph and its originating ShaclShape by URI.
    /// Returns None if the URI has not been pre-loaded.
    member _.TryGet(shapeUri: Uri) : struct (ShapesGraph * ShaclShape) voption =
        match cache.TryGetValue(shapeUri) with
        | true, entry -> ValueSome entry
        | _ -> ValueNone

    /// Get or add a ShapesGraph by deriving the shape at runtime from a System.Type.
    /// Kept for backwards-compat with middleware paths that still carry a Type.
    /// The derived shape is stored keyed by its NodeShapeUri.
    [<System.Obsolete("Use TryGet with pre-populated shapes from ShapeLoader instead. Will be removed when all middleware paths use Uri-keyed lookup.")>]
    member _.GetOrAddDerived(shapeType: Type) : struct (ShapesGraph * ShaclShape) =
        let shape = ShapeBuilder.deriveShapeDefault shapeType

        cache.GetOrAdd(
            shape.NodeShapeUri,
            fun _ ->
                let shapesGraph = ShapeGraphBuilder.buildShapesGraph shape
                struct (shapesGraph, shape)
        )

    /// Get or add a ShapesGraph for a resolved ShaclShape (capability-dependent).
    /// Keyed by the shape's NodeShapeUri to avoid rebuilding per-request.
    member _.GetOrAddResolved(shape: ShaclShape) : struct (ShapesGraph * ShaclShape) =
        cache.GetOrAdd(
            shape.NodeShapeUri,
            fun _ ->
                let shapesGraph = ShapeGraphBuilder.buildShapesGraph shape
                struct (shapesGraph, shape)
        )

    /// All currently cached shape URIs. Used by cross-reference checks (e.g., ProjectionValidator).
    member _.Keys: Uri seq = cache.Keys :> Uri seq

    /// Clear all cached shapes. Useful for testing.
    member _.Clear() = cache.Clear()
