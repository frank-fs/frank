namespace Frank.Alps

open System
open Microsoft.AspNetCore.Http

/// Reads bound transition descriptors directly off registered endpoints' metadata -- no
/// ApiExplorer/`Frank.JsonHome` dependency; `binds` (Task 11) already puts the `Descriptor` exactly
/// where this looks.
module EndpointSurface =
    /// Every (Endpoint, Descriptor) pair across every endpoint the DI-registered `EndpointDataSource`
    /// knows about.
    val allDescriptors: services: IServiceProvider -> (Endpoint * Descriptor) list

    /// (Endpoint, Descriptor) pairs restricted to endpoints sharing exactly `routePattern` -- one
    /// resource's several HTTP-method endpoints, each carrying the Descriptor its own `binds` attached.
    val descriptorsForRoute: services: IServiceProvider -> routePattern: string -> (Endpoint * Descriptor) list
