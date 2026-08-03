namespace Frank.Provenance

open System
open Frank.Rdf

type ProvenanceRecord =
    { Activity: Node
      Resource: Node
      Agent: Node
      StartedAt: DateTimeOffset
      EndedAt: DateTimeOffset
      ActivityType: Uri option
      Properties: (string * Value) list }

module ProvenanceRecord =
    let toDoc (record: ProvenanceRecord) : Doc =
        let activityDescription =
            let withProvStatements =
                Prov.activity record.Activity
                |> Prov.wasAssociatedWith record.Agent
                |> Prov.startedAtTime record.StartedAt
                |> Prov.endedAtTime record.EndedAt

            let withDomainType =
                match record.ActivityType with
                | Some iri ->
                    { withProvStatements with
                        Statements = withProvStatements.Statements @ [ RdfTypeIri, Value.Node(Node.Iri iri.AbsoluteUri) ] }
                | None -> withProvStatements

            { withDomainType with
                Statements = withDomainType.Statements @ record.Properties }

        let resourceDescription = Prov.entity record.Resource |> Prov.wasGeneratedBy record.Activity

        let agentDescription = Prov.agent record.Agent

        rdf {
            about activityDescription
            about resourceDescription
            about agentDescription
        }
