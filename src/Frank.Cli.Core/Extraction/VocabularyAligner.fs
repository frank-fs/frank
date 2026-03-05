namespace Frank.Cli.Core.Extraction

open System
open System.Text.RegularExpressions
open VDS.RDF
open Frank.Cli.Core.Rdf
open Frank.Cli.Core.Rdf.Vocabularies

/// Aligns extracted RDF to standard vocabularies (Schema.org, Hydra).
module VocabularyAligner =

    let private splitCamelCase (name: string) =
        Regex.Replace(name, "([a-z])([A-Z])", "$1 $2").ToLowerInvariant()

    let private normalizeFieldName (name: string) =
        splitCamelCase(name).Replace(" ", "").ToLowerInvariant()

    let private alignmentMap : (string list * string) list = [
        (["name"; "title"], SchemaOrg.Name)
        (["description"; "summary"; "body"], SchemaOrg.Description)
        (["email"; "emailaddress"], SchemaOrg.Email)
        (["url"; "uri"; "website"; "homepage"], SchemaOrg.Url)
        (["price"; "cost"; "amount"], SchemaOrg.Price)
        (["createdat"; "datecreated"; "created"], SchemaOrg.DateCreated)
        (["updatedat"; "datemodified"; "modified"], SchemaOrg.DateModified)
        (["image"; "imageurl"; "photo"], SchemaOrg.Image)
        (["telephone"; "phone"], SchemaOrg.Telephone)
    ]

    let private tryFindAlignment (fieldName: string) : string option =
        let normalized = normalizeFieldName fieldName
        alignmentMap
        |> List.tryFind (fun (names, _) -> names |> List.contains normalized)
        |> Option.map snd

    let alignVocabularies (config: TypeMapper.MappingConfig) (graph: IGraph) : IGraph =
        if not (config.Vocabularies |> List.contains "schema.org") then
            graph
        else
            // Find all triples where the predicate is rdfs:label and subject is a property
            // (i.e., subject has rdf:type owl:DatatypeProperty or owl:ObjectProperty)
            let rdfTypeUri = Uri Rdf.Type
            let datatypePropUri = Uri Owl.DatatypeProperty
            let objectPropUri = Uri Owl.ObjectProperty
            let equivalentPropUri = Uri Owl.EquivalentProperty

            let rdfTypeNode = createUriNode graph rdfTypeUri
            let labelNode = createUriNode graph (Uri Rdfs.Label)

            // Collect all property nodes (nodes that are typed as owl:DatatypeProperty or owl:ObjectProperty)
            let propertyNodes =
                triplesWithPredicate graph rdfTypeNode
                |> Seq.filter (fun t ->
                    match t.Object with
                    | :? IUriNode as on ->
                        on.Uri = datatypePropUri || on.Uri = objectPropUri
                    | _ -> false)
                |> Seq.map (fun t -> t.Subject)
                |> Seq.distinct
                |> Seq.toList

            // For each property node, find its rdfs:label and try to align
            for propNode in propertyNodes do
                let labelTriples =
                    triplesWithSubjectPredicate graph propNode labelNode
                    |> Seq.toList

                for labelTriple in labelTriples do
                    match labelTriple.Object with
                    | :? ILiteralNode as lit ->
                        match tryFindAlignment lit.Value with
                        | Some schemaUri ->
                            let equivNode = createUriNode graph equivalentPropUri
                            let schemaNode = createUriNode graph (Uri schemaUri)
                            assertTriple graph (propNode, equivNode, schemaNode)
                        | None -> ()
                    | _ -> ()

            graph
