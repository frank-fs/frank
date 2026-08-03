namespace Frank.Provenance

open System
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Query

[<RequireQualifiedAccess>]
type ProvenanceQuery =
    | ByResource of resourceIri: string
    | ByAgent of agentIri: string
    | ByActivityId of activityIri: string

[<RequireQualifiedAccess>]
type SparqlQueryResult =
    | Bindings of SparqlResultSet
    | Graph of IGraph

type IProvenanceStore =
    abstract Append: record: ProvenanceRecord -> unit
    abstract Query: query: ProvenanceQuery -> SparqlQueryResult

type ProvenanceStoreConfig =
    { MaxRecords: int
      EvictionBatchSize: int }

module ProvenanceStoreConfig =
    let defaults = { MaxRecords = 1000; EvictionBatchSize = 100 }

[<AutoOpen>]
module ProvenanceStore =
    let internal toSparqlQuery (query: ProvenanceQuery) : SparqlQuery =
        let parser = SparqlQueryParser()

        let render (commandText: string) (paramName: string) (iri: string) : SparqlQuery =
            let qs = SparqlParameterizedString(commandText)
            qs.SetUri(paramName, Uri iri)
            parser.ParseFromString(qs.ToString())

        match query with
        | ProvenanceQuery.ByResource resourceIri ->
            render
                """
                CONSTRUCT { @resource ?rp ?ro . ?activity ?ap ?ao . }
                WHERE {
                    @resource ?rp ?ro .
                    OPTIONAL {
                        @resource <http://www.w3.org/ns/prov#wasGeneratedBy> ?activity .
                        ?activity ?ap ?ao .
                    }
                }
                """
                "resource"
                resourceIri

        | ProvenanceQuery.ByAgent agentIri ->
            render
                """
                CONSTRUCT { ?activity ?ap ?ao . }
                WHERE {
                    ?activity <http://www.w3.org/ns/prov#wasAssociatedWith> @agent .
                    ?activity ?ap ?ao .
                }
                """
                "agent"
                agentIri

        | ProvenanceQuery.ByActivityId activityIri -> render "DESCRIBE @activity" "activity" activityIri
