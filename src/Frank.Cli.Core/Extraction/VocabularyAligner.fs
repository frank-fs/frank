namespace Frank.Cli.Core.Extraction

open System
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.FSharpRdf
open Frank.Cli.Core.Rdf.Vocabularies
open Frank.Cli.Core.Shared

/// Aligns extracted RDF to standard vocabularies (Schema.org, Hydra).
module VocabularyAligner =

    let alignVocabularies (config: TypeMapper.MappingConfig) (graph: IGraph) : IGraph =
        if not (config.Vocabularies |> List.contains "schema.org") then
            graph
        else
            let rdfTypeUri = Uri Rdf.Type
            let datatypePropUri = Uri Owl.DatatypeProperty
            let objectPropUri = Uri Owl.ObjectProperty
            let owlClassUri = Uri Owl.Class
            let equivalentPropUri = Uri Owl.EquivalentProperty
            let equivalentClassUri = Uri Owl.EquivalentClass

            let rdfTypeNode = createUriNode graph rdfTypeUri
            let labelNode = createUriNode graph (Uri Rdfs.Label)

            let typeTriples = triplesWithPredicate graph rdfTypeNode |> Seq.toList

            // Align properties: owl:DatatypeProperty and owl:ObjectProperty → owl:equivalentProperty
            let propertyNodes =
                typeTriples
                |> List.filter (fun t ->
                    match t.Object with
                    | :? IUriNode as on -> on.Uri = datatypePropUri || on.Uri = objectPropUri
                    | _ -> false)
                |> List.map (fun t -> t.Subject)
                |> List.distinct

            for propNode in propertyNodes do
                let labelTriples =
                    triplesWithSubjectPredicate graph propNode labelNode |> Seq.toList

                for labelTriple in labelTriples do
                    match labelTriple.Object with
                    | :? ILiteralNode as lit ->
                        match SchemaAlignment.tryFindIn SchemaAlignment.propertyAlignmentMap lit.Value with
                        | Some schemaUri ->
                            let equivNode = createUriNode graph equivalentPropUri
                            let schemaNode = createUriNode graph (Uri schemaUri)
                            assertTriple graph (propNode, equivNode, schemaNode)
                        | None -> ()
                    | _ -> ()

            // Align classes: owl:Class → owl:equivalentClass
            let classNodes =
                typeTriples
                |> List.filter (fun t ->
                    match t.Object with
                    | :? IUriNode as on -> on.Uri = owlClassUri
                    | _ -> false)
                |> List.map (fun t -> t.Subject)
                |> List.distinct

            for classNode in classNodes do
                let labelTriples =
                    triplesWithSubjectPredicate graph classNode labelNode |> Seq.toList

                for labelTriple in labelTriples do
                    match labelTriple.Object with
                    | :? ILiteralNode as lit ->
                        match SchemaAlignment.tryFindIn SchemaAlignment.classAlignmentMap lit.Value with
                        | Some schemaUri ->
                            let equivNode = createUriNode graph equivalentClassUri
                            let schemaNode = createUriNode graph (Uri schemaUri)
                            assertTriple graph (classNode, equivNode, schemaNode)
                        | None -> ()
                    | _ -> ()

            graph
