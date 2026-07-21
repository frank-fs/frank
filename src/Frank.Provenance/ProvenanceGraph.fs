module Frank.Provenance.ProvenanceGraph

open System
open VDS.RDF
open Newtonsoft.Json.Linq
open Frank.Semantic

// ── Context and compaction utilities ─────────────────────────────────────────

let private toUriOption (node: INode) : Uri option =
    match node with
    | :? IUriNode as n -> Some n.Uri
    | _ -> None

let private graphUriNodes (g: IGraph) : Uri list =
    g.Triples
    |> Seq.collect (fun t ->
        [ toUriOption t.Subject; toUriOption t.Predicate; toUriOption t.Object ]
        |> List.choose id)
    |> Seq.toList

let private tryMatchRelativeNs (storedNs: string) (uris: Uri list) : string option =
    uris
    |> List.tryFind (fun u ->
        let pathAndFrag = u.AbsolutePath + u.Fragment
        pathAndFrag.StartsWith(storedNs, StringComparison.Ordinal))
    |> Option.map (fun u ->
        let port = if u.IsDefaultPort then "" else ":" + string u.Port
        u.Scheme + "://" + u.Host + port + storedNs)

let private tryMatchAbsoluteNs (storedNs: string) (uris: Uri list) : string option =
    uris
    |> List.tryFind (fun u -> u.AbsoluteUri.StartsWith(storedNs, StringComparison.Ordinal))
    |> Option.map (fun _ -> storedNs)

/// Retain only the declared prefixes whose namespace actually prefixes one of the given
/// (already-scanned) graph URI nodes. For host-relative stored namespaces (starting with
/// "/"), the absolute namespace is derived from the matching URI's own scheme+host — no
/// app-owned-vs-external classification is performed.
let private filterUsedPrefixes (declared: (string * string) list) (uris: Uri list) : (string * string) list =
    declared
    |> List.choose (fun (prefix, storedNs) ->
        let tryMatch =
            if storedNs.StartsWith("/", StringComparison.Ordinal) then
                tryMatchRelativeNs storedNs uris
            else
                tryMatchAbsoluteNs storedNs uris

        tryMatch |> Option.map (fun resolvedNs -> prefix, resolvedNs))

/// Build the @context entry list from DeclaredPrefixes, retaining only those whose
/// namespace actually prefixes a URI node in the graph.
let usedPrefixContext (declared: (string * string) list) (g: IGraph) : (string * string) list =
    filterUsedPrefixes declared (graphUriNodes g)

// PROV-O's own namespaces (prov/http/rdfs). #412: run these through usedPrefixContext just
// like app-declared DeclaredPrefixes — no unconditional always-present base context.
let private provDeclaredPrefixes: (string * string) list =
    [ "prov", ProvVocabulary.Namespace
      "http", ProvVocabulary.Http.Namespace
      "rdfs", RdfSerialization.RdfsNamespace ]

/// #424: compute the served @context entries (PROV-O's fixed prefixes ++ the app's
/// DeclaredPrefixes), filtered to prefixes actually used in the graph, from a single shared
/// graphUriNodes walk — instead of ProvenanceEndpoint.serveJsonLd and this filtering each
/// re-scanning the graph's triples independently.
let internal usedContextEntries (declaredPrefixes: (string * string) list) (g: IGraph) : (string * string) list =
    let uris = graphUriNodes g

    filterUsedPrefixes provDeclaredPrefixes uris
    @ filterUsedPrefixes declaredPrefixes uris

/// Compact `graph` to JSON-LD, filtering both PROV-O's fixed prefixes and `extraContext`
/// to only those actually used in the graph (same discipline as compactGraph's #424 fix —
/// no unconditional pass-through of a declared-but-unused @context entry).
let private compact (graph: IGraph) (extraContext: (string * string) list) : string =
    let ctx = JObject()

    for (k, v) in usedContextEntries extraContext graph do
        ctx.[k] <- JToken.op_Implicit v

    RdfSerialization.compactWithContext graph ctx

let private u (g: IGraph) (s: string) =
    g.CreateUriNode(UriFactory.Create s) :> INode

let private lit (g: IGraph) (v: string) (dt: string) =
    g.CreateLiteralNode(v, UriFactory.Create dt) :> INode

let private plain (g: IGraph) (v: string) = g.CreateLiteralNode v :> INode
let private assertT (g: IGraph) s p o = g.Assert(Triple(s, p, o)) |> ignore

let private domainTypeNode (g: IGraph) (record: ProvenanceRecord) (cls: ProvOClass) =
    match record.DomainType with
    | Some(c, iri) when c = cls -> Some(u g iri.AbsoluteUri)
    | _ -> None

// ── IRI minting ───────────────────────────────────────────────────────────────

let private stateKey (resourceUri: string) (k: int) : string =
    let bytes = Text.Encoding.UTF8.GetBytes(sprintf "%s|%d" resourceUri k)
    Convert.ToBase64String(bytes).Replace("+", "-").Replace("/", "_").TrimEnd('=')

