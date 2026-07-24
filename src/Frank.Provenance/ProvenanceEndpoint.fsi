module Frank.Provenance.ProvenanceEndpoint

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// GET /provenance?resource=<uri> — return the full lineage batch document.
val handle: store: IProvenanceStore -> config: ProvenanceConfig -> ctx: HttpContext -> Task

/// GET /provenance/{nodeId} — return a focused graph for a single activity or state entity.
/// nodeId starting with "entity-" is a state entity (base64url-encoded resourceUri|k).
/// Any other nodeId is treated as an activity IRI suffix.
val handleNode: store: IProvenanceStore -> config: ProvenanceConfig -> ctx: HttpContext -> Task

/// Extracts the nodeId route value the same way handleNode does — used by the ETagMetadata
/// attached to the per-node route (Frank.Provenance.fs) so the instance id it resolves can
/// never diverge from what handleNode itself resolves (#426).
val resolveNodeId: ctx: HttpContext -> string

/// Computes an ETag for a provenance node by re-running the SAME node-resolution logic
/// handleNode's 200 path uses (#426) — attached as ETagMetadata.Compute on the per-node
/// route so ConditionalRequestMiddleware's 304 short-circuit can never drift from what the
/// handler would actually serve. Returns the raw (unquoted) ETag value.
val computeNodeETag: store: IProvenanceStore -> etagContext: Frank.ETagContext -> Task<string option>

/// Computes an ETag for the lineage batch document by re-running the SAME resolution logic
/// `handle`'s 200 path uses (#426). Returns the raw (unquoted) ETag value.
val computeLineageETag: store: IProvenanceStore -> etagContext: Frank.ETagContext -> Task<string option>
