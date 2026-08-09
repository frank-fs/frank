namespace Frank.Provenance

open System
open VDS.RDF
open VDS.RDF.Parsing
open VDS.RDF.Query
open Frank.Rdf

[<Struct>]
[<RequireQualifiedAccess>]
type ProvenanceQuery =
    | ByResource of resourceIri: string
    | ByAgent of agentIri: string
    | ByActivityId of activityIri: string
    | Latest of resourceIri: string

[<RequireQualifiedAccess>]
type SparqlQueryResult =
    | Bindings of SparqlResultSet
    | Graph of IGraph

type IProvenanceStore =
    abstract Append: record: ProvenanceRecord -> unit
    abstract Query: query: ProvenanceQuery -> SparqlQueryResult

[<Struct>]
type ProvenanceStoreConfig =
    { MaxRecords: int
      EvictionBatchSize: int
      SnapshotEvery: int }

module ProvenanceStoreConfig =
    let defaults =
        { MaxRecords = 1000
          EvictionBatchSize = 100
          SnapshotEvery = 100 }

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
                $"""
                CONSTRUCT {{ @resource ?rp ?ro . ?activity ?ap ?ao . }}
                WHERE {{
                    @resource ?rp ?ro .
                    OPTIONAL {{
                        @resource <{ProvRelation.toIri ProvRelation.WasGeneratedBy}> ?activity .
                        ?activity ?ap ?ao .
                    }}
                }}
                """
                "resource"
                resourceIri

        | ProvenanceQuery.ByAgent agentIri ->
            render
                $"""
                CONSTRUCT {{ ?activity ?ap ?ao . }}
                WHERE {{
                    ?activity <{ProvRelation.toIri ProvRelation.WasAssociatedWith}> @agent .
                    ?activity ?ap ?ao .
                }}
                """
                "agent"
                agentIri

        | ProvenanceQuery.ByActivityId activityIri -> render "DESCRIBE @activity" "activity" activityIri

        | ProvenanceQuery.Latest resourceIri ->
            // The resource IRI can be prov:wasGeneratedBy several activities over its lifetime (one per
            // ProvenanceRecord appended for it) -- unlike ByResource, this picks exactly the one whose
            // endedAtTime is most recent, via a subquery (SELECT ... ORDER BY DESC LIMIT 1) that resolves
            // ?activity to a single binding before the outer pattern pulls in the rest of that activity's
            // triples. Doing the ORDER BY/LIMIT directly in the outer CONSTRUCT WHERE would instead cap the
            // number of ?ap/?ao rows returned, truncating the winning activity's own properties.
            render
                $"""
                CONSTRUCT {{
                    @resource <{RdfTypeIri}> <{ProvClass.toIri ProvClass.Entity}> .
                    @resource <{ProvRelation.toIri ProvRelation.WasGeneratedBy}> ?activity .
                    ?activity ?ap ?ao .
                }}
                WHERE {{
                    {{
                        SELECT ?activity WHERE {{
                            @resource <{ProvRelation.toIri ProvRelation.WasGeneratedBy}> ?activity .
                            ?activity <{ProvRelation.toIri ProvRelation.EndedAtTime}> ?ended .
                        }}
                        ORDER BY DESC(?ended)
                        LIMIT 1
                    }}
                    ?activity ?ap ?ao .
                }}
                """
                "resource"
                resourceIri
