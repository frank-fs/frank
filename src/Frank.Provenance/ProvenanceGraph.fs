module Frank.Provenance.ProvenanceGraph

open System
open VDS.RDF
open Frank.Semantic

let private provContext =
    """{"@context":{"prov":"http://www.w3.org/ns/prov#","http":"http://www.w3.org/2011/http#","rdfs":"http://www.w3.org/2000/01/rdf-schema#"}}"""

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

let toJsonLd (record: ProvenanceRecord) : string =
    RdfSerialization.serializeGraphJsonLdWithContext (toGraph record) provContext

let listToJsonLd (records: ProvenanceRecord list) : string =
    let g = new Graph() :> IGraph

    for r in records do
        g.Merge(toGraph r) |> ignore

    RdfSerialization.serializeGraphJsonLdWithContext g provContext
