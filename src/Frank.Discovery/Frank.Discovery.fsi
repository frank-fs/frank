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

        /// Registers static semantic discovery (JSON Home, ALPS, OPTIONS/Allow, Link
        /// rel=describedby) for live ALPS Type correlation against real registered HTTP
        /// methods (#397/#411). Correlation reads Frank's own composed Endpoint[]
        /// directly (via the narrow ResourceEndpointDataSource WebHostBuilder.Run
        /// registers) — no reflection walk, no ApiExplorer/Microsoft.AspNetCore.OpenApi
        /// dependency.
        [<CustomOperation("useDiscoveryWith")>]
        member UseDiscoveryWith: spec: WebHostSpec * config: DiscoveryConfig -> WebHostSpec

        [<CustomOperation("useDiscovery")>]
        member UseDiscovery: spec: WebHostSpec -> WebHostSpec

/// Extends ResourceBuilder with a `relation` operation that stamps
/// ResourceRelationMetadata onto EVERY endpoint built by the resource CE block, regardless
/// of HTTP verb — the same resource-block-level scope as `name`/`entryPoint`, not per-verb.
/// A resource may declare more than one relation to type the resource itself with more than
/// one vocabulary class — each declaration adds its OWN ResourceRelationMetadata instance
/// rather than overwriting a prior one (#433). Per-verb relation scoping (different verbs
/// embodying different classes) is tracked separately (#470). Frank.Discovery adds this
/// operation; Frank core is unchanged.
[<AutoOpen>]
module ResourceRelationExtensions =

    type ResourceBuilder with

        /// Stamp the vocabulary IRI as ResourceRelationMetadata on every endpoint
        /// produced by this resource block. The discovery middleware reads this at
        /// runtime to build the JSON Home directory — no static HomeResources needed.
        /// Composes with any other `relation` call in the same resource block (#433):
        /// calling this more than once accumulates one metadata instance per call.
        [<CustomOperation("relation")>]
        member Relation: spec: ResourceSpec * iri: string -> ResourceSpec

        /// Stamp MULTIPLE vocabulary IRIs in one call — one ResourceRelationMetadata
        /// instance per IRI, in list order (#433). Equivalent to calling the single-IRI
        /// `relation` overload once per list entry; reuses it directly rather than
        /// duplicating the validation/stamping logic (Constitution rule 8).
        [<CustomOperation("relation")>]
        member Relation: spec: ResourceSpec * iris: string list -> ResourceSpec
