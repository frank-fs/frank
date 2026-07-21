module Frank.Provenance.ProvenanceGraph

open VDS.RDF

/// Decode a state entity key (the part after "entity-" in the nodeId) back to
/// (resourceUri, k). Returns None when the key is not valid base64url+pipe encoding.
val tryParseStateEntityKey: key: string -> (string * int) option

val toJsonLd: record: ProvenanceRecord -> string

// Rule 10: iteration count is bounded by the store's MaxRecords setting upstream.
// No additional runtime cap is applied here to avoid silently truncating output.
val listToJsonLd: extraContext: (string * string) list -> records: ProvenanceRecord list -> string

/// Build the full PROV-O lineage graph: entity_0 + state_1..N + activities + agents.
/// Produces N+1 prov:Entity nodes and N prov:wasDerivedFrom edges (linear chain).
/// Each activity_k prov:used state_{k-1} (prior state, NOT the generated state).
/// Rule 10: bounded by records.Length (capped upstream by MaxRecords).
val buildLineageGraph: origin: string -> resourceUri: string -> records: ProvenanceRecord list -> IGraph

/// Build a focused graph for a single activity node (per-node route response).
/// Includes: activity edges (used/wasAssociatedWith/body attrs), generated state back-link,
/// and agent. posIdx is the 0-based position of the record in its game's ordered list.
val buildActivityNodeGraph: origin: string -> record: ProvenanceRecord -> posIdx: int -> IGraph

/// Build a focused graph for a single state entity node (per-node route response).
/// k=0: entity_0 (root, no wasGeneratedBy/wasDerivedFrom). k>=1: full edges.
val buildStateEntityNodeGraph:
    origin: string -> resourceUri: string -> records: ProvenanceRecord list -> k: int -> IGraph

/// Build the @context entry list from DeclaredPrefixes, retaining only those whose
/// namespace actually prefixes a URI node in the graph. For host-relative stored namespaces
/// (starting with "/"), the absolute namespace is derived from the matching graph URI's own
/// scheme+host — no app-owned-vs-external classification is performed.
val usedPrefixContext: declared: (string * string) list -> g: IGraph -> (string * string) list

/// #424: PROV-O's fixed prefixes ++ declaredPrefixes, filtered to those used in the graph,
/// computed from a single shared triple walk. Internal — used by compactGraph and by
/// Frank.Provenance.Tests to verify the single-scan behavior.
val internal usedContextEntries: declaredPrefixes: (string * string) list -> g: IGraph -> (string * string) list

/// Compact `g` to JSON-LD; `declaredPrefixes` is the raw (unfiltered) app-declared prefix
/// list — filtering against both it and PROV-O's fixed prefixes happens internally from one
/// triple walk (#424).
val compactGraph: declaredPrefixes: (string * string) list -> g: IGraph -> string
