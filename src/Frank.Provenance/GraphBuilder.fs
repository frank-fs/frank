namespace Frank.Provenance

open System
open VDS.RDF

/// Converts ProvenanceRecord lists into dotNetRdf PROV-O RDF graphs.
[<RequireQualifiedAccess>]
module GraphBuilder =

    let private uriNode (graph: IGraph) (uri: string) =
        graph.CreateUriNode(UriFactory.Create(uri))

    let private literalNode (graph: IGraph) (value: string) (datatypeUri: string) =
        graph.CreateLiteralNode(value, UriFactory.Create(datatypeUri))

    let private plainLiteral (graph: IGraph) (value: string) = graph.CreateLiteralNode(value)

    let private assertTriple (graph: IGraph) (s: INode) (p: INode) (o: INode) = graph.Assert(Triple(s, p, o)) |> ignore

    let private registerPrefixes (graph: IGraph) =
        graph.NamespaceMap.AddNamespace("prov", UriFactory.Create(ProvVocabulary.Namespace))
        graph.NamespaceMap.AddNamespace("frank", UriFactory.Create(ProvVocabulary.FrankNamespace))
        graph.NamespaceMap.AddNamespace("xsd", UriFactory.Create(ProvVocabulary.XsdNamespace))

        graph.NamespaceMap.AddNamespace("rdf", UriFactory.Create(ProvVocabulary.RdfNamespace))
        graph.NamespaceMap.AddNamespace("rdfs", UriFactory.Create(ProvVocabulary.RdfsNamespace))

    let private addAgent (graph: IGraph) (agent: ProvenanceAgent) =
        let agentNode = uriNode graph agent.Id
        let rdfType = uriNode graph ProvVocabulary.Rdf.Type
        let rdfsLabel = uriNode graph ProvVocabulary.Rdfs.label

        match agent.AgentType with
        | AgentType.Person(name, _) ->
            assertTriple graph agentNode rdfType (uriNode graph ProvVocabulary.Class.Person)
            assertTriple graph agentNode rdfsLabel (plainLiteral graph name)
        | AgentType.SoftwareAgent identifier ->
            assertTriple graph agentNode rdfType (uriNode graph ProvVocabulary.Class.SoftwareAgent)
            assertTriple graph agentNode rdfsLabel (plainLiteral graph identifier)
        | AgentType.LlmAgent(identifier, model) ->
            assertTriple graph agentNode rdfType (uriNode graph ProvVocabulary.Class.SoftwareAgent)
            assertTriple graph agentNode rdfType (uriNode graph ProvVocabulary.Frank.LlmAgent)
            assertTriple graph agentNode rdfsLabel (plainLiteral graph identifier)

            match model with
            | Some m ->
                assertTriple graph agentNode (uriNode graph ProvVocabulary.Frank.agentModel) (plainLiteral graph m)
            | None -> ()

    let private addActivity (graph: IGraph) (record: ProvenanceRecord) =
        let activity = record.Activity
        let activityNode = uriNode graph activity.Id
        let rdfType = uriNode graph ProvVocabulary.Rdf.Type

        assertTriple graph activityNode rdfType (uriNode graph ProvVocabulary.Class.Activity)

        assertTriple
            graph
            activityNode
            (uriNode graph ProvVocabulary.Property.StartedAtTime)
            (literalNode graph (activity.StartedAt.ToString("o")) ProvVocabulary.Xsd.DateTime)

        assertTriple
            graph
            activityNode
            (uriNode graph ProvVocabulary.Property.EndedAtTime)
            (literalNode graph (activity.EndedAt.ToString("o")) ProvVocabulary.Xsd.DateTime)

        assertTriple
            graph
            activityNode
            (uriNode graph ProvVocabulary.Property.WasAssociatedWith)
            (uriNode graph record.Agent.Id)

        assertTriple
            graph
            activityNode
            (uriNode graph ProvVocabulary.Property.Used)
            (uriNode graph record.UsedEntity.Id)

        assertTriple
            graph
            activityNode
            (uriNode graph ProvVocabulary.Frank.httpMethod)
            (plainLiteral graph activity.HttpMethod)

        assertTriple
            graph
            activityNode
            (uriNode graph ProvVocabulary.Frank.eventName)
            (plainLiteral graph activity.EventName)

    let private addUsedEntity (graph: IGraph) (record: ProvenanceRecord) =
        let entity = record.UsedEntity
        let entityNode = uriNode graph entity.Id
        let rdfType = uriNode graph ProvVocabulary.Rdf.Type

        assertTriple graph entityNode rdfType (uriNode graph ProvVocabulary.Class.Entity)

        assertTriple
            graph
            entityNode
            (uriNode graph ProvVocabulary.Property.WasAttributedTo)
            (uriNode graph record.Agent.Id)

        assertTriple
            graph
            entityNode
            (uriNode graph ProvVocabulary.Frank.stateName)
            (plainLiteral graph entity.StateName)

    let private addGeneratedEntity (graph: IGraph) (record: ProvenanceRecord) =
        let entity = record.GeneratedEntity
        let entityNode = uriNode graph entity.Id
        let rdfType = uriNode graph ProvVocabulary.Rdf.Type

        assertTriple graph entityNode rdfType (uriNode graph ProvVocabulary.Class.Entity)

        assertTriple
            graph
            entityNode
            (uriNode graph ProvVocabulary.Property.WasGeneratedBy)
            (uriNode graph record.Activity.Id)

        assertTriple
            graph
            entityNode
            (uriNode graph ProvVocabulary.Property.WasAttributedTo)
            (uriNode graph record.Agent.Id)

        assertTriple
            graph
            entityNode
            (uriNode graph ProvVocabulary.Property.WasDerivedFrom)
            (uriNode graph record.UsedEntity.Id)

        assertTriple
            graph
            entityNode
            (uriNode graph ProvVocabulary.Frank.stateName)
            (plainLiteral graph entity.StateName)

    /// Converts a list of ProvenanceRecords into a PROV-O RDF graph.
    let toGraph (records: ProvenanceRecord list) : IGraph =
        let graph = new Graph()
        registerPrefixes graph

        for record in records do
            addActivity graph record
            addAgent graph record.Agent
            addUsedEntity graph record
            addGeneratedEntity graph record

        graph
