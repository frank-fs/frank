module Frank.Provenance.ProvenanceGraph

open System
open VDS.RDF
open Newtonsoft.Json.Linq
open Frank.Semantic

let private provContextObj =
    JObject.Parse(
        """{"prov":"http://www.w3.org/ns/prov#","http":"http://www.w3.org/2011/http#","rdfs":"http://www.w3.org/2000/01/rdf-schema#"}"""
    )

let private compact (graph: IGraph) (extraContext: (string * string) list) : string =
    let ctx =
        if List.isEmpty extraContext then
            provContextObj.DeepClone() :?> JObject
        else
            let merged = provContextObj.DeepClone() :?> JObject

            for (k, v) in extraContext do
                merged.[k] <- JToken.op_Implicit v

            merged

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

let private addEntity (g: IGraph) (record: ProvenanceRecord) (entity: INode) (activity: INode) =
    let rdfType = u g ProvVocabulary.Rdf.Type
    assertT g entity rdfType (u g ProvVocabulary.Class.Entity)
    assertT g entity (u g ProvVocabulary.Property.WasGeneratedBy) activity

    domainTypeNode g record ProvOClass.Entity
    |> Option.iter (assertT g entity rdfType)

let private addActivity (g: IGraph) (record: ProvenanceRecord) (activity: INode) (agent: INode) (entity: INode) =
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
    assertT g activity (u g ProvVocabulary.Property.Used) entity
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
    | Some l -> assertT g agent (u g "http://www.w3.org/2000/01/rdf-schema#label") (plain g l)
    | None -> ()

let toGraph (record: ProvenanceRecord) : IGraph =
    let g = new Graph() :> IGraph
    let entity = u g record.ResourceUri
    let activity = u g record.Id
    let agent = u g record.Agent.Id
    addEntity g record entity activity
    addActivity g record activity agent entity
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

/// Build the @context entry list from DeclaredPrefixes, retaining only those whose
/// namespace actually prefixes a URI node in the graph. For host-relative stored namespaces
/// (starting with "/"), the absolute namespace is derived from the matching graph URI's own
/// scheme+host — no app-owned-vs-external classification is performed.
let usedPrefixContext (declared: (string * string) list) (g: IGraph) : (string * string) list =
    let uris = graphUriNodes g

    declared
    |> List.choose (fun (prefix, storedNs) ->
        let tryMatch =
            if storedNs.StartsWith("/", StringComparison.Ordinal) then
                tryMatchRelativeNs storedNs uris
            else
                tryMatchAbsoluteNs storedNs uris

        tryMatch |> Option.map (fun resolvedNs -> prefix, resolvedNs))

let compactGraph (extraCtx: (string * string) list) (g: IGraph) : string = compact g extraCtx
