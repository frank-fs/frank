namespace Frank.Validation

open System
open System.Collections.Concurrent
open VDS.RDF.Shacl

/// Thread-safe cache of compiled ShapesGraph instances, keyed by the F# type
/// that was used to derive the shape. ShapesGraph is constructed once at startup
/// and reused for every request validation.
type ShapeCache() =

    let cache = ConcurrentDictionary<Type, struct (ShapesGraph * ShaclShape)>()
    let resolvedCache = ConcurrentDictionary<Uri, struct (ShapesGraph * ShaclShape)>()

    /// Get or create a ShapesGraph for the given type. The shape is derived via
    /// ShapeDerivation and converted via ShapeGraphBuilder. Thread-safe: concurrent
    /// calls for the same type will use GetOrAdd semantics.
    member _.GetOrAdd(shapeType: Type) : struct (ShapesGraph * ShaclShape) =
        cache.GetOrAdd(
            shapeType,
            fun t ->
                let shape = ShapeDerivation.deriveShapeDefault t
                let shapesGraph = ShapeGraphBuilder.buildShapesGraph shape
                struct (shapesGraph, shape)
        )

    /// Get or create a ShapesGraph for a resolved ShaclShape (capability-dependent).
    /// Keyed by the shape's NodeShapeUri to avoid rebuilding per-request.
    member _.GetOrAddResolved(shape: ShaclShape) : struct (ShapesGraph * ShaclShape) =
        resolvedCache.GetOrAdd(
            shape.NodeShapeUri,
            fun _ ->
                let shapesGraph = ShapeGraphBuilder.buildShapesGraph shape
                struct (shapesGraph, shape)
        )

    /// Clear all cached shapes. Useful for testing.
    member _.Clear() =
        cache.Clear()
        resolvedCache.Clear()