/// Mint a dereferenceable IRI for a state entity at position k (k=0 = entity_0).
let stateEntityIri (origin: string) (resourceUri: string) (k: int) : string =
    sprintf "%s/provenance/entity-%s" origin (stateKey resourceUri k)

/// Decode a state entity key (the part after "entity-" in the nodeId) back to
/// (resourceUri, k). Returns None when the key is not valid base64url+pipe encoding.
let tryParseStateEntityKey (key: string) : (string * int) option =
    if isNull key then
        invalidArg (nameof key) "key must not be null"

    let padded =
        let m = key.Length % 4
        if m = 0 then key else key + String.replicate (4 - m) "="

    try
        let bytes = Convert.FromBase64String(padded.Replace("-", "+").Replace("_", "/"))
        let s = Text.Encoding.UTF8.GetString bytes
        let i = s.LastIndexOf '|'

        if i < 0 then
            None
        else
            match Int32.TryParse(s.Substring(i + 1)) with
            | true, k -> Some(s.Substring(0, i), k)
            | _ -> None
    with :? System.FormatException ->
        None

// ── Single-record builders (backward compat + InvokeWithProv) ─────────────────

let private addEntity (g: IGraph) (record: ProvenanceRecord) (entity: INode) (activity: INode) =
    let rdfType = u g ProvVocabulary.Rdf.Type
    assertT g entity rdfType (u g ProvVocabulary.Class.Entity)
    assertT g entity (u g ProvVocabulary.Property.WasGeneratedBy) activity

    domainTypeNode g record ProvOClass.Entity
    |> Option.iter (assertT g entity rdfType)

// priorEntityOpt=None for single-record snapshot (no prior-state context, avoids used-cycle).
// Pass Some(priorNode) for lineage mode where the prior state entity is known.
let private addActivity
    (g: IGraph)
    (record: ProvenanceRecord)
    (activity: INode)
    (agent: INode)
    (priorEntityOpt: INode option)
    =
    let rdfType = u g ProvVocabulary.Rdf.Type
    assertT g activity rdfType (u g ProvVocabulary.Class.Activity)

    domainTypeNode g record ProvOClass.Activity
    |> Option.iter (assertT g activity rdfType)

    assertT
        g
        activity
        (u g ProvVocabulary.Property.StartedAtTime)
        (lit g (record.StartedAt.ToString "o") ProvVocabulary.Xsd.DateTime)

    assertT
        g
        activity
        (u g ProvVocabulary.Property.EndedAtTime)
        (lit g (record.EndedAt.ToString "o") ProvVocabulary.Xsd.DateTime)

    assertT g activity (u g ProvVocabulary.Property.WasAssociatedWith) agent

    match priorEntityOpt with
    | Some prior -> assertT g activity (u g ProvVocabulary.Property.Used) prior
    | None -> ()

    assertT g activity (u g ProvVocabulary.Http.MethodName) (plain g record.HttpMethod)

    assertT
        g
        activity
        (u g ProvVocabulary.Http.StatusCodeValue)
        (lit g (string record.StatusCode) ProvVocabulary.Xsd.Integer)

    for (iri, attrValue) in record.BodyAttributes do
        match attrValue with
        | Literal v -> assertT g activity (u g iri) (plain g v)
        | IriNode valueIri -> assertT g activity (u g iri) (u g valueIri)

let private addAgent (g: IGraph) (record: ProvenanceRecord) (agent: INode) =
    let rdfType = u g ProvVocabulary.Rdf.Type
    assertT g agent rdfType (u g ProvVocabulary.Class.Agent)

    domainTypeNode g record ProvOClass.Agent
    |> Option.iter (assertT g agent rdfType)

    match record.Agent.Label with
    | Some l -> assertT g agent (u g (RdfSerialization.RdfsNamespace + "label")) (plain g l)
    | None -> ()

// Single-record snapshot graph (used by InvokeWithProv content negotiation path).
// Entity = resource URI (the modified resource). No 'used' edge — no prior-state
// context is available in the per-request path; the full chain is in buildLineageGraph.
let toGraph (record: ProvenanceRecord) : IGraph =
    let g = new Graph() :> IGraph
    let entity = u g record.ResourceUri
    let activity = u g record.Id
    let agent = u g record.Agent.Id
    addEntity g record entity activity
    addActivity g record activity agent None
    addAgent g record agent
    g

let toJsonLd (record: ProvenanceRecord) : string = compact (toGraph record) []

let toJsonLdWith (extraContext: (string * string) list) (record: ProvenanceRecord) : string =
    compact (toGraph record) extraContext

// Rule 10: iteration count is bounded by the store's MaxRecords setting upstream.
// No additional runtime cap is applied here to avoid silently truncating output.
let listToJsonLd (extraContext: (string * string) list) (records: ProvenanceRecord list) : string =
    let g = new Graph() :> IGraph

    for r in records do
        g.Merge(toGraph r) |> ignore

    compact g extraContext

