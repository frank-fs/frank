namespace Frank.Discovery

open Microsoft.AspNetCore.Routing
open Frank.Builder

/// Validates href-template variables in the JSON Home document by running the existing
/// serializer check before the app starts serving. Reuses homeResourcesFromEndpoints and
/// JsonHomeSerializer.serialize — the existing invalidOp fires on an unresolved variable,
/// naming it. Register via DI as IStartupValidator (done automatically by useDiscoveryWith).
type HrefVarsValidator =
    new: config: DiscoveryConfig -> HrefVarsValidator

    interface IStartupValidator

/// Extensions adding static semantic discovery (JSON Home, ALPS, OPTIONS/Allow,
/// Link rel=describedby) to the Frank WebHostBuilder CE. Consumes a DiscoveryConfig
/// (the MSBuild-generated GeneratedDiscovery module) plus endpoint metadata.
[<AutoOpen>]
module DiscoveryExtensions =

    type WebHostBuilder with

        [<CustomOperation("useDiscoveryWith")>]
        member UseDiscoveryWith: spec: WebHostSpec * config: DiscoveryConfig -> WebHostSpec

        [<CustomOperation("useDiscovery")>]
        member UseDiscovery: spec: WebHostSpec -> WebHostSpec

/// Extends ResourceBuilder with a `relation` operation that stamps
/// ResourceRelationMetadata onto every endpoint built by the resource CE block.
/// Frank.Discovery adds this operation; Frank core is unchanged.
[<AutoOpen>]
module ResourceRelationExtensions =

    type ResourceBuilder with

        /// Stamp the vocabulary IRI as ResourceRelationMetadata on every endpoint
        /// produced by this resource block. The discovery middleware reads this at
        /// runtime to build the JSON Home directory — no static HomeResources needed.
        [<CustomOperation("relation")>]
        member Relation: spec: ResourceSpec * iri: string -> ResourceSpec
