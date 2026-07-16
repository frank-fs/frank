module Frank.Provenance.ProvenanceEndpoint

open System.Threading.Tasks
open Microsoft.AspNetCore.Http

/// GET /provenance?resource=<uri> — return the full lineage batch document.
val handle: store: IProvenanceStore -> config: ProvenanceConfig -> ctx: HttpContext -> Task

/// GET /provenance/{nodeId} — return a focused graph for a single activity or state entity.
/// nodeId starting with "entity-" is a state entity (base64url-encoded resourceUri|k).
/// Any other nodeId is treated as an activity IRI suffix.
val handleNode: store: IProvenanceStore -> config: ProvenanceConfig -> ctx: HttpContext -> Task