let buildMergedGraph (records: ProvenanceRecord list) : IGraph =
    let g = new Graph() :> IGraph

    for r in records do
        g.Merge(toGraph r) |> ignore

    g

// ── Lineage graph builders ────────────────────────────────────────────────────

let private addStateEntityNode
    (g: IGraph)
    (stateNode: INode)
    (gameIriNode: INode)
    (activityNodeOpt: INode option)
    (priorNodeOpt: INode option)
    (domainType: (ProvOClass * Uri) option)
    =
    let rdfType = u g ProvVocabulary.Rdf.Type
    assertT g stateNode rdfType (u g ProvVocabulary.Class.Entity)
    assertT g stateNode (u g ProvVocabulary.Property.SpecializationOf) gameIriNode

    activityNodeOpt
    |> Option.iter (assertT g stateNode (u g ProvVocabulary.Property.WasGeneratedBy))

    priorNodeOpt
    |> Option.iter (assertT g stateNode (u g ProvVocabulary.Property.WasDerivedFrom))

    match domainType with
    | Some(cls, iri) when cls = ProvOClass.Entity -> assertT g stateNode rdfType (u g iri.AbsoluteUri)
    | _ -> ()

let private addLineageStep (g: IGraph) (origin: string) (resourceUri: string) (record: ProvenanceRecord) (k: int) =
    let gameIriNode = u g resourceUri
    let activityNode = u g record.Id
    let agentNode = u g record.Agent.Id
    let stateNode = u g (stateEntityIri origin resourceUri k)
    let priorNode = u g (stateEntityIri origin resourceUri (k - 1))
    addStateEntityNode g stateNode gameIriNode (Some activityNode) (Some priorNode) record.DomainType
    addActivity g record activityNode agentNode (Some priorNode)
    addAgent g record agentNode

/// Build the full PROV-O lineage graph: entity_0 + state_1..N + activities + agents.
/// Produces N+1 prov:Entity nodes and N prov:wasDerivedFrom edges (linear chain).
/// Each activity_k prov:used state_{k-1} (prior state, NOT the generated state).
/// Rule 10: bounded by records.Length (capped upstream by MaxRecords).
let buildLineageGraph (origin: string) (resourceUri: string) (records: ProvenanceRecord list) : IGraph =
    let g = new Graph() :> IGraph
    let gameIriNode = u g resourceUri
    let entity0 = u g (stateEntityIri origin resourceUri 0)
    addStateEntityNode g entity0 gameIriNode None None None

    for i = 0 to records.Length - 1 do
        addLineageStep g origin resourceUri records.[i] (i + 1)

    g

/// Build a focused graph for a single activity node (per-node route response).
/// Includes: activity edges (used/wasAssociatedWith/body attrs), generated state back-link,
/// and agent. posIdx is the 0-based position of the record in its game's ordered list.
let buildActivityNodeGraph (origin: string) (record: ProvenanceRecord) (posIdx: int) : IGraph =
    let g = new Graph() :> IGraph
    let activityNode = u g record.Id
    let agentNode = u g record.Agent.Id
    let priorNode = u g (stateEntityIri origin record.ResourceUri posIdx)
    let generatedNode = u g (stateEntityIri origin record.ResourceUri (posIdx + 1))
    let rdfType = u g ProvVocabulary.Rdf.Type
    addActivity g record activityNode agentNode (Some priorNode)
    assertT g generatedNode rdfType (u g ProvVocabulary.Class.Entity)
    assertT g generatedNode (u g ProvVocabulary.Property.WasGeneratedBy) activityNode
    addAgent g record agentNode
    g

/// Build a focused graph for a single state entity node (per-node route response).
/// k=0: entity_0 (root, no wasGeneratedBy/wasDerivedFrom). k>=1: full edges.
let buildStateEntityNodeGraph
    (origin: string)
    (resourceUri: string)
    (records: ProvenanceRecord list)
    (k: int)
    : IGraph =
    if k < 0 then
        invalidArg (nameof k) "k must be >= 0"

    if k > records.Length then
        invalidArg (nameof k) "k must be <= records.Length"

    let g = new Graph() :> IGraph
    let gameIriNode = u g resourceUri
    let stateNode = u g (stateEntityIri origin resourceUri k)

    if k = 0 then
        addStateEntityNode g stateNode gameIriNode None None None
    else
        let record = records.[k - 1]
        let activityNode = u g record.Id
        let priorNode = u g (stateEntityIri origin resourceUri (k - 1))
        addStateEntityNode g stateNode gameIriNode (Some activityNode) (Some priorNode) record.DomainType

    g

/// Compact `g` to JSON-LD, with `declaredPrefixes` (raw, app-declared, unfiltered) and
/// PROV-O's fixed prefixes both resolved against a single triple walk (#424).
let compactGraph (declaredPrefixes: (string * string) list) (g: IGraph) : string =
    let ctx = JObject()

    for (k, v) in usedContextEntries declaredPrefixes g do
        ctx.[k] <- JToken.op_Implicit v

    RdfSerialization.compactWithContext g ctx
